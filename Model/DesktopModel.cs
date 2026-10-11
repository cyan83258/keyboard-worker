using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;

namespace KeyboardWorker;

public sealed class AppRule
{
    public string Process { get; set; } = "";
    public bool Hide { get; set; }
    public bool Mute { get; set; }
    public bool NoReminder { get; set; }
    public bool ExcludeWork { get; set; }
}
public sealed class DesktopOptions
{
    public DesktopOptions Snapshot()
    {
        var copy = (DesktopOptions)MemberwiseClone(); copy.Shortcuts = new(Shortcuts);
        copy.AppRules = AppRules.Select(r => new AppRule { Process = r.Process, Hide = r.Hide, Mute = r.Mute, NoReminder = r.NoReminder, ExcludeWork = r.ExcludeWork }).ToList(); return copy;
    }
    public bool QuickButton { get; set; } = true;
    public Dictionary<string, string> Shortcuts { get; set; } = new() { ["quick"] = "Ctrl+Alt+Q", ["visibility"] = "Ctrl+Alt+H", ["rest"] = "Ctrl+Alt+R", ["settings"] = "Ctrl+Alt+S", ["quiet"] = "Ctrl+Alt+M" };
    public int ExpressionSeconds { get; set; } = 8;
    public int PatSeconds { get; set; } = 4;
    public int QuickRestMinutes { get; set; } = 15;
    public bool TypingPriority { get; set; } = true;
    public bool TypingBounce { get; set; } = true;
    public int BounceStrength { get; set; } = 5;
    public int TypingReactionMs { get; set; } = 200;
    public int? BounceReactionMs { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public int BodyReactionMs => BounceReactionMs ?? TypingReactionMs;
    public int BubbleFont { get; set; } = 14;
    // Standard bubble colours: soft pink for the main pet, soft sky blue for the second. While a setting still has its
    // standard value it follows the theme (yellow / yellow-green in 사과팝, and so on).
    public const string StandardBubbleText = "#875477", StandardBubbleBackground = "#FFDFEE", StandardBubbleBackground2 = "#D8F2F8";
    public string BubbleText { get; set; } = StandardBubbleText;
    public string BubbleBackground { get; set; } = StandardBubbleBackground;
    public string BubbleText2 { get; set; } = StandardBubbleText;
    public string BubbleBackground2 { get; set; } = StandardBubbleBackground2;
    public int BubbleOpacity { get; set; } = 95;
    public int BubbleWidth { get; set; } = 320;
    public int BubbleHeight { get; set; } = 220;
    public string BubblePosition { get; set; } = "위";
    public int BubbleGap { get; set; } = 4;
    public int BubbleOffsetX { get; set; }
    public int BubbleOffsetY { get; set; }
    public int ReplySeconds { get; set; } = 15;
    public bool BubbleAutoHide { get; set; } = true;
    public int? BubbleHideSeconds { get; set; }
    public List<AppRule> AppRules { get; set; } = new();
    public bool WorkEnabled { get; set; } = true;
    public int WorkGapMs { get; set; } = 2000;
    public bool ShowWorkOnPet { get; set; } = true;
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public bool FocusRepeat { get; set; }
    public void Normalize()
    {
        Shortcuts ??= new(); AppRules ??= new();
        Shortcuts.TryAdd("quiet", "Ctrl+Alt+M");
        ExpressionSeconds = Math.Clamp(ExpressionSeconds, 1, 120); PatSeconds = Math.Clamp(PatSeconds, 1, 120);
        QuickRestMinutes = Math.Clamp(QuickRestMinutes, 1, 1440);
        BounceStrength = Math.Clamp(BounceStrength, 1, 10);
        TypingReactionMs = Math.Clamp(TypingReactionMs, 0, 2000);
        if (BounceReactionMs.HasValue) BounceReactionMs = Math.Clamp(BounceReactionMs.Value, 0, 2000);
        BubbleFont = Math.Clamp(BubbleFont, FontAssets.MinSize, FontAssets.MaxSize); BubbleWidth = Math.Clamp(BubbleWidth, 200, 600); BubbleHeight = Math.Clamp(BubbleHeight, 80, 400);
        BubbleOpacity = Math.Clamp(BubbleOpacity, 30, 100); ReplySeconds = Math.Clamp(ReplySeconds, 3, 120);
        if (BubbleHideSeconds.HasValue) BubbleHideSeconds = Math.Clamp(BubbleHideSeconds.Value, 1, 3600);
        BubblePosition = new[] { "위", "왼쪽", "오른쪽" }.Contains(BubblePosition) ? BubblePosition : "위";
        BubbleGap = Math.Clamp(BubbleGap, 0, 100); BubbleOffsetX = Math.Clamp(BubbleOffsetX, -600, 600); BubbleOffsetY = Math.Clamp(BubbleOffsetY, -600, 600);
        // "#FFF8FC" was the standard background before each pet had its own bubble colour.
        if (!ValidColor(BubbleText)) BubbleText = StandardBubbleText;
        if (!ValidColor(BubbleBackground) || BubbleBackground.Equals("#FFF8FC", StringComparison.OrdinalIgnoreCase)) BubbleBackground = StandardBubbleBackground;
        if (!ValidColor(BubbleText2)) BubbleText2 = StandardBubbleText; if (!ValidColor(BubbleBackground2)) BubbleBackground2 = StandardBubbleBackground2;
        WorkGapMs = Math.Clamp(WorkGapMs, 200, 10000); FocusMinutes = Math.Clamp(FocusMinutes, 1, 240); BreakMinutes = Math.Clamp(BreakMinutes, 1, 120);
        AppRules = AppRules.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Process)).Take(50).ToList();
    }
    public static bool ValidColor(string? text) { try { return text != null && text.Length == 7 && text[0] == '#' && ColorConverter.ConvertFromString(text) is Color; } catch { return false; } }
    public AppRule Policy(string name)
    {
        var matches = AppRules.Where(r => string.Equals(Path.GetFileNameWithoutExtension(r.Process.Trim()), Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase)).ToArray();
        return new() { Hide = matches.Any(r => r.Hide), Mute = matches.Any(r => r.Mute), NoReminder = matches.Any(r => r.NoReminder), ExcludeWork = matches.Any(r => r.ExcludeWork) };
    }
}

public sealed class TypingWorkClock
{
    long last = -1;
    public long SessionMs { get; private set; }
    public long TodayMs { get; private set; }
    public string Day { get; private set; }
    public bool Paused { get; set; }
    public TypingWorkClock(string day, long today = 0) { Day = day; TodayMs = Math.Max(0, today); }
    public void Key(long now, string day, int gap)
    {
        RollDay(day);
        if (Paused) { Suspend(); return; }
        if (last >= 0 && now >= last && now - last <= gap) { long delta = now - last; SessionMs += delta; TodayMs += delta; }
        last = now;
    }
    public bool IsActive(long now, int gap) => !Paused && last >= 0 && now - last <= gap;
    public void Suspend() { last = -1; }
    public void RollDay(string day) { if (Day != day) { Day = day; TodayMs = 0; Suspend(); } }
    public void ResetSession() { SessionMs = 0; Suspend(); }
    public void ResetToday() { TodayMs = 0; Suspend(); }
    public static string Format(long ms) { var t = TimeSpan.FromMilliseconds(Math.Max(0, ms)); return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"; }
}
public static class WorkTimeStore
{
    sealed record Data(string Day, long Milliseconds);
    public static string Today => DateTime.Now.ToString("yyyy-MM-dd");
    public static TypingWorkClock Load()
    {
        var d = Storage.ReadJson<Data>(Path.Combine(Storage.Root, "work-time.json")); return new(Today, d?.Day == Today ? d.Milliseconds : 0);
    }
    public static void Save(TypingWorkClock clock)
    {
        OrderedStore.SaveJson(Path.Combine(Storage.Root, "work-time.json"), new Data(clock.Day, clock.TodayMs));
    }
}

public sealed class FocusClock
{
    public string Phase { get; private set; } = "대기";
    public bool Paused { get; private set; }
    public bool Running => Phase is "집중" or "휴식";
    long remaining, last;
    int focusMinutes, breakMinutes;
    bool repeat;
    public long RemainingMs => Math.Max(0, remaining);
    public void Start(long now, int focus, int rest, bool cycle) { focusMinutes = focus; breakMinutes = rest; repeat = cycle; Phase = "집중"; remaining = focus * 60000L; last = now; Paused = false; }
    public string? Tick(long now)
    {
        if (!Running || Paused) { last = now; return null; }
        remaining -= Math.Max(0, now - last); last = now;
        if (remaining > 0) return null;
        if (Phase == "집중") { Phase = "휴식"; remaining = breakMinutes * 60000L; return $"집중 시간 끝! {breakMinutes}분 동안 잠깐 쉬자 ♡"; }
        if (repeat) { Phase = "집중"; remaining = focusMinutes * 60000L; return "휴식 끝! 다시 함께 작업해 볼까? ☆"; }
        Stop(); return "휴식 끝! 다음 집중 시간은 원할 때 시작해 줘 ♡";
    }
    public void TogglePause(long now) { if (!Running) return; if (!Paused) Tick(now); Paused = !Paused; last = now; }
    public void Suspend(long now) { if (Running && !Paused) { Tick(now); Paused = true; } last = now; }
    public void SkipBreak(long now)
    {
        if (Phase != "휴식") return;
        if (repeat) { Phase = "집중"; remaining = focusMinutes * 60000L; last = now; Paused = false; } else Stop();
    }
    public void Stop() { Phase = "대기"; remaining = 0; Paused = false; }
}
