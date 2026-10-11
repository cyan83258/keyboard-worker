using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KeyboardWorker;

public sealed record AiBudget
{
    public bool Enabled { get; init; } = true;
    public int Calls { get; init; } = 100;
    public int AutomaticCalls { get; init; } = 30;
    public long Tokens { get; init; } = 100000;
    // Spread the automatic requests over the day instead of using them up in the first hours.
    public bool Pace { get; init; } = true;
}
public sealed class UsageDay
{
    public string Date { get; set; } = "";
    public int Calls { get; set; }
    public int AutomaticCalls { get; set; }
    public int Failed { get; set; }
    public int Estimated { get; set; }
    public long Tokens { get; set; }
    public long Reserved { get; set; }
    public int Pending { get; set; }
    public Dictionary<string, int> Kinds { get; set; } = new();
    public UsageDay Snapshot() => new() { Date = Date, Calls = Calls, AutomaticCalls = AutomaticCalls, Failed = Failed, Estimated = Estimated, Tokens = Tokens, Reserved = Reserved, Pending = Pending, Kinds = new(Kinds) };
}
public sealed class AiUsage
{
    public static string Label(string kind) => kind switch { "Chat" => "대표 채팅", "ChatSecondary" => "둘째 채팅", "ChatBoth" => "둘 모두", "AutoNudge" => "AI 재촉", "AutoNudgeBatch" => "재촉 묶음", "AutoRandom" => "랜덤 대사", "AutoRandomBatch" => "랜덤 묶음", "AutoDuo" => "상호작용", "AutoMusicDuo" => "음악 대화", "AutoQuest" => "자동 퀘스트", "AutoStory" => "자동 이야기", "Quest" => "퀘스트 생성", "Story" => "이야기 생성", _ => kind };
    // How many automatic requests may have been made by this time of day when pacing is on: an even share of the daily limit plus a small head start.
    public static int Allowance(AiBudget budget, DateTime now) => Math.Min(budget.AutomaticCalls, (int)Math.Ceiling(budget.AutomaticCalls * now.TimeOfDay.TotalHours / 24) + Math.Max(3, budget.AutomaticCalls / 6));
    static AiUsage? instance;
    public static AiUsage Current => instance != null && instance.root == Storage.Root ? instance : instance = new(Storage.Root);
    readonly string root, path;
    readonly object gate = new();
    readonly List<UsageDay> days;
    long reserved;
    readonly Func<string> today;
    public AiUsage(string root, Func<string>? today = null)
    {
        this.root = root; path = Path.Combine(root, "ai-usage.json"); this.today = today ?? (() => DateTime.Now.ToString("yyyy-MM-dd"));
        days = Storage.ReadJson<List<UsageDay>>(path) ?? new();
        foreach (var day in days) { day.Kinds ??= new(); if (day.Reserved > 0) { day.Tokens += day.Reserved; day.Estimated += Math.Max(1, day.Pending); day.Failed += day.Pending; day.Reserved = 0; day.Pending = 0; } }
    }
    UsageDay Day() { string date = today(); var found = days.FirstOrDefault(d => d.Date == date); if (found == null) { found = new() { Date = date }; days.Add(found); } return found; }
    void Save() { while (days.Count > 31) days.RemoveAt(0); OrderedStore.SaveJson(path, days.Select(d => d.Snapshot()).ToArray()); }
    public UsageDay Snapshot() { lock (gate) return Day().Snapshot(); }
    public Ticket Begin(AiBudget budget, string kind, int output)
    {
        lock (gate)
        {
            var day = Day(); bool automatic = kind.StartsWith("Auto", StringComparison.Ordinal);
            if (budget.Enabled && (day.Calls >= budget.Calls || automatic && day.AutomaticCalls >= budget.AutomaticCalls || day.Tokens + reserved + output > budget.Tokens)) throw new InvalidOperationException("오늘 AI 사용 상한에 도달했습니다. AI 사용량·성능 설정에서 확인해 주세요.");
            day.Calls++;
            if (automatic) day.AutomaticCalls++;
            day.Kinds[kind] = day.Kinds.GetValueOrDefault(kind) + 1;
            reserved += output;
            day.Reserved += output;
            day.Pending++;
            Save();
            return new(this, day, output);
        }
    }
    public sealed class Ticket : IDisposable
    {
        readonly AiUsage owner; readonly UsageDay day; readonly int output;
        long actual; bool known, success, disposed;
        internal Ticket(AiUsage owner, UsageDay day, int output) { this.owner = owner; this.day = day; this.output = output; }
        public void Capture(JsonElement root)
        {
            if (root.TryGetProperty("response", out var response)) root = response;
            JsonElement usage;
            if (root.TryGetProperty("usageMetadata", out usage) || root.TryGetProperty("usage", out usage))
                foreach (string key in new[] { "totalTokenCount", "total_tokens" }) if (usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(key, out var count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt64(out long tokens))
                {
                    actual = Math.Max(actual, tokens);
                    known = true;
                }
            if (!known && usage.ValueKind == JsonValueKind.Object)
            {
                long sum = 0; bool found = false;
                foreach (string key in new[] { "input_tokens", "output_tokens", "prompt_tokens", "completion_tokens" }) if (usage.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long n))
                {
                    sum += n;
                    found = true;
                }
                if (found) { actual = Math.Max(actual, sum); known = true; }
            }
        }
        public void Succeeded() => success = true;
        public void Dispose()
        {
            lock (owner.gate)
            {
                if (disposed) return; disposed = true; owner.reserved -= output; day.Reserved = Math.Max(0, day.Reserved - output); day.Pending = Math.Max(0, day.Pending - 1);
                day.Tokens += known ? actual : output; if (!known) day.Estimated++; if (!success) day.Failed++; owner.Save();
            }
        }
    }
}
