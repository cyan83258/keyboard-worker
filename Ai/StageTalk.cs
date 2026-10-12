using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

// Backdrop: the title of the background the character moved the scene to ("" = stay).
public sealed record StageReply(ChatTurn Turn, string Tag, string Mood, string Backdrop);
// What a request on a page may choose from, and what just happened there.
public sealed record StageScene(IReadOnlyList<string> Pictures, IReadOnlyList<string> Backdrops, string Backdrop, IReadOnlyList<string> Others, string Note = "");

// A talk on a character's page: this one character answers in its own voice with the page's own prompt and length, names the
// standing picture that fits, and may move the scene to another background. These talks have their own history (StageLog);
// the ordinary chat with the desktop pets is neither shown to the model here nor written to.
public static class StageTalk
{
    // The earlier exchanges of this thread, without names in front of the lines.
    public static ChatTurn[] Context(StageLog log, string thread, string name, IReadOnlyList<string> others, int turns) =>
        log.Turns(thread).TakeLast(Math.Max(0, turns)).Select(t => t with { Assistant = OwnLine(t.Assistant, name, others) }).ToArray();
    // Takes "이름:" off the front of a line, and cuts the answer where another character starts talking.
    public static string OwnLine(string text, string name, IReadOnlyList<string> others)
    {
        text = text.Trim(); var timeout = TimeSpan.FromMilliseconds(100);
        if (name.Length > 0) text = Regex.Replace(text, @"(^|\n)[ \t]*" + Regex.Escape(name) + @"[ \t]*[:：][ \t]*", "$1", RegexOptions.CultureInvariant, timeout);
        foreach (string other in others.Where(o => o.Length > 0 && o != name))
        {
            var match = Regex.Match(text, @"(^|\n)[ \t]*" + Regex.Escape(other) + @"[ \t]*[:：]", RegexOptions.CultureInvariant, timeout);
            if (match.Success && match.Index > 0) text = text[..match.Index];
        }
        return text.Trim();
    }
    // The page's instruction: its prompt with the names and the wanted length filled in, then what the mode adds.
    public static string Instruction(StageTalkOptions options, StageMode mode, Persona persona) =>
(mode.Layout == StageMode.Messenger ? "\n[문자 메시지]\n" : "\n[캐릭터 페이지의 대화]\n") + PromptTemplates.Expand(options.Prompt, new Dictionary<string, string> { ["name"] = persona.Name, ["user_name"] = persona.UserName ?? "", ["length"] = StageTalkOptions.Guide(mode.Length.Length > 0 ? mode.Length : options.Length) }).Trim() + mode.Prompt;
    // A picture character (😊, ❤): outside the basic plane, or one of the symbol blocks that are drawn as emoji.
    // Hearts, stars and notes drawn as plain type (♡ ★ ♪) are part of many characters' way of writing and do not count.
    public static bool HasEmoji(string text) { foreach (char c in text) if (char.IsSurrogate(c) || (c >= 0x2600 && c <= 0x27BF && !"♡♥★☆♪♬♩♫".Contains(c)) || c == 0xFE0F) return true; return false; }
    // What text messages can and cannot be, whatever the user's prompt says. slang: ㅋㅋ, ^^ and the like may be used.
    // Emoji are held back by looking at the last answer: one that had them is followed by one without, so they never become a habit.
    public static string TextRules(bool slang, IReadOnlyList<ChatTurn> earlier) =>
        "\n[문자 메시지의 규칙]"
        + "\n- 두 사람은 지금 서로 다른 곳에 있고, 오가는 것은 휴대폰 문자뿐입니다. 캐릭터도 이것이 문자 대화라는 것을 알고 있습니다."
        + "\n- 문자로는 할 수 없는 일을 하지 않습니다: 쓰다듬기·안기·손잡기 같은 접촉, 상대의 표정이나 모습을 보는 것, 이 대화 중에 찾아가거나 눈앞에 나타나는 것. 만나자는 약속이나 보고 싶다는 말은 할 수 있지만, 실제로 만난 것처럼 쓰지 않습니다."
        + "\n- (괄호) 지문과 행동 묘사를 쓰지 않습니다. 보낼 수 있는 것은 글자와 이모티콘뿐입니다."
        + (slang ? "\n- 문자에서만 쓰는 표현(ㅋㅋ, ㅎㅎ, ㅠㅠ, ^^, ;; 등)은 이 캐릭터의 성격·나이·말투에 어울릴 때만, 어울리는 만큼 씁니다. 어울리지 않는 캐릭터는 쓰지 않습니다."
                 : "\n- ㅋㅋ, ㅎㅎ, ㅠㅠ, ^^, ;; 같은 문자 전용 표현은 쓰지 않습니다. 평소 말투 그대로 글로 씁니다.")
        + "\n- 이모티콘(😊 같은 그림 문자)은 가끔만 씁니다. 사용자가 이모티콘을 보냈다고 따라 붙이지 않고, 대부분의 답장은 이모티콘 없이 씁니다."
        + (earlier.Count > 0 && HasEmoji(earlier[^1].Assistant) ? "\n- 바로 앞 답장에 이모티콘을 썼습니다. 이번 답장에는 이모티콘을 하나도 쓰지 않습니다." : "");
    public static async Task<StageReply> Send(StageLog log, ChatPreferences preferences, Persona persona, string thread, string text, string state, StageScene scene, StageTalkOptions options, StageMode mode, CancellationToken cancellation, Action<string>? progress = null, AiClient? client = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 6000) throw new InvalidOperationException("메시지는 1~6000자로 입력해 주세요.");
        var settings = ChatRepository.Clone(preferences); settings.Profile = persona.Snapshot(); settings.Profile.Prompts = (settings.Profile.Prompts ?? new()).Normalize(); settings.UsageKind = "ChatStage";
        settings.MaxOutputTokens = options.MaxTokens; string name = settings.Profile.Name; bool pictured = mode.Layout == StageMode.Stage;
        settings.Profile.Extra += Instruction(options, mode, settings.Profile) + (pictured ? "" : TextRules(options.Slang, log.Turns(thread))) + state + scene.Note + (pictured ? StageTags.Instruction(scene.Pictures) + StageTags.BackdropInstruction(scene.Backdrops, scene.Backdrop) : "");
        string answer = await (client ?? new AiClient()).Send(settings, Context(log, thread, name, scene.Others, options.ContextTurns), text.Trim(), cancellation, progress == null ? null : partial => progress(Shown(partial, name)));
        cancellation.ThrowIfCancellationRequested();
        // The model may still add the expression mark it knows from ordinary chat; that goes as well.
        var (line, tag, backdrop) = StageTags.Read(answer); line = OwnLine(PetMood.Parse(line).Text, name, scene.Others);
        if (line.Length == 0) throw new InvalidOperationException("대사 없이 표식만 도착했습니다. 다시 시도해 주세요.");
        var turn = new ChatTurn(text.Trim(), line, DateTimeOffset.Now, name, thread, mode.Name); log.Add(thread, turn);
        return new(turn, tag, PetMood.Guess(StageScript.Speech(line)), backdrop);
    }
    // What to show of an answer that is still arriving.
    public static string Shown(string partial, string name) => OwnLine(PetMood.DisplayText(StageTags.Display(partial)), name, Array.Empty<string>());
}
