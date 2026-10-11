using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KeyboardWorker;

public sealed class AppUse { public string Kind { get; set; } = "other"; public long Ms { get; set; } }
public sealed class DailyUse
{
    public string Date { get; set; } = "";
    public long WorkMs { get; set; }                                  // typing work time counted by the work clock
    public Dictionary<string, AppUse> Apps { get; set; } = new();     // time each program was in front, by its display name
    public long KindMs(string kind) => Apps.Values.Where(a => a.Kind == kind).Sum(a => a.Ms);
}

// Daily history for the statistics window and for "you have been at this for two hours" remarks. Stays on this PC; 90 days are kept.
public sealed class UsageLog
{
    readonly string path; bool dirty;
    public List<DailyUse> Days { get; }
    public UsageLog(string? root = null)
    {
        path = Path.Combine(root ?? Storage.Root, "usage-log.json");
        Days = Storage.ReadJson<List<DailyUse>>(path, 8 * 1024 * 1024) ?? new();
        Days = Days.Where(d => d != null && !string.IsNullOrEmpty(d.Date)).ToList();
        foreach (var day in Days) { day.Apps ??= new(); foreach (var key in day.Apps.Where(p => p.Value == null).Select(p => p.Key).ToArray()) day.Apps.Remove(key); }
    }
    public DailyUse Day(string date)
    {
        var found = Days.Count > 0 && Days[^1].Date == date ? Days[^1] : Days.FirstOrDefault(d => d.Date == date);
        if (found == null) { found = new() { Date = date }; Days.Add(found); Days.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date)); while (Days.Count > 90) Days.RemoveAt(0); dirty = true; }
        return found;
    }
    public void AddApp(string date, string title, string kind, long ms)
    {
        if (ms <= 0 || string.IsNullOrWhiteSpace(title)) return; var day = Day(date);
        if (!day.Apps.TryGetValue(title, out var use)) { if (day.Apps.Count >= 150) return; use = new(); day.Apps[title] = use; }
        use.Kind = kind; use.Ms += ms; dirty = true;
    }
    public void SetWork(string date, long ms) { var day = Day(date); if (day.WorkMs != ms) { day.WorkMs = ms; dirty = true; } }
    public long AppMs(string date, string title) => Day(date).Apps.TryGetValue(title, out var use) ? use.Ms : 0;
    public void Save(bool force = false)
    {
        if (!dirty && !force) return; dirty = false;
        OrderedStore.SaveJson(path, Days.Select(d => new DailyUse { Date = d.Date, WorkMs = d.WorkMs, Apps = d.Apps.ToDictionary(p => p.Key, p => new AppUse { Kind = p.Value.Kind, Ms = p.Value.Ms }) }).ToArray());
    }
    // "25분", "2시간", "2시간 10분"
    public static string Span(long ms) { long minutes = Math.Max(0, ms) / 60000; return minutes < 60 ? minutes + "분" : minutes % 60 == 0 ? minutes / 60 + "시간" : $"{minutes / 60}시간 {minutes % 60}분"; }
}
