using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KeyboardWorker;

// The novel's summaries, after the idea of SillyTavern's scenario summarizer: the parts of a story are summarized a few
// at a time into entries ("#1~5"), each written under the headings the user chose (scenario, place, time, relationship...).
// The summarized parts are then left out of what is sent - only the summaries go, within a token budget - so a long
// story stays in reach. The newest parts always go as they are.
public sealed class NovelSummaryItem
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Prompt { get; set; } = "";
    public NovelSummaryItem Snapshot() => (NovelSummaryItem)MemberwiseClone();
}
public sealed class NovelSummaryOptions
{
    public bool Inject { get; set; } = true;          // send the summaries (and leave the summarized parts out)
    public bool Auto { get; set; }                    // summarize by itself once enough parts are waiting
    public int Every { get; set; } = 10;              // parts waiting before an automatic summary
    public int Group { get; set; } = 5;               // parts that go into one summary
    public int KeepRecent { get; set; } = 3;          // the newest parts are always sent as they are
    public int Budget { get; set; } = 20000;          // tokens the summaries may take in a request
    public int Context { get; set; } = 3;             // earlier summaries shown to the summarizer, so it stays consistent
    public string Connection { get; set; } = "";      // the AI connection that summarizes; "" = the novel's own
    public string Guide { get; set; } = NovelSummaries.StandardGuide;
    public List<NovelSummaryItem> Items { get; set; } = NovelSummaries.StandardItems();
    public NovelSummaryOptions Snapshot() { var copy = (NovelSummaryOptions)MemberwiseClone(); copy.Items = Items.Select(i => i.Snapshot()).ToList(); return copy; }
}
// One summary: the parts it covers (by when each was written, so edits and deletions elsewhere do not move it) and the text.
public sealed record NovelSummary(long From, long To, string Text, DateTimeOffset At);

public static class NovelSummaries
{
    public const int MaxText = 8000, MaxEntries = 2000;
    public const string StandardGuide = """
        당신은 긴 연재 소설의 내용을 정리하는 편집자입니다. 주어진 부분들을 하나의 요약으로 정리합니다.
        - 본문에 나온 사실만 씁니다. 짐작하거나 지어내지 않습니다.
        - 사건을 나열하지 말고 원인과 결과가 이어지도록 씁니다. 누가 무엇을 왜 했는지가 드러나야 합니다.
        - 이야기의 흐름에 중요한 행동·사건·대사에 집중하고, 사소한 잡담은 과감히 뺍니다. 관계가 달라지는 계기가 되는 작은 말이나 행동은 놓치지 않습니다.
        - 앞 요약과 달라지지 않은 항목(장소·시간·관계 등)은 앞 요약과 같은 말로 그대로 씁니다. '동일함' 같은 말로 줄이지 않습니다.
        - 문장은 건조하고 분명한 평서문으로 끝냅니다.
        """;
    public static List<NovelSummaryItem> StandardItems() => new()
    {
        new() { Key = "scenario", Name = "시나리오", Prompt = "사건의 흐름을 인과가 드러나게 서술합니다. 중요한 대사는 원문 그대로 큰따옴표로 인용합니다." },
        new() { Key = "location", Name = "장소", Prompt = "인물들이 있는 곳을 짧게 적습니다. 옮겨 갔다면 화살표(→)로 적습니다." },
        new() { Key = "time", Name = "시간", Prompt = "때(새벽, 밤 등)를 적습니다. 바뀌지 않았으면 앞 요약과 같게 적습니다." },
        new() { Key = "relationship", Name = "관계", Prompt = "두 사람의 지금 관계를 가장 잘 나타내는 말로 적습니다. 뚜렷한 변화가 없으면 앞 요약과 같게 적습니다." },
        new() { Key = "emotion", Name = "감정", Enabled = false, Prompt = "'- 인물: 감정 (까닭)' 형식으로 인물마다 한 줄씩 적습니다." },
        new() { Key = "thoughts", Name = "속마음", Enabled = false, Prompt = "본문에 드러난 속마음만 '- 인물: \"속마음\"'으로 적습니다. 짐작하지 않으며, 없으면 '없음'이라고 적습니다." },
        new() { Key = "mood", Name = "분위기", Enabled = false, Prompt = "장면의 긴장감과 분위기를 형용사 몇 개로 적습니다." },
        new() { Key = "date", Name = "날짜", Enabled = false, Prompt = "본문의 단서로 날짜를 짐작해 '연/월/일(요일)'로 적습니다. 바뀌었으면 화살표(→)로 적습니다." },
        new() { Key = "events", Name = "주요 사건", Enabled = false, Prompt = "고백·약속·이별처럼 이야기 전체에 영향을 주는 사건만 적습니다. 없으면 '없음'." },
        new() { Key = "items", Name = "주요 물건", Enabled = false, Prompt = "선물·반지·열쇠처럼 이야기에서 의미 있는 물건과 지금 가진 사람을 적습니다. 없으면 '없음'." },
        new() { Key = "people", Name = "등장인물", Enabled = false, Prompt = "새로 등장했거나 크게 달라진 인물만 '이름 | 역할 | 특징 | 관계'로 적습니다. 없으면 '없음'." }
    };
    public static NovelSummaryOptions Normalize(NovelSummaryOptions? options)
    {
        var o = options ?? new(); o.Every = Math.Clamp(o.Every, 1, 200); o.Group = Math.Clamp(o.Group, 1, 50); o.KeepRecent = Math.Clamp(o.KeepRecent, 0, 50); o.Budget = Math.Clamp(o.Budget, 500, 500000); o.Context = Math.Clamp(o.Context, 0, 20);
        o.Connection = SafeFiles.OneLine(o.Connection, 80); o.Guide = string.IsNullOrWhiteSpace(o.Guide) ? StandardGuide : o.Guide[..Math.Min(o.Guide.Length, 8000)];
        o.Items = (o.Items ?? new()).Where(i => i != null).Take(30).ToList(); if (o.Items.Count == 0) o.Items = StandardItems();
        foreach (var item in o.Items) { item.Key = SafeFiles.OneLine(item.Key, 40) is { Length: > 0 } key ? key : Guid.NewGuid().ToString("N")[..8]; item.Name = SafeFiles.OneLine(item.Name, 30) is { Length: > 0 } name ? name : "항목"; item.Prompt = (item.Prompt ?? "")[..Math.Min((item.Prompt ?? "").Length, 2000)]; }
        return o;
    }
    static bool Covers(NovelSummary summary, ChatTurn turn) { long at = turn.At.UtcTicks; return at >= summary.From && at <= summary.To; }
    // The parts no summary covers yet, as their places in the story (from 0).
    public static int[] Pending(IReadOnlyList<ChatTurn> story, IReadOnlyList<NovelSummary> summaries) => Enumerable.Range(0, story.Count).Where(i => !summaries.Any(s => Covers(s, story[i]))).ToArray();
    // The waiting parts that may be summarized now: all but the newest, which are sent as they are anyway.
    public static int[] Ready(NovelSummaryOptions options, IReadOnlyList<ChatTurn> story, IReadOnlyList<NovelSummary> summaries) => Pending(story, summaries).Where(i => i < story.Count - options.KeepRecent).ToArray();
    public static bool Due(NovelSummaryOptions options, IReadOnlyList<ChatTurn> story, IReadOnlyList<NovelSummary> summaries) => options.Auto && Ready(options, story, summaries).Length >= options.Every;
    // Places of the parts in runs of at most Group, each run made of neighbours (a summary covers parts that follow one another).
    public static List<int[]> Runs(IEnumerable<int> places, int group)
    {
        var runs = new List<int[]>(); var run = new List<int>();
        foreach (int i in places.OrderBy(i => i)) { if (run.Count > 0 && (i != run[^1] + 1 || run.Count >= group)) { runs.Add(run.ToArray()); run.Clear(); } run.Add(i); }
        if (run.Count > 0) runs.Add(run.ToArray()); return runs;
    }
    // "#3" or "#3~7": the numbers of the parts a summary covers, as they stand in the story now.
    public static string Label(NovelSummary summary, IReadOnlyList<ChatTurn> story)
    {
        var places = Enumerable.Range(0, story.Count).Where(i => Covers(summary, story[i])).ToArray();
        return places.Length == 0 ? "(지운 부분)" : places.Length == 1 ? "#" + (places[0] + 1) : $"#{places[0] + 1}~{places[^1] + 1}";
    }
    // The request for one summary: the instruction (with the headings that are on) and the parts with their numbers.
    public static (string System, string Text) Request(NovelSummaryOptions options, IReadOnlyList<ChatTurn> story, int[] run, IReadOnlyList<NovelSummary> earlier, string names)
    {
        var items = options.Items.Where(i => i.Enabled).ToList(); if (items.Count == 0) items = StandardItems().Take(1).ToList();
        var system = new StringBuilder(options.Guide.Trim()).Append("\n\n## 항목\n");
        foreach (var item in items) system.Append("- ").Append(item.Name).Append(": ").Append(item.Prompt.Trim()).Append('\n');
        system.Append("\n## 출력 형식\n머리말이나 다른 말 없이, 항목마다 한 줄로 시작합니다. 항목의 내용이 길면 여러 줄로 이어 써도 됩니다.\n");
        foreach (var item in items) system.Append("* ").Append(item.Name).Append(": …\n");
        system.Append("모든 출력은 한국어로 씁니다.");
        var text = new StringBuilder(); if (names.Length > 0) text.Append("[등장인물] ").Append(names).Append("\n\n");
        var before = earlier.Where(s => s.To < story[run[0]].At.UtcTicks).TakeLast(options.Context).ToArray();
        if (before.Length > 0) { text.Append("[앞 요약 — 이어지도록 참고]\n"); foreach (var s in before) text.Append(Label(s, story)).Append('\n').Append(s.Text.Trim()).Append("\n\n"); }
        text.Append("[요약할 부분]\n"); foreach (int i in run) text.Append('#').Append(i + 1).Append('\n').Append(story[i].Assistant.Trim()).Append("\n\n");
        return (system.ToString(), text.ToString().TrimEnd());
    }
    // What the model wrote, without a fence or a number header it may have added.
    public static string Clean(string answer)
    {
        var lines = answer.Replace("\r\n", "\n").Trim().Trim('`').Trim().Split('\n').ToList();
        while (lines.Count > 0 && (lines[0].Trim().Length == 0 || lines[0].TrimStart().StartsWith('#'))) lines.RemoveAt(0);
        string text = string.Join("\n", lines).Trim(); return text[..Math.Min(text.Length, MaxText)];
    }
    // What goes to the novel's request: the summaries (newest first while the budget lasts, then in order of the story),
    // and the parts that still go as they are - those no summary covers, and always the newest few.
    public static (string Digest, ChatTurn[] Story) Apply(NovelSummaryOptions options, IReadOnlyList<ChatTurn> story, IReadOnlyList<NovelSummary> summaries)
    {
        if (!options.Inject || summaries.Count == 0) return ("", story.ToArray());
        var keep = Enumerable.Range(0, story.Count).Where(i => i >= story.Count - options.KeepRecent || !summaries.Any(s => Covers(s, story[i]))).Select(i => story[i]).ToArray();
        var chosen = new List<NovelSummary>(); int used = 0;
        foreach (var summary in summaries.Where(s => story.Any(t => Covers(s, t))).OrderByDescending(s => s.To))
        {
            int cost = AiRequestOptions.Estimate(summary.Text) + 8; if (used + cost > options.Budget) break; used += cost; chosen.Add(summary);
        }
        if (chosen.Count == 0) return ("", story.ToArray());
        var digest = new StringBuilder("[시나리오 요약]\n");
        foreach (var summary in chosen.OrderBy(s => s.From)) digest.Append("### ").Append(Label(summary, story)).Append('\n').Append(summary.Text.Trim()).Append("\n\n");
        return (digest.ToString().TrimEnd(), keep);
    }
}
