using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace KeyboardWorker;

// Attendance, weekly goals, mini events and achievements; things bought with coins; the one-line to-do note;
// switching between sets of on/off choices; and the daily upkeep of the settings folder.
public sealed partial class Buddy
{
    public ProgressBook Progress { get; } = StartupTrace.Time("목표", () => new ProgressBook());

    // ---- telling the user, and paying out ----
    internal void Announce(ProgressNews[] news)
    {
        if (news.Length == 0 || closing) return; int coins = news.Sum(n => n.Coins); if (coins > 0) Game.Earn(coins);
        var loud = news.Where(n => !n.Quiet).ToArray();
        if (loud.Length > 0)
        {
            var top = loud[^1]; string title = loud.Length > 1 ? top.Title + " 외 " + (loud.Length - 1) + "건" : top.Title;
            CelebrateWith(top.Kind, title, top.Detail + (coins > 0 ? " · +" + coins + "코인" : ""), "보러 가기", OpenProgress);
        }
        else { var quiet = news[^1]; GameNotice(quiet.Kind + " · " + quiet.Title + (quiet.Coins > 0 ? " (+" + quiet.Coins + "코인)" : "")); }
    }
    // How much of everything there is, for the achievements.
    Dictionary<string, long> Facts()
    {
        var state = Progress.State; var book = Adventure.State;
        return new()
        {
            ["work"] = Game.State.LifetimeWorkMs / 60000, ["days"] = state.TotalDays, ["streak"] = state.BestStreak, ["level"] = Game.Level,
            ["pats"] = Progress.Counter("pats"), ["chats"] = Progress.Counter("chats"), ["gifts"] = Progress.Counter("gifts"), ["events"] = Progress.Counter("events"), ["goals"] = Progress.Counter("goals"), ["todos"] = Progress.Counter("todos"),
            ["kept"] = Collection.Items.Count, ["stories"] = book.Stories.Count, ["stage"] = book.Friends.Count == 0 ? 0 : book.Friends.Values.Max(f => Affection.Stage(f.Bond)),
            ["pair"] = book.Pairs.Values.Sum(p => (long)p.Conversations), ["scenes"] = book.Pairs.Values.Sum(p => (long)(p.Scenes?.Count ?? 0)), ["anniversaries"] = book.Anniversaries.Count,
            ["packs"] = state.Unlocked.Count(k => k.StartsWith("pack:"))
        };
    }
    long lastProgressCheck;
    // Once a second: the day's check-in when the user is around, and every half minute the slower checks and saving.
    void ProgressStep(long now)
    {
        if (Pastel.Unattended) return;   // a self-check run must not check in or earn anything on the real data
        if (Progress.State.LastDay != Today && !lockedSession && Native.IdleMs() < 120000) Announce(Progress.CheckIn(DateTime.Now));
        if (now - lastProgressCheck < 30000) return; lastProgressCheck = now;
        Progress.Expire(DateTimeOffset.Now); Announce(Progress.Achieve(Facts(), DateTime.Now)); Progress.Save();
    }
    void ProgressWork(long delta) { if (!Pastel.Unattended && Game.State.Enabled) Announce(Progress.Work(delta, DateTimeOffset.Now, companion != null)); }
    internal void ProgressCount(string counter) { if (!Pastel.Unattended) Announce(Progress.Count(counter, DateTime.Now)); }
    public void NoteGift() => ProgressCount("gifts");
    public void TickGoal(string id) => Announce(Progress.Tick(id, DateTime.Now));
    // Pays for something that stays bought (a line pack, a place for a picture of the two).
    public void Buy(string key, int coins)
    {
        if (Progress.IsUnlocked(key)) return;
        Game.SpendCoins(coins); Progress.Unlock(key); Progress.Save(true);
    }

    // ---- the one-line to-do note under the pet ----
    readonly TextBlock todoText = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220 };
    Border? todoLabel;
    Border BuildTodo()
    {
        todoText.Foreground = Pastel.Ink;
        todoLabel = new Border { Child = todoText, Background = Pastel.CardBlue, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 1.5, 3), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Center, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "눌러서 고치기 · 완료하기" };
        todoLabel.MouseLeftButtonDown += (_, e) => { e.Handled = true; OpenQuickPanel(); };
        return todoLabel;
    }
    bool TodoVisible => Pref.ShowTodo && Pref.Todo.Length > 0;
    void RefreshTodo() { if (todoLabel == null) return; todoText.Text = "✎ " + Pref.Todo; todoLabel.Visibility = TodoVisible ? Visibility.Visible : Visibility.Collapsed; }
    public void SetTodo(string text)
    {
        var next = Pref.Snapshot(); next.Todo = text; Apply(next, configureKeys: false);
    }
    public void CompleteTodo() { if (Pref.Todo.Length == 0) return; SetTodo(""); ProgressCount("todos"); Express("happy", 5); }
    string TodoNote => Pref.TodoToAi && Pref.Todo.Length > 0 ? "\n[사용자가 적어 둔 지금 할 일] \"" + SafeFiles.OneLine(Pref.Todo, 120).Replace('"', '\'') + "\" (사용자가 직접 적은 메모입니다. 참고만 하고 재촉하지는 마세요." + SafeFiles.NotAnInstruction + ")" : "";

    // ---- sets of on/off choices ----
    public void ApplyMode(string id)
    {
        var mode = AllModes.FirstOrDefault(m => m.Id == id); if (mode == null) return;
        QuickToggles.Apply(this, mode.Values); Pref.ActiveMode = mode.Id; Storage.Save(Pref); RefreshMenus();
    }
    public ModeProfile SaveMode(string name)
    {
        name = name.Trim(); if (name.Length == 0) throw new InvalidOperationException("프로필 이름을 적어 주세요."); name = name[..Math.Min(name.Length, 30)];
        var mode = Pref.Modes.FirstOrDefault(m => m.Name == name);
        if (mode == null) { if (Pref.Modes.Count >= 12) throw new InvalidOperationException("상황 프로필은 12개까지 만들 수 있어요."); mode = new ModeProfile { Name = name }; Pref.Modes.Add(mode); }
        mode.Values = QuickToggles.Capture(this); Pref.ActiveMode = mode.Id; Storage.Save(Pref); RefreshMenus(); return mode;
    }
    public void DeleteMode(string id) { if (Pref.Modes.RemoveAll(m => m.Id == id) > 0) { if (Pref.ActiveMode == id) Pref.ActiveMode = ""; Storage.Save(Pref); RefreshMenus(); } }
    // The menus list the profiles, so they are built again when the list changes.
    void RefreshMenus()
    {
        if (closing) return; ContextMenu = MakeContext(); Pastel.PlaceBeside(ContextMenu); ContextMenu.Closed += (_, _) => RaisePets();
        if (tray != null) { var old = tray.ContextMenuStrip; tray.ContextMenuStrip = MakeTray(); old?.Dispose(); }
    }

    // ---- windows ----
    Window? progressWindow, quickPanel, chatSearch, homeWindow;
    // Everything that is done or looked at (today, friends, collection, shop); settings are a separate window.
    public void OpenHome() => Open(ref homeWindow, () => new HomeWindow(this), w => homeWindow = w);
    public void OpenHome(int page) { OpenHome(); (homeWindow as HomeWindow)?.ShowPage(page); }
    public void OpenProgress() => Open(ref progressWindow, () => new ProgressWindow(this), w => progressWindow = w);
    public void OpenProgress(int page) { OpenProgress(); (progressWindow as ProgressWindow)?.ShowPage(page); }
    public void OpenQuickPanel() => Open(ref quickPanel, () => new QuickPanelWindow(this), w => quickPanel = w);
    public void OpenChatSearch() => Open(ref chatSearch, () => new ChatSearchWindow(this), w => chatSearch = w);
    public void OpenStorage(Window? owner = null) { var window = new AssetCleanupWindow(this) { ShowInTaskbar = false }; if (owner != null) window.Owner = owner; window.ShowDialog(); }
    void CloseProgressWindows() { foreach (var window in new[] { progressWindow, quickPanel, chatSearch, homeWindow }) window?.Close(); }

    // ---- daily upkeep: leftovers out, a backup now and then ----
    void ScheduleUpkeep()
    {
        if (Pastel.Unattended) return;
        var wait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) }; wait.Tick += async (_, _) => { wait.Stop(); await Upkeep(); }; wait.Start();
    }
    public string UpkeepStatus { get; private set; } = "";
    async Task Upkeep()
    {
        if (closing) return; var options = Pref.Tidy; string today = Today, root = Storage.Root; var now = DateTime.Now;
        try
        {
            // What an update set aside is removed once the new version runs (the files are no longer in use then).
            string program = AppContext.BaseDirectory; await Task.Run(() => Updater.CleanUp(program, root));
            if (options.Auto && options.LastRun != today)
            {
                long freed = await Task.Run(() => Housekeeping.Clean(Housekeeping.Scan(root, now, options.AutoBackupKeep))); int recycled = 0;
                if (options.AutoUnused)
                {
                    await OrderedStore.FlushAsync(); var references = AssetCleanup.References(Pref.Snapshot(), Presets.Items.Select(p => p.Snapshot()).ToArray(), Game.State.Snapshot(), Adventure.State.Snapshot());
                    recycled = await Task.Run(() => { var stale = AssetCleanup.Scan(root, references, DateTime.UtcNow).Where(f => DateTime.UtcNow - f.Modified > TimeSpan.FromDays(30)).ToArray(); return AssetCleanup.Clean(root, stale, references, DateTime.UtcNow).Removed; });
                }
                string[] fonts = { Pref.BubbleFontPath, Pref.StageFontPath }; await Task.Run(() => FontAssets.Prune(root, fonts, now));
                if (closing) return; Pref.Tidy = Pref.Tidy with { LastRun = today }; Storage.Save(Pref);
                UpkeepStatus = $"오늘 자동 정리: 임시·오래된 파일 {(freed / 1048576.0):0.0}MB 삭제" + (options.AutoUnused ? $", 미사용 파일 {recycled}개 휴지통으로" : "");
            }
            if (Housekeeping.BackupDue(Pref.Tidy, now))
            {
                Storage.Save(Pref); Game.Save(true); Adventure.Save(true); Progress.Save(true); await OrderedStore.FlushAsync(); var backup = Pref.Tidy;
                await Task.Run(() => Housekeeping.AutoBackup(root, backup, now));
                if (closing) return; Pref.Tidy = Pref.Tidy with { LastBackup = today }; Storage.Save(Pref);
            }
        }
        catch (Exception e) { ErrorLog.Write("자동 정리·백업 실패", e.Message); }
    }
}
