using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

// One line of a scene for two. Second: said by the second character. Both: the two are on screen together.
public sealed record PairLine(bool Second, string Text, string Sprite = "", bool Both = true);
public sealed record PairScene(IReadOnlyList<PairLine> Lines, string Backdrop);
// The two characters of a scene, what each may be shown as, and how they relate.
public sealed record PairCast(Persona First, Persona Second, IReadOnlyList<string> FirstPictures, IReadOnlyList<string> SecondPictures, string Relation, IReadOnlyList<string> Backdrops, string Backdrop);

// A scene between two characters with the user absent. The user only directs: nothing (go on), a situation, or a line
// for one of the two. The model writes the whole exchange in one answer; the page then plays it line by line.
public static class PairStageTalk
{
    public const string Continue = "continue", Situation = "situation", AsFirst = "first", AsSecond = "second";
    public static int Turns(string length) => length switch { "매우 짧게" => 2, "짧게" => 3, "길게" => 6, "매우 길게" => 8, _ => 4 };
    // What the model is told the user did.
    public static string Direction(string kind, string text, string firstName, string secondName)
    {
        text = text.Trim();
        if (kind is AsFirst or AsSecond && text.Length > 0)
        {
            string who = kind == AsFirst ? firstName : secondName, other = kind == AsFirst ? secondName : firstName;
            return $"[{who}의 대사·행동 — 이미 정해진 것]\n{text}\n\n이 대사는 방금 {who}이(가) 한 것입니다. 되풀이하지 말고, {other}의 반응부터 장면을 이어 가세요.";
        }
        return kind == Situation && text.Length > 0 ? "[상황]\n" + text + "\n\n이 상황에서 두 사람의 장면을 시작하거나 이어 가세요." : "(장면을 자연스럽게 이어 가세요.)";
    }
    public static string Instruction(StageTalkOptions options, PairCast cast)
    {
        string first = cast.First.Name, second = cast.Second.Name;
        string body = PromptTemplates.Expand(options.PairPrompt, new Dictionary<string, string> { ["first_name"] = first, ["second_name"] = second, ["length"] = StageTalkOptions.Guide(options.Length), ["turns"] = Turns(options.Length).ToString() }).Trim();
        string pictures = (cast.FirstPictures.Count > 0 ? $"\n{first}의 그림: " + string.Join(", ", cast.FirstPictures) : "") + (cast.SecondPictures.Count > 0 ? $"\n{second}의 그림: " + string.Join(", ", cast.SecondPictures) : "");
        return "\n[둘만의 장면]\n" + body + cast.Relation
            + "\n[출력 형식]\n다른 말 없이 JSON 하나만 출력합니다: {\"lines\":[{\"who\":\"first\",\"text\":\"대사\",\"sprite\":\"그림 이름\",\"view\":\"both\"}],\"bg\":\"\"}"
            + $"\n- who: first = {first}, second = {second}. 대사 안에 이름이나 '이름:'을 붙이지 않습니다."
            + (pictures.Length > 0 ? "\n- sprite: 그 대사의 감정·상황에 어울리는 그 인물의 그림 이름 (아래 목록에서만, 없으면 빈 문자열)." + pictures : "\n- sprite: 빈 문자열.")
            + "\n- view: 두 사람이 함께 화면에 보이면 \"both\", 한 사람만 비추는 것이 어울리면(독백, 클로즈업, 상대가 자리에 없을 때) \"solo\"."
            + (cast.Backdrops.Count > 1 ? "\n- bg: 장면이 끝난 시점의 장소와 시간대에 가장 가까운 배경 이름(괄호 앞부분 그대로). 두 사람이 다른 곳으로 가거나 시간대가 바뀌면 그 장소의 배경을, 그대로면 지금 배경(" + (cast.Backdrop.Length == 0 ? "없음" : cast.Backdrop) + ")을 적습니다. 고를 수 있는 배경: " + BackdropWords.Listed(cast.Backdrops) : "\n- bg: 빈 문자열.");
    }
    // Reads the model's answer. If it did not keep to JSON, lines written as "이름: 대사" are still understood.
    public static PairScene Parse(string reply, string firstName, string secondName)
    {
        string text = reply.Trim(); if (text.StartsWith("```")) { int line = text.IndexOf('\n'); if (line >= 0) text = text[(line + 1)..]; if (text.TrimEnd().EndsWith("```")) text = text.TrimEnd()[..^3]; }
        int open = text.IndexOf('{'), close = text.LastIndexOf('}'); var lines = new List<PairLine>(); string backdrop = "";
        if (open >= 0 && close > open)
        {
            try
            {
                using var json = JsonDocument.Parse(text[open..(close + 1)]); var root = json.RootElement;
                if (root.TryGetProperty("lines", out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var item in list.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object || lines.Count >= 16) continue; string said = Text(item, "text").Trim(); if (said.Length == 0) continue; string who = Text(item, "who").Trim().ToLowerInvariant();
                        bool second = who == "second" || who == secondName.ToLowerInvariant();
                        lines.Add(new(second, Clean(said, second ? secondName : firstName, second ? firstName : secondName), Text(item, "sprite").Trim(), Text(item, "view").Trim().ToLowerInvariant() != "solo"));
                    }
                backdrop = Text(root, "bg").Trim();
            }
            catch (JsonException) { lines.Clear(); }
        }
        if (lines.Count == 0) lines = FromScript(text, firstName, secondName);
        if (lines.Count == 0) throw new InvalidOperationException("두 사람의 대사를 읽지 못했습니다. 다시 시도해 주세요.");
        return new(lines, backdrop);
    }
    static string Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    static string Clean(string text, string name, string other) { text = StageTalk.OwnLine(text, name, new[] { other }); return text.Length > 1500 ? text[..1500] : text; }
    // A scene as plain text, one line per speech ("이름: 대사"): how it is kept in the history and shown to the model next time.
    public static string Script(IEnumerable<PairLine> lines, string firstName, string secondName) => string.Join("\n", lines.Select(l => (l.Second ? secondName : firstName) + ": " + l.Text.Replace("\n", " ")));
    public static List<PairLine> FromScript(string script, string firstName, string secondName)
    {
        var lines = new List<PairLine>(); var start = new Regex(@"^\s*(" + Regex.Escape(firstName) + "|" + Regex.Escape(secondName) + @")\s*[:：]\s*(.*)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        foreach (string raw in script.Replace("\r\n", "\n").Split('\n'))
        {
            var match = start.Match(raw);
            if (match.Success) { if (match.Groups[2].Value.Trim().Length > 0) lines.Add(new(match.Groups[1].Value == secondName && firstName != secondName, match.Groups[2].Value.Trim())); }
            else if (lines.Count > 0 && raw.Trim().Length > 0) lines[^1] = lines[^1] with { Text = lines[^1].Text + " " + raw.Trim() };   // a line that ran on
        }
        return lines;
    }
    // directive: what Direction made. authored: the line the user wrote for one of the two (it opens the scene as shown and stored).
    public static async Task<(ChatTurn Turn, PairScene Scene)> Send(StageLog log, ChatPreferences preferences, PairCast cast, string thread, string directive, PairLine? authored, string state, StageTalkOptions options, CancellationToken cancellation, AiClient? client = null)
    {
        var settings = ChatRepository.Clone(preferences); settings.UsageKind = "ChatStagePair"; settings.Streaming = false; settings.MaxOutputTokens = Math.Max(options.MaxTokens, 2048);
        string first = cast.First.Name, second = cast.Second.Name;
        // Both profiles in one prompt, the own-voice rule once (see DuoInteraction).
        settings.Profile = cast.First.Snapshot(); settings.Profile.Prompts = new PromptTemplates { Profile = "{extra}" };
        settings.Profile.Extra = "두 캐릭터가 나누는 장면을 한 번에 씁니다. 아래 두 프로필은 역할별 자료입니다.\n[" + first + "의 프로필 (first)]\n" + CharacterPrompt.Build(cast.First, false) + "\n[" + second + "의 프로필 (second)]\n" + CharacterPrompt.Build(cast.Second, false) + CharacterPrompt.OwnVoice + Instruction(options, cast) + state;
        string answer = await (client ?? new AiClient()).Send(settings, log.Turns(thread).TakeLast(Math.Max(0, options.ContextTurns)).ToArray(), directive, cancellation);
        cancellation.ThrowIfCancellationRequested(); var scene = Parse(answer, first, second);
        if (authored != null) scene = scene with { Lines = new[] { authored }.Concat(scene.Lines).ToArray() };
        var turn = new ChatTurn(directive, Script(scene.Lines, first, second), DateTimeOffset.Now, first + " · " + second, thread, "둘만의 장면"); log.Add(thread, turn);
        return (turn, scene);
    }
}
