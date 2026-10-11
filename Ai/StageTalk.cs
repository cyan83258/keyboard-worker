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
        "\n[캐릭터 페이지의 대화]\n" + PromptTemplates.Expand(options.Prompt, new Dictionary<string, string> { ["name"] = persona.Name, ["user_name"] = persona.UserName ?? "", ["length"] = StageTalkOptions.Guide(mode.Length.Length > 0 ? mode.Length : options.Length) }).Trim() + mode.Prompt;
    public static async Task<StageReply> Send(StageLog log, ChatPreferences preferences, Persona persona, string thread, string text, string state, StageScene scene, StageTalkOptions options, StageMode mode, CancellationToken cancellation, Action<string>? progress = null, AiClient? client = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 6000) throw new InvalidOperationException("메시지는 1~6000자로 입력해 주세요.");
        var settings = ChatRepository.Clone(preferences); settings.Profile = persona.Snapshot(); settings.Profile.Prompts = (settings.Profile.Prompts ?? new()).Normalize(); settings.UsageKind = "ChatStage";
        settings.MaxOutputTokens = options.MaxTokens; string name = settings.Profile.Name; bool pictured = mode.Layout == StageMode.Stage;
        settings.Profile.Extra += Instruction(options, mode, settings.Profile) + state + scene.Note + (pictured ? StageTags.Instruction(scene.Pictures) + StageTags.BackdropInstruction(scene.Backdrops, scene.Backdrop) : "");
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
