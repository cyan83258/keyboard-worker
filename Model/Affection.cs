using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KeyboardWorker;

// The closeness with a pet (0-1000) read as a stage. A higher stage changes how the pet talks and unlocks lines.
public static class Affection
{
    static readonly (int From, string Name, string Tone)[] stages =
    {
        (0, "낯선 사이", "아직 서로를 잘 모릅니다. 낯선 상대에게 이 캐릭터가 두는 거리 그대로 대합니다."),
        (50, "아는 사이", "얼굴을 익혀 말 붙이기가 조금 편해졌습니다."),
        (150, "친구", "편하게 대하는 사이. 이 캐릭터가 친한 상대에게 보이는 태도가 조금씩 나옵니다."),
        (300, "가까운 친구", "사용자를 꽤 신뢰합니다. 속내나 관심을 이 캐릭터다운 방식으로 조금 더 드러냅니다."),
        (500, "단짝", "서로를 잘 알아 스스럼없습니다. 이 캐릭터 나름의 방식으로 아끼는 티가 납니다."),
        (750, "소중한 사람", "깊이 신뢰하는 특별한 상대입니다. 표현 방식은 여전히 성격을 따르지만, 사용자를 특별하게 여기는 것이 말에 묻어납니다.")
    };
    public const int Stages = 6;
    // 1 (strangers) to 6.
    public static int Stage(int bond) { int stage = 1; for (int i = 0; i < stages.Length; i++) if (bond >= stages[i].From) stage = i + 1; return stage; }
    public static string Name(int stage) => stages[Math.Clamp(stage, 1, Stages) - 1].Name;
    public static string Tone(int stage) => stages[Math.Clamp(stage, 1, Stages) - 1].Tone;
    // Points still needed for the next stage; null at the last one.
    public static int? ToNext(int bond) { int stage = Stage(bond); return stage >= Stages ? null : stages[stage].From - bond; }
    public static string Summary(int bond) => $"{Stage(bond)}단계 · {Name(Stage(bond))}" + (ToNext(bond) is int left ? $" (다음 단계까지 {left})" : " (최고 단계)");
    public static string Context(int bond) => $"\n[친밀도 단계] {Stage(bond)}/{Stages} · {Name(Stage(bond))}. {Tone(Stage(bond))} 친밀도가 높아져도 성격은 바뀌지 않습니다. 차갑거나 오만한 캐릭터는 그 성격 그대로, 다만 가까운 상대에게 보이는 방식으로 말합니다. 거리감에 자연스럽게 반영하되 단계나 수치를 직접 말하지는 마세요.";

    static readonly Regex gate = new(@"^\[(\d)\+\]\s*", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    // Lines written as "[3+] 대사" are only said from that stage on; the marker itself is removed.
    public static string[] Available(IEnumerable<string> lines, int stage) =>
        lines.Select(line => line.Trim()).Where(line => line.Length > 0).Select(line => (Line: line, Match: gate.Match(line)))
            .Where(item => !item.Match.Success || stage >= item.Match.Groups[1].Value[0] - '0').Select(item => item.Match.Success ? item.Line[item.Match.Length..].Trim() : item.Line).Where(line => line.Length > 0).ToArray();
}

public sealed class Anniversary
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Date { get; set; } = "";       // yyyy-MM-dd
    public bool Yearly { get; set; }             // comes back every year (a birthday); otherwise days are counted from it
    public Anniversary Snapshot() => (Anniversary)MemberwiseClone();
}

public static class Anniversaries
{
    public static DateTime? Day(Anniversary item) => DateTime.TryParseExact(item.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ? date : null;
    // This year's (or next year's) return of a yearly date; 29 February falls on the 28th in other years.
    public static DateTime Next(DateTime date, DateTime today)
    {
        DateTime On(int year) => new(year, date.Month, Math.Min(date.Day, DateTime.DaysInMonth(year, date.Month)));
        var soon = On(today.Year); return soon < today.Date ? On(today.Year + 1) : soon;
    }
    // "D-12", "D-DAY", or "D+364 · 365일째" for a count-up date in the past.
    public static string DDay(Anniversary item, DateTime today)
    {
        if (Day(item) is not DateTime date) return "날짜 없음"; today = today.Date;
        if (item.Yearly) { int left = (Next(date, today) - today).Days; return left == 0 ? "D-DAY" : "D-" + left; }
        int passed = (today - date).Days; return passed < 0 ? "D" + passed : passed == 0 ? "D-DAY · 1일째" : $"D+{passed} · {passed + 1}일째";
    }
    // What is worth celebrating today: a yearly date, the day itself, every hundredth day and every full year of a count-up date.
    public static string[] Today(IEnumerable<Anniversary> items, DateTime today)
    {
        today = today.Date; var found = new List<string>();
        foreach (var item in items)
        {
            if (Day(item) is not DateTime date || string.IsNullOrWhiteSpace(item.Name)) continue;
            if (item.Yearly) { if (Next(date, today) == today) found.Add(item.Name); continue; }
            int passed = (today - date).Days; if (passed < 0) continue;
            if (passed == 0) found.Add(item.Name);
            else if ((passed + 1) % 100 == 0) found.Add($"{item.Name} {passed + 1}일");
            else if (Next(date, today) == today) found.Add($"{item.Name} {today.Year - date.Year}주년");
        }
        return found.ToArray();
    }
}

public sealed record PairEvent(DateTimeOffset At, string Kind, string Title, string Text);

// Who is feeling left out. Talking to only one pet again and again makes the other sulk; attention makes up for it.
public sealed class PairMoodState
{
    public string Sulking { get; set; } = "";      // id of the pet that feels left out
    public string SulkingDay { get; set; } = "";
    public string MadeUpDay { get; set; } = "";
    public string StreakWith { get; set; } = "";
    public int Streak { get; set; }
    public PairMoodState Snapshot() => (PairMoodState)MemberwiseClone();
}
public static class PairMood
{
    public const int Limit = 5;   // this many chats in a row with one pet alone
    static void Forget(PairMoodState state, string today) { if (state.Sulking.Length > 0 && state.SulkingDay != today) { state.Sulking = ""; state.Streak = 0; state.StreakWith = ""; } }
    // The user chatted with one pet alone. Returns "sulk" when the other one starts sulking, "makeup" when a sulking pet is spoken to.
    public static string Chat(PairMoodState state, string target, string other, string today)
    {
        Forget(state, today);
        if (state.Sulking == target) return MakeUp(state, today);
        if (state.StreakWith != target) { state.StreakWith = target; state.Streak = 0; }
        state.Streak++;
        if (state.Sulking.Length > 0 || state.Streak < Limit || state.MadeUpDay == today) return "";
        state.Sulking = other; state.SulkingDay = today; return "sulk";
    }
    // Talking to both, or to nobody in particular, breaks the streak.
    public static void Together(PairMoodState state, string today) { Forget(state, today); state.Streak = 0; state.StreakWith = ""; }
    // A pat or a gift for the sulking pet.
    public static string Care(PairMoodState state, string id, string today) { Forget(state, today); return state.Sulking == id ? MakeUp(state, today) : ""; }
    static string MakeUp(PairMoodState state, string today) { state.Sulking = ""; state.MadeUpDay = today; state.Streak = 0; state.StreakWith = ""; return "makeup"; }
    // How this pet feels right now, for the AI.
    public static string Context(PairMoodState state, string id, string otherName, string today)
    {
        if (state.Sulking == id && state.SulkingDay == today) return $"\n[지금 기분] 사용자가 {otherName}하고만 계속 이야기해서 살짝 토라져 있습니다. 서운함을 이 캐릭터다운 방식으로 드러내되(삐침·빈정거림·말수가 줄어듦·모르는 척 등) 심각하게 몰아가지는 않고, 사용자가 말을 걸어 주면 금방 풀립니다.";
        if (state.MadeUpDay == today && state.Sulking.Length == 0) return "\n[지금 기분] 조금 전 서운했던 일이 풀려서 기분이 좋습니다.";
        return "";
    }
}

// Lines the user chose to keep, per pet. Unlike the history of this run, these are saved.
public sealed record CollectedLine(string Id, string CharacterId, string Speaker, string Text, DateTimeOffset At);
public sealed class LineCollection
{
    public const int Limit = 2000;
    readonly string path;
    public List<CollectedLine> Items { get; }
    public event Action? Changed;
    public LineCollection(string? root = null)
    {
        path = System.IO.Path.Combine(root ?? Storage.Root, "line-collection.json");
        Items = (Storage.ReadJson<List<CollectedLine>>(path, 8 * 1024 * 1024) ?? new()).Where(l => l != null && !string.IsNullOrWhiteSpace(l.Text)).ToList();
    }
    public bool Has(string speaker, string text) => Items.Any(l => l.Speaker == speaker && l.Text == text);
    // False when the line is already kept or the collection is full.
    public bool Add(string characterId, string speaker, string text)
    {
        text = text.Trim(); if (text.Length == 0 || Has(speaker, text) || Items.Count >= Limit) return false;
        Items.Add(new(Guid.NewGuid().ToString("N"), characterId, speaker, text[..Math.Min(text.Length, 2000)], DateTimeOffset.Now)); Save(); return true;
    }
    public void Remove(string id) { if (Items.RemoveAll(l => l.Id == id) > 0) Save(); }
    void Save() { OrderedStore.SaveJson(path, Items.ToArray()); Changed?.Invoke(); }
}
