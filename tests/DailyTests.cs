using System;
using System.IO;
using System.Linq;

namespace KeyboardWorker;

public static class DailyTests
{
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        // Time of day
        Check(DayClock.Part(3) == "새벽" && DayClock.Part(7) == "아침" && DayClock.Part(10) == "오전" && DayClock.Part(13) == "점심 무렵" && DayClock.Part(16) == "오후" && DayClock.Part(20) == "저녁" && DayClock.Part(23) == "밤", "The day is split into named parts");
        var sunday = new DateTime(2026, 10, 11, 23, 5, 0);
        Check(DayClock.Exact(sunday).Contains("일요일") && DayClock.Exact(sunday).Contains("23시 5분") && DayClock.Exact(sunday).Contains("밤"), "Chat is told the exact time and weekday");
        Check(DayClock.Rough(sunday).Contains("일요일 밤") && !DayClock.Rough(sunday).Contains("23"), "Prepared lines only get the part of the day");
        Check(new AwarenessOptions().Clock && !new AwarenessOptions().CommentsUseAi && !new AwarenessOptions().GameHide, "Time awareness is on and hiding during games is off by default");

        // Time spent in a program
        var game = new AppInFront("eldenring", "", "ELDEN RING", "game"); var editor = new AppInFront("Code", "", "Visual Studio Code", "work");
        Check(UsageLog.Span(25 * 60000) == "25분" && UsageLog.Span(120 * 60000) == "2시간" && UsageLog.Span(130 * 60000) == "2시간 10분" && UsageLog.Span(-5) == "0분", "Durations read naturally");
        Check(!AppKinds.Instruction(game, 10 * 60000).Contains("쓴 시간은") && AppKinds.Instruction(game, 45 * 60000).Contains("45분") && AppKinds.Instruction(game, 130 * 60000).Contains("2시간 10분") && AppKinds.Instruction(game, 130 * 60000).Contains("길다"), "Long use is mentioned to the AI once it is worth mentioning");
        Check(AppKinds.Context(editor, 90 * 60000).Contains("1시간 30분") && !AppKinds.Context(editor).Contains("쓴 시간은"), "Chat knows how long the program has been used today");

        // Daily log
        string home = Path.Combine(root, "usage"); Directory.CreateDirectory(home); var log = new UsageLog(home);
        log.AddApp("2026-10-10", "ELDEN RING", "game", 3600000);
        log.AddApp("2026-10-11", "Visual Studio Code", "work", 60000);
        log.AddApp("2026-10-11", "Visual Studio Code", "work", 30000);
        log.AddApp("2026-10-11", "ELDEN RING", "game", 5000);
        log.AddApp("2026-10-11", "", "other", 9000);
        log.AddApp("2026-10-11", "Chrome", "other", 0);
        log.SetWork("2026-10-11", 1234567);
        Check(log.AppMs("2026-10-11", "Visual Studio Code") == 90000 && log.AppMs("2026-10-11", "ELDEN RING") == 5000 && log.AppMs("2026-10-10", "ELDEN RING") == 3600000 && log.AppMs("2026-10-11", "Chrome") == 0, "Program time adds up per day");
        Check(log.Day("2026-10-11").KindMs("work") == 90000 && log.Day("2026-10-11").KindMs("game") == 5000 && log.Day("2026-10-11").Apps.Count == 2, "Time is grouped by kind; empty entries are ignored");
        log.Save(); OrderedStore.FlushAsync().GetAwaiter().GetResult(); var again = new UsageLog(home);
        Check(again.Days.Count == 2 && again.Day("2026-10-11").WorkMs == 1234567 && again.AppMs("2026-10-10", "ELDEN RING") == 3600000 && again.Days[0].Date == "2026-10-10", "The log survives a restart, oldest day first");
        for (int day = 0; day < 100; day++) again.AddApp(new DateTime(2027, 1, 1).AddDays(day).ToString("yyyy-MM-dd"), "App", "other", 1000);
        Check(again.Days.Count == 90 && again.Days[0].Date != "2026-10-10", "Only the last 90 days are kept");

        // Spreading automatic requests over the day
        var budget = new AiBudget { AutomaticCalls = 30 };
        Check(budget.Pace && AiUsage.Allowance(budget, new DateTime(2026, 1, 1, 0, 0, 0)) == 5 && AiUsage.Allowance(budget, new DateTime(2026, 1, 1, 12, 0, 0)) == 20 && AiUsage.Allowance(budget, new DateTime(2026, 1, 1, 23, 59, 0)) == 30, "The automatic allowance grows through the day up to the daily limit");
        Check(AiUsage.Allowance(new AiBudget { AutomaticCalls = 1 }, new DateTime(2026, 1, 1, 0, 0, 0)) == 1 && AiUsage.Allowance(new AiBudget { AutomaticCalls = 600 }, new DateTime(2026, 1, 1, 6, 0, 0)) == 250, "The head start scales with the limit and never exceeds it");

        // Reading settings files: a broken file must never be lost silently
        string files = Path.Combine(root, "read-json"); Directory.CreateDirectory(files); string before = Storage.Root; Storage.UseTestRoot(files);
        try
        {
            string missing = Path.Combine(files, "missing.json"), good = Path.Combine(files, "good.json"), broken = Path.Combine(files, "broken.json"), huge = Path.Combine(files, "huge.json");
            Check(Storage.ReadJson<Preferences>(missing) == null && !File.Exists(ErrorLog.FilePath), "A missing settings file is normal and is not reported");
            File.WriteAllText(good, "{\"Size\":222}"); Check(Storage.ReadJson<Preferences>(good)!.Size == 222 && !File.Exists(ErrorLog.FilePath), "A good settings file is read");
            File.WriteAllText(broken, "{\"Size\": 2"); Check(Storage.ReadJson<Preferences>(broken) == null, "A broken settings file gives defaults instead of a crash");
            string kept = Directory.GetFiles(files, "broken.json.broken-*").Single();
            Check(File.ReadAllText(kept) == "{\"Size\": 2" && File.ReadAllText(ErrorLog.FilePath).Contains("broken.json"), "The broken file is kept aside and the problem is logged");
            File.WriteAllText(huge, new string(' ', 5000));
            Check(Storage.ReadJson<Preferences>(huge, 1000) == null && File.ReadAllText(ErrorLog.FilePath).Contains("huge.json") && Directory.GetFiles(files, "huge.json.broken-*").Length == 0, "An oversized file is refused and logged without being copied");
            File.WriteAllText(Path.Combine(files, "settings.json"), "not json");
            Check(Storage.Load().Size == new Preferences().Size && new PresetStore(files).Items.Count == 0, "Stores start with defaults when their file cannot be read");
        }
        finally { Storage.UseTestRoot(before); }

        // Own resource use
        var meter = new PerfMeter(); long start = Environment.TickCount64; meter.Sample(start + 1000, false); meter.Sample(start + 2000, true);
        Check(meter.Summary.Contains("CPU") && meter.Summary.Contains("메모리") && meter.Summary.Contains("아직 측정 전"), "Resource use is reported, with averages only after enough samples");
        return count;
    }
}
