using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KeyboardWorker;

// Attendance, weekly goals, mini events, achievements, things bought with coins, upkeep of the settings folder, quick switches.
public static class ProgressTests
{
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        bool Throws(Action action) { try { action(); return false; } catch (Exception) { return true; } }
        string home = Path.Combine(root, "progress"); Directory.CreateDirectory(home);
        ProgressBook New(string name) { string folder = Path.Combine(home, name); Directory.CreateDirectory(folder); return new ProgressBook(folder); }
        // Types for some minutes in ten-second stretches, the way real input arrives.
        List<ProgressNews> Type(ProgressBook book, int minutes, ref DateTimeOffset now, bool duo = false, Func<int, int>? roll = null)
        {
            var news = new List<ProgressNews>(); for (int i = 0; i < minutes * 6; i++) { now = now.AddSeconds(10); news.AddRange(book.Work(10000, now, duo, roll)); }
            return news;
        }

        // Attendance
        Check(ProgressBook.WeekOf(new DateTime(2026, 10, 11)) == "2026-10-05" && ProgressBook.WeekOf(new DateTime(2026, 10, 12)) == "2026-10-12" && ProgressBook.WeekOf(new DateTime(2026, 10, 14, 23, 0, 0)) == "2026-10-12", "A week runs from Monday to Sunday");
        Check(ProgressBook.CheckInCoins(1) == 5 && ProgressBook.CheckInCoins(2) == 7 && ProgressBook.CheckInCoins(11) == 25 && ProgressBook.CheckInCoins(300) == 25 && ProgressBook.CheckInCoins(0) == 5, "The daily reward grows with the streak up to a limit");
        var days = New("days"); var monday = new DateTime(2026, 10, 12, 9, 0, 0);
        var first = days.CheckIn(monday); Check(first.Length == 1 && first[0].Coins == 5 && first[0].Quiet && days.State.Streak == 1 && days.State.TotalDays == 1 && days.State.LastDay == "2026-10-12" && days.State.WeekDays == 1, "The first check-in starts a streak");
        Check(days.CheckIn(monday.AddHours(8)).Length == 0 && days.State.TotalDays == 1, "A day is counted once");
        var second = days.CheckIn(monday.AddDays(1)); Check(second[0].Coins == 7 && days.State.Streak == 2 && second[0].Quiet, "The next day continues the streak");
        var third = days.CheckIn(monday.AddDays(2)); Check(days.State.Streak == 3 && !third[0].Quiet && third[0].Coins == 9 + 3 && third[0].Title.Contains("3일 연속"), "A streak milestone is celebrated with a bonus");
        var later = days.CheckIn(monday.AddDays(5)); Check(days.State.Streak == 1 && days.State.BestStreak == 3 && days.State.TotalDays == 4 && later[0].Coins == 5 && days.State.WeekDays == 4, "A missed day starts the streak again and takes nothing away");
        days.CheckIn(monday.AddDays(7)); Check(days.State.Week == "2026-10-19" && days.State.WeekDays == 1, "A new week counts its own days");
        days.Save(true); OrderedStore.FlushAsync().GetAwaiter().GetResult(); var reopened = new ProgressBook(Path.Combine(home, "days"));
        Check(reopened.State.TotalDays == 5 && reopened.State.BestStreak == 3 && reopened.State.LastDay == "2026-10-19", "Attendance is saved");

        // Weekly goals
        var goals = New("goals"); goals.SetEvents(false); var now = new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(9)); var today = now.LocalDateTime;
        var work = goals.AddGoal("  글 3분 쓰기  ", "work", 3, today); Check(work.Title == "글 3분 쓰기" && work.Target == 10 && work.Kind == "work", "A goal is trimmed and its target kept within the kind's range");
        goals.EditGoal(work.Id, "글 12분 쓰기", "work", 12);
        var early = Type(goals, 11, ref now); Check(early.Count == 0 && work.Value == 11 && !work.Done, "Work is counted towards a goal in minutes");
        var done = Type(goals, 1, ref now); Check(done.Count == 1 && done[0].Kind == "주간 목표 달성" && done[0].Coins == 40 && work.Done && goals.Counter("goals") == 1, "Reaching the target finishes the goal and pays once");
        Check(Type(goals, 5, ref now).Count == 0 && work.Value == 12, "A finished goal is not counted or paid again");
        var chats = goals.AddGoal("수다 2번", "chats", 2, today); Check(goals.Count("chats", today).Length == 0 && goals.Count("chats", today).Single().Coins == 20 && goals.Counter("chats") == 2 && goals.Count("chats", today).Length == 0 && goals.Counter("chats") == 3, "Chats, pats and the like count towards their goals and their lifetime totals");
        var manual = goals.AddGoal("산책", "manual", 1, today); var walks = new[] { goals.AddGoal("물 마시기", "manual", 1, today), goals.AddGoal("스트레칭", "manual", 1, today), goals.AddGoal("정리", "manual", 1, today) };
        Check(goals.Tick(manual.Id, today).Single().Coins == 10 && goals.Tick(manual.Id, today).Length == 0 && goals.Tick(work.Id, today).Length == 0, "A hand-counted goal goes up by one press; other kinds cannot be pressed");
        var paid = walks.Select(w => goals.Tick(w.Id, today).Single().Coins).ToArray(); Check(paid.SequenceEqual(new[] { 10, 10, 0 }), "Hand-counted goals are rewarded only a few times a week");
        goals.EditGoal(chats.Id, "수다 5번", "chats", 5); Check(chats.Amount == 2 && chats.Done && chats.Title == "수다 5번", "Editing keeps what was counted");
        goals.EditGoal(chats.Id, "쓰다듬기", "pats", 3); Check(chats.Amount == 0 && chats.Kind == "pats", "Changing how a goal is counted starts it from zero");
        Check(Throws(() => goals.EditGoal("none", "x", "work", 10)) && Throws(() => goals.AddGoal(" ", "work", 10, today)) && Throws(() => goals.EditGoal(work.Id, "", "work", 10)), "A goal needs a name, and only an existing goal can be edited");
        goals.AddGoal("일곱", "days", 99, today); goals.AddGoal("여덟", "bogus", 5, today);
        Check(goals.State.Goals.Count == ProgressBook.MaxGoals && goals.State.Goals[6].Target == 7 && goals.State.Goals[7].Kind == "work" && Throws(() => goals.AddGoal("아홉", "work", 10, today)), "There is room for eight goals; an unknown kind becomes work time");
        goals.DeleteGoal(manual.Id); goals.DeleteGoal("none"); Check(goals.State.Goals.Count == 7 && goals.State.Goals.All(g => g.Id != manual.Id), "A goal can be deleted");
        var attend = goals.State.Goals.First(g => g.Kind == "days"); goals.CheckIn(today); Check(attend.Amount == 1, "Checking in counts towards an attendance goal");
        goals.CheckIn(today.AddDays(7)); Check(goals.State.Goals.Count == 7 && goals.State.Goals.Where(g => g.Kind != "days").All(g => g.Amount == 0 && !g.Done) && attend.Amount == 1 && goals.State.ManualRewards == 0 && goals.Counter("goals") >= 3, "On Monday every goal starts again; the goals themselves and the lifetime count stay");

        // Mini events
        var events = New("events"); var clock = new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(9)); int pick = 0; int Roll(int n) => n == 41 ? 0 : Math.Min(pick, n - 1);
        Check(Type(events, 44, ref clock, roll: Roll).Count == 0 && events.State.Event == null, "No event opens before enough work has built up");
        var opened = Type(events, 1, ref clock, roll: Roll); Check(opened.Single().Kind == "미니 이벤트 시작" && opened[0].Coins == 0 && events.State.Event!.Kind == "sprint" && events.State.EventsToday == 1 && events.State.NextEventMs == 40 * 60000 && opened[0].Detail.Contains("10분"), "After about 45 minutes of work an event opens");
        var won = Type(events, 10, ref clock, roll: Roll); Check(won.Single().Kind == "미니 이벤트 성공" && won[0].Coins == 15 && events.State.Event == null && events.Counter("events") == 1, "Typing enough before the time is up wins the event");
        pick = 1; Type(events, 40, ref clock, roll: Roll); Check(events.State.Event!.Kind == "steady" && events.State.Event.Coins == 30, "Another event comes after more work");
        clock = clock.AddMinutes(50); var missed = events.Work(10000, clock, false, Roll); Check(missed.Single().Quiet && missed[0].Coins == 0 && events.State.Event == null && events.Counter("events") == 1, "An event that runs out of time ends quietly and costs nothing");
        pick = 2; Type(events, 40, ref clock, duo: true, roll: Roll); Check(events.State.Event!.Kind == "duo" && events.State.EventsToday == 3, "With two pets out there is an event for the pair");
        clock = clock.AddHours(1); events.Expire(clock); Check(events.State.Event == null, "An event left behind is cleared");
        Check(Type(events, 90, ref clock, roll: Roll).Count == 0 && events.State.Event == null, "At most three events open in a day");
        clock = clock.AddDays(1); Check(Type(events, 1, ref clock, roll: Roll).Count == 1 && events.State.EventsToday == 1, "The next day brings events again");
        events.SetEvents(false); Check(events.State.Event == null && Type(events, 120, ref clock, roll: Roll).Count == 0, "Events can be turned off");
        var solo = New("solo"); pick = 5; Type(solo, 45, ref clock, roll: Roll); Check(solo.State.Event!.Kind == "steady", "Alone, the event for two is never picked");
        Check(ProgressBook.Describe(new MiniEvent { TargetMs = 600000, ProgressMs = 185000, Ends = clock.AddMinutes(7.2), Coins = 15 }, clock).StartsWith("8분 안에 키보드 작업 10분 채우기 · 지금 3분"), "An event says how much is left");
        Check(solo.Work(0, clock, false).Length == 0 && solo.Work(-5, clock, false).Length == 0 && ReferenceEquals(solo.Work(1000, clock, false), solo.Work(1000, clock, false)), "Typing that changes nothing allocates no news");

        // Achievements
        var medals = New("medals"); var facts = new Dictionary<string, long> { ["work"] = 700, ["days"] = 8, ["level"] = 5 };
        var seeded = medals.Achieve(facts, new DateTime(2026, 10, 12));
        Check(seeded.Single().Title.Contains("4개") && seeded[0].Coins == 10 + 30 + 15 + 20 && medals.State.Seeded && medals.State.Achievements.Keys.OrderBy(k => k).SequenceEqual(new[] { "days-7", "level-5", "work-1", "work-10" }) && medals.State.Achievements["work-10"] == "2026-10-12", "What was earned before achievements existed is counted once, as one piece of news");
        Check(medals.Achieve(facts, DateTime.Now).Length == 0, "An achievement is reached once");
        facts["chats"] = 10; facts["streak"] = 3; var two = medals.Achieve(facts, DateTime.Now); Check(two.Length == 2 && two.All(n => n.Kind == "업적 달성" && !n.Quiet) && two.Sum(n => n.Coins) == 20, "Later achievements are announced one by one");
        var fresh = New("fresh"); Check(fresh.Achieve(new Dictionary<string, long>(), DateTime.Now).Length == 0 && fresh.State.Seeded && fresh.Achieve(new Dictionary<string, long> { ["pats"] = 50 }, DateTime.Now).Single().Title == "다정한 손길", "A new user starts with nothing and earns them one at a time");
        Check(ProgressBook.All.Select(a => a.Id).Distinct().Count() == ProgressBook.All.Length && ProgressBook.All.All(a => a.Target > 0 && a.Coins > 0 && a.Title.Length > 0 && a.Description.Length > 0), "Every achievement has its own id, a target and a reward");

        // Bought with coins
        var shop = New("shop"); Check(!shop.IsUnlocked("pack:warm"), "Nothing is bought at first"); shop.Unlock("pack:warm"); shop.Unlock("pack:warm"); shop.Unlock(PairScenes.Unlock("night")); shop.Save(true); OrderedStore.FlushAsync().GetAwaiter().GetResult();
        var bought = new ProgressBook(Path.Combine(home, "shop")); Check(bought.IsUnlocked("pack:warm") && bought.IsUnlocked("scene:night") && bought.State.Unlocked.Count == 2 && !bought.IsUnlocked("scene:work"), "What was bought stays bought");
        string gameHome = Path.Combine(home, "game"); Directory.CreateDirectory(gameHome); var game = new PetGame(gameHome); long before = game.State.Coins; game.Earn(30); game.Earn(0); game.Earn(-5);
        Check(game.State.Coins == before + 30 && Throws(() => game.SpendCoins((int)game.State.Coins + 1)) && game.State.Coins == before + 30, "Coins are earned, and nothing can be bought without enough of them");
        game.SpendCoins(25); Check(game.State.Coins == before + 5, "Spending takes the coins"); Check(PairScenes.Price == 1200 && LinePacks.All.All(p => p.Price >= 900) && PairScenes.Free == "talk" && PairScenes.Known(PairScenes.Free), "Packs and places for a picture of the two take days of work to afford; the first place is free");
        Check(LinePacks.All.Select(p => p.Id).Distinct().Count() == LinePacks.All.Length && LinePacks.All.All(p => p.Price > 0 && p.Lines.Count >= 5 && p.Lines.Keys.All(k => LineBook.Find(k) != null)) && LinePacks.Find("tsun") != null && LinePacks.Find("x") == null && LinePacks.Key("warm") == "pack:warm", "Every line pack has a price and only lines for known situations");
        Check(LinePacks.All.SelectMany(p => p.Lines.Where(l => l.Key != "pair")).All(l => !l.Value.Contains(" / ")) && LinePacks.Find("couple")!.Lines["pair"].Split('\n').All(l => LineBook.Turns(l).Length >= 2), "Exchanges in a pack have both pets speaking");
        var lines = new Dictionary<string, string> { ["nudge"] = "원래 재촉\n천천히 해도 괜찮아. 나 여기 있어.", ["random"] = "" }; var warm = LinePacks.Find("warm")!; int total = warm.Lines.Sum(l => l.Value.Split('\n').Length);
        Check(LinePacks.AddTo(lines, warm) == total - 1 && lines["nudge"].Split('\n').Length == 3 && lines["nudge"].StartsWith("원래 재촉\n") && lines["random"].Split('\n').Length == 3 && lines.ContainsKey("rest-start"), "A pack is added after the lines already there, without repeating one");
        Check(LinePacks.AddTo(lines, warm) == 0, "Adding the same pack again changes nothing");

        // Keeping the settings folder tidy
        string data = Path.Combine(home, "data"), temp = Path.Combine(home, "temp"); foreach (var folder in new[] { data, temp, Path.Combine(data, "images"), Path.Combine(data, "backups", "auto"), Path.Combine(data, "preset-import-old"), Path.Combine(data, "couple-import-new"), Path.Combine(temp, "kw-couple-old"), Path.Combine(temp, "other-program") }) Directory.CreateDirectory(folder);
        var moment = new DateTime(2026, 10, 12, 12, 0, 0);
        string Make(string relative, double daysOld, int bytes = 10, string? baseFolder = null) { string path = Path.Combine(baseFolder ?? data, relative); File.WriteAllBytes(path, new byte[bytes]); File.SetLastWriteTimeUtc(path, moment.ToUniversalTime().AddDays(-daysOld)); return path; }
        string keepSettings = Make("settings.json", 100), keepImage = Make(Path.Combine("images", "a.png"), 400), oldTemp = Make("settings.json.tmp", 1), newTemp = Make("chat-history.json.tmp", 0.01), imageTemp = Make(Path.Combine("images", "b.png.tmp"), 2, 300);
        string oldBroken = Make("settings.json.broken-20260101", 60), newBroken = Make("pet-game.json.broken-20261001", 5), pending = Make("pending-import.zip", 3);
        for (int i = 1; i <= 7; i++) Make(Path.Combine("backups", $"before-import-2026100{i}-120000.zip"), 8 - i, 100);
        string cleanupBackup = Make(Path.Combine("backups", "before-cleanup.zip"), 120), recentOther = Make(Path.Combine("backups", "before-diet.zip"), 10);
        for (int i = 1; i <= 4; i++) Make(Path.Combine("backups", "auto", $"auto-backup-2026100{i}-090000.zip"), 8 - i, 50);
        Make(Path.Combine("preset-import-old", "x.png"), 2, 500); Directory.SetLastWriteTimeUtc(Path.Combine(data, "preset-import-old"), moment.ToUniversalTime().AddDays(-2)); Directory.SetLastWriteTimeUtc(Path.Combine(data, "couple-import-new"), moment.ToUniversalTime());
        Make(Path.Combine("kw-couple-old", "first.kwpreset"), 2, 700, temp); Directory.SetLastWriteTimeUtc(Path.Combine(temp, "kw-couple-old"), moment.ToUniversalTime().AddDays(-2)); string stranger = Make(Path.Combine("other-program", "file.tmp"), 30, 10, temp);
        var found = Housekeeping.Scan(data, moment, 2, temp); var names = found.Select(f => Path.GetFileName(f.Path)).OrderBy(n => n).ToArray();
        Check(names.SequenceEqual(new[] { "auto-backup-20261001-090000.zip", "auto-backup-20261002-090000.zip", "b.png.tmp", "before-cleanup.zip", "before-import-20261001-120000.zip", "before-import-20261002-120000.zip", "kw-couple-old", "pending-import.zip", "preset-import-old", "settings.json.broken-20260101", "settings.json.tmp" }.OrderBy(n => n)), "Leftovers are found: old temporary files, working folders, old broken copies, stale imports and backups beyond the newest few (" + string.Join(", ", names) + ")");
        Check(found.Single(f => f.Path.EndsWith("preset-import-old")).Bytes == 500 && found.Single(f => f.Path.EndsWith("preset-import-old")).Folder && found.All(f => f.Kind.Length > 0), "A working folder is measured with what is inside");
        long freed = Housekeeping.Clean(found);
        Check(freed == found.Sum(f => f.Bytes) && !File.Exists(oldTemp) && !File.Exists(imageTemp) && !File.Exists(oldBroken) && !File.Exists(pending) && !File.Exists(cleanupBackup) && !Directory.Exists(Path.Combine(data, "preset-import-old")) && !Directory.Exists(Path.Combine(temp, "kw-couple-old")), "Cleaning removes exactly what was found");
        Check(File.Exists(keepSettings) && File.Exists(keepImage) && File.Exists(newTemp) && File.Exists(newBroken) && File.Exists(recentOther) && File.Exists(stranger) && Directory.Exists(Path.Combine(data, "couple-import-new")) && Directory.GetFiles(Path.Combine(data, "backups"), "before-import-*").Length == 5 && Directory.GetFiles(Path.Combine(data, "backups", "auto")).Length == 2, "Settings, pictures, anything recent and other programs' files are left alone");
        Check(Housekeeping.Scan(data, moment, 2, temp).Length == 0 && Housekeeping.Scan(Path.Combine(home, "no-such-folder"), moment).Length == 0 && Housekeeping.Clean(found) == 0, "A clean folder has nothing to clean, and cleaning twice is harmless");
        var usage = Housekeeping.Usage(data); Check(usage.Single(u => u.Name == "이미지").Files == 1 && usage.Single(u => u.Name == "백업").Files == 8 && usage.Single(u => u.Name == "설정·기록 파일").Files == 3, "The folder's use is summed up by kind");

        // Automatic backups
        var off = new TidyOptions { AutoBackupDays = 0 }; var weekly = new TidyOptions { AutoBackupDays = 7, LastBackup = "2026-10-06" };
        Check(!Housekeeping.BackupDue(off, moment) && Housekeeping.BackupDue(new TidyOptions(), moment) && !Housekeeping.BackupDue(weekly, moment) && Housekeeping.BackupDue(weekly, moment.AddDays(1)) && Housekeeping.BackupDue(weekly with { LastBackup = "2030-01-01" }, moment) && Housekeeping.BackupDue(weekly with { LastBackup = "언젠가" }, moment), "A backup is due when the interval has passed, never when turned off");
        string made = ""; for (int i = 0; i < 4; i++) made = Housekeeping.AutoBackup(data, new TidyOptions { AutoBackupKeep = 3 }, moment.AddDays(10 + i));
        var autos = Housekeeping.AutoBackups(data); Check(autos.Length == 3 && autos[0].FullName == made && SettingsBackup.Describe(made) is { Media: false, Chat: true }, "Automatic backups hold settings and chat, and only the newest few are kept");
        using (var zip = System.IO.Compression.ZipFile.OpenRead(made)) Check(zip.GetEntry("settings.json") != null && zip.Entries.All(e => !e.FullName.StartsWith("backups") && !e.FullName.StartsWith("images") && !e.FullName.EndsWith(".tmp") && !e.FullName.Contains(".broken-")), "A backup never contains other backups, temporary files or (unless asked) pictures");
        var tidy = new TidyOptions { AutoBackupDays = 500, AutoBackupKeep = 0, LastRun = null!, LastBackup = null! }.Normalize();
        Check(tidy.AutoBackupDays == 90 && tidy.AutoBackupKeep == 1 && tidy.LastRun == "" && new TidyOptions().Auto && !new TidyOptions().AutoUnused && new TidyOptions().AutoBackupDays == 7 && !new TidyOptions().AutoBackupMedia, "Upkeep is on by default; unused pictures are only removed by themselves when asked");

        // The to-do line, profiles and quick switches
        var settings = new Preferences { Todo = "  보고서\n\n초안 쓰기 " + new string('!', 300), Tidy = null!, ActiveMode = null! }; settings.Normalize();
        Check(settings.Todo.StartsWith("보고서 초안 쓰기") && settings.Todo.Length == 120 && !settings.Todo.Contains('\n') && settings.ShowTodo && !settings.TodoToAi && settings.Tidy != null && settings.ActiveMode == "", "The to-do note is one line of limited length, and is not shared with the AI unless asked");
        Check(settings.Modes.Count == 3 && settings.Modes.Select(m => m.Name).SequenceEqual(new[] { "편하게", "업무 집중", "발표·화면 공유" }) && settings.Modes.SelectMany(m => m.Values.Keys).All(k => QuickToggles.Find(k) != null), "New settings start with three profiles that only use known switches");
        var copy = settings.Snapshot(); copy.Modes[0].Values["quick"] = false; copy.Modes.RemoveAt(2); Check(settings.Modes.Count == 3 && settings.Modes[0].Values["quick"], "A copy of the settings has its own profiles");
        settings.SavedModes = new(); settings.Normalize(); Check(settings.Modes.Count == 0, "Deleting every profile does not bring the starters back");
        settings.SavedModes = Enumerable.Range(0, 20).Select(i => new ModeProfile { Name = i == 3 ? " " : "  프로필 " + i + new string('가', 40), Values = null! }).ToList(); settings.Normalize();
        Check(settings.Modes.Count == 12 && settings.Modes.All(m => m.Name.Length <= 30 && m.Name.StartsWith("프로필") && m.Values != null), "Profiles are limited in number and name length");
        var saved = System.Text.Json.JsonSerializer.Deserialize<Preferences>(System.Text.Json.JsonSerializer.Serialize(new Preferences { Todo = "메모", SavedModes = new() { new ModeProfile { Name = "밤", Values = new() { ["typing"] = false } } } }))!; saved.Normalize();
        Check(saved.Todo == "메모" && saved.Modes.Single().Name == "밤" && !saved.Modes[0].Values["typing"], "The note and the profiles are saved with the settings");
        Check(QuickToggles.All.Select(t => t.Key).Distinct().Count() == QuickToggles.All.Length && QuickToggles.All.All(t => (t.Set == null) != (t.Special == null) && t.Title.Length > 0) && new[] { "timer", "badge", "player", "quick", "todo", "companion" }.All(k => QuickToggles.Find(k)?.Group == "화면에 보이는 것"), "Every switch has one way of being set; what shows around the pet is in one group");
        var probe = new Preferences(); QuickToggles.Find("timer")!.Set!(probe, false); QuickToggles.Find("player")!.Set!(probe, false); QuickToggles.Find("voice")!.Set!(probe, false); QuickToggles.Find("todo")!.Set!(probe, false);
        Check(!probe.Desktop.ShowWorkOnPet && !probe.Music.PlayerVisible && !probe.Voice.Enabled && !probe.ShowTodo && probe.Desktop.QuickButton, "A switch changes only its own setting");

        // Looking through the chat
        var turns = new[]
        {
            new ChatTurn("오늘 점심 뭐 먹지", "카레 어때?", new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), "모모"), new ChatTurn("졸려", "잠깐 눈 붙여.\n10분만.", new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero), "루", "secondary", "위로"),
            new ChatTurn("카레 맛있었어", "다행이다!", new DateTimeOffset(2026, 10, 3, 13, 0, 0, TimeSpan.Zero), "모모")
        };
        Check(ChatSearchWindow.Find(turns, "카레").Length == 2 && ChatSearchWindow.Find(turns, "카레 점심").Length == 1 && ChatSearchWindow.Find(turns, "  ").Length == 3 && ChatSearchWindow.Find(turns, "루").Single().User == "졸려" && ChatSearchWindow.Find(turns, "CURRY").Length == 0 && ChatSearchWindow.Find(turns, "눈 붙여").Length == 1, "Chats are found by every word given, in either side's text or the character's name");
        string plain = ChatSearchWindow.Text(turns.Take(2), false, "나"), marked = ChatSearchWindow.Text(turns.Take(2), true, "선배");
        Check(plain.Contains("나: 오늘 점심 뭐 먹지\r\n모모: 카레 어때?") && plain.Contains("루: 잠깐 눈 붙여.\r\n10분만.") && !plain.Contains("#") && marked.StartsWith("# Keyboard Worker 대화 기록") && marked.Contains("**선배**: 졸려") && marked.Contains("· 위로"), "Chats are written out as plain text or Markdown");
        return count;
    }
}
