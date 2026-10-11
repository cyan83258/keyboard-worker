using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

// One line of an exchange between the two pets. Second = said by the second pet.
public sealed record DuoTurn(bool Second, string Text, string Mood);
// Moods are not part of equality: two replies with the same lines are the same reply.
public sealed record DuoReply(string First, string Second)
{
    public string FirstMood { get; init; } = "neutral"; public string SecondMood { get; init; } = "neutral";
    // The whole exchange in order. Two lines unless more were asked for.
    public IReadOnlyList<DuoTurn> Turns { get; init; } = Array.Empty<DuoTurn>();
    public bool Equals(DuoReply? other) => other != null && First == other.First && Second == other.Second;
    public override int GetHashCode() => HashCode.Combine(First, Second);
}
public static class DuoInteraction
{
    public const int MaxTurns = 4;
    static string Cut(string value) => value.Length > 400 ? value[..397] + "…" : value;
    static string MoodOf(JsonElement item, string key, string line) => item.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String && Array.IndexOf(PetMood.Moods, property.GetString()!.Trim().ToLowerInvariant()) >= 0 ? PetMood.Normalize(property.GetString()) : PetMood.Guess(line);
    public static DuoReply Parse(string text)
    {
        text = text.Trim(); if (text.StartsWith("```")) { int line = text.IndexOf('\n'); if (line >= 0) text = text[(line + 1)..]; if (text.EndsWith("```")) text = text[..^3]; }
        using var json = JsonDocument.Parse(text); var root = json.RootElement;
        // {"turns":[{"speaker":"first","text":"…","mood":"…"}, …]}: an exchange of several lines.
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("turns", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            var turns = new List<DuoTurn>();
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || turns.Count >= MaxTurns) continue;
                string line = item.TryGetProperty("text", out var said) && said.ValueKind == JsonValueKind.String ? said.GetString()!.Trim() : ""; if (line.Length == 0) continue;
                string who = item.TryGetProperty("speaker", out var speaker) && speaker.ValueKind == JsonValueKind.String ? speaker.GetString()!.Trim().ToLowerInvariant() : "";
                turns.Add(new(who == "second", Cut(line), MoodOf(item, "mood", line)));
            }
            var one = turns.FirstOrDefault(t => !t.Second); var two = turns.FirstOrDefault(t => t.Second);
            if (one == null || two == null) throw new InvalidOperationException("두 캐릭터의 대사가 모두 필요합니다.");
            return new(one.Text, two.Text) { FirstMood = one.Mood, SecondMood = two.Mood, Turns = turns };
        }
        string Line(string key)
        {
            string value = root.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString()!.Trim() : "";
            if (value.Length == 0) throw new InvalidOperationException("두 캐릭터의 대사가 모두 필요합니다."); return Cut(value);
        }
        string first = Line("first"), second = Line("second"); string firstMood = MoodOf(root, "first_mood", first), secondMood = MoodOf(root, "second_mood", second);
        return new(first, second) { FirstMood = firstMood, SecondMood = secondMood, Turns = new DuoTurn[] { new(false, first, firstMood), new(true, second, secondMood) } };
    }
    // Asked for more than two lines, the answer is a list of turns instead of one line each.
    public static string TurnsInstruction(int turns) => "\n[주고받는 대화]\n이번에는 두 캐릭터가 번갈아 모두 " + turns + "번 말하는 짧은 대화를 만드세요. 각 대사는 1~2문장이고, 앞 대사에 자연스럽게 반응하며 이어져야 합니다. 누가 먼저 말할지는 상황에 맞게 정하세요.\n위에 다른 JSON 형식이 적혀 있어도 최종 출력은 이 JSON 하나입니다: {\"turns\":[{\"speaker\":\"first\",\"text\":\"대표 친구 대사\",\"mood\":\"표정\"},{\"speaker\":\"second\",\"text\":\"둘째 친구 대사\",\"mood\":\"표정\"}, …]} (speaker는 first=대표 친구, second=둘째 친구)";
    public static async Task<DuoReply> Generate(ChatPreferences preferences, Persona second, DuoOptions options, string last, ChatTurn[] history, CancellationToken cancellation, AiClient? client = null, string usageKind = "AutoDuo", int turns = 2)
    {
        var settings = preferences.Snapshot(); settings.UsageKind = usageKind; string firstProfile = CharacterPrompt.Build(settings.Profile, false), secondProfile = CharacterPrompt.Build(second, false);
        turns = Math.Clamp(turns, 2, MaxTurns);
        string user = PromptTemplates.Expand(options.Prompt, new Dictionary<string, string> { ["first_name"] = settings.Profile.Name, ["second_name"] = second.Name, ["last_line"] = last.Length == 0 ? "없음" : last }) + PetMood.PairInstruction + (turns > 2 ? TurnsInstruction(turns) : "");
        settings.Profile.Prompts = new PromptTemplates { Profile = "{extra}" };
        settings.Profile.Extra = "두 캐릭터의 대사를 한 번에 생성한다. 아래 각 프로필은 역할별 참고 자료다. 사용자 화면·입력 문자열을 보았다고 말하지 말고 각 말투를 지킨다. 두 캐릭터의 말투가 서로 닮아 가지 않게 한다.\n[대표 친구 프로필]\n" + firstProfile + "\n[둘째 친구 프로필]\n" + secondProfile + CharacterPrompt.OwnVoice;
        settings.Streaming = false;
        settings.AutoContinue = false;
        if (!usageKind.StartsWith("Chat")) { settings.MaxOutputTokens = Math.Max(settings.MaxOutputTokens, AutomaticGenerator.OutputTokens); settings.RequestTimeoutSeconds = Math.Min(settings.RequestTimeoutSeconds, 45); }
        string reply = await (client ?? new AiClient()).Send(settings, settings.Automatic.UseConversation ? history : Array.Empty<ChatTurn>(), user, cancellation);
        cancellation.ThrowIfCancellationRequested();
        return Parse(reply);
    }
}
