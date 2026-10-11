using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace KeyboardWorker;

// The main pet window: state, construction, appearance and placement. Other responsibilities live in the Buddy.*.cs partials.
public sealed partial class Buddy : Window, IPetAnchor
{
    // The theme is chosen here, before any other field builds a themed control.
    public Preferences Pref { get; private set; } = StartupTrace.Time("설정", () => { var saved = Storage.Load(); Pastel.Use(Program.ThemeOverride ?? saved.Theme); Pastel.ShowNotes = saved.ShowNotes; return saved; });
    readonly Activity activity = new(Environment.TickCount64);
    readonly TypingAudio audio = new();
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer animation = new() { Interval = TimeSpan.FromMilliseconds(180) };
    readonly HandReaction hands = new();
    readonly ReactionLimiter reactionLimiter = new();
    readonly ReactionLimiter bodyLimiter = new();
    bool bounceLeft;
    readonly QuietMode quiet = new();
    bool wasQuiet;
    QuietWindow? quietWindow;
    ReactionPreviewWindow? reactionPreview;
    public bool IsQuiet => quiet.Active(Environment.TickCount64);
    public long QuietSeconds => quiet.Seconds(Environment.TickCount64);
    public ChatRepository Chat { get; } = StartupTrace.Time("대화", () => new ChatRepository());
    public PresetStore Presets { get; } = StartupTrace.Time("프리셋", () => new PresetStore());
    readonly Character character = new();
    readonly PetBubbleWindow bubble;
    readonly TextBlock workLabel = Pastel.Text("", 13);
    readonly Border workBackground;
    readonly Button gameBadge;
    readonly AccessoryLayer accessories = new();
    readonly AccessoryLayer backAccessories = new() { BackLayer = true };
    readonly LazyImageCache equipmentImages;
    public PetGame Game { get; } = StartupTrace.Time("성장", () => new PetGame());
    public AdventureBook Adventure { get; } = StartupTrace.Time("이야기책", () => new AdventureBook());
    public string CharacterId => string.IsNullOrWhiteSpace(Pref.ActivePresetId) ? "default-main" : Pref.ActivePresetId;
    public CharacterPreset? SecondaryPreset => companion?.Preset ?? Presets.Items.FirstOrDefault(p => p.Id == Pref.Duo.PresetId);
    public string ChatSpeakerLabel => Chat.Preferences.Target == "secondary" ? SecondaryPreset?.Profile.Name ?? "둘째 친구" : Chat.Preferences.Target == "both" ? Chat.Preferences.Profile.Name + " + " + (SecondaryPreset?.Profile.Name ?? "둘째") : Chat.Preferences.Profile.Name;
    public MusicMonitor Music { get; private set; } = null!;
    MusicPlayerWindow? musicPlayer;
    AdventureWindow? adventureWindow;
    public bool IsListening => Pref.Music.Enabled && Pref.Music.Sprites && Music?.Song.Playing == true;
    GameWindow? gameWindow;
    readonly Button quickButton;
    readonly PetConversation conversation;
    readonly TypingBounce bounce;
    readonly Grid body;
    readonly HeartTrail hearts = new();
    readonly StrokeGesture strokes = new();
    CompanionWindow? companion;
    public Rect PetBounds
    {
        get
        {
            if (body == null || !IsLoaded) return new Rect(Left, Top, Math.Max(1, Width), Math.Max(1, Pref.Size));
            var local = image.Visibility == Visibility.Visible ? image.TransformToAncestor(this).TransformBounds(PetImageLayout.ImageBounds(image)) : character.TransformToAncestor(this).TransformBounds(new Rect(0, 0, character.ActualWidth, character.ActualHeight));
            local.Offset(Left, Top); return local;
        }
    }
    // Re-raising a pet while its right-click menu is open would push the pet above the menu popup.
    bool MenuOpen => ContextMenu?.IsOpen == true || companion?.ContextMenu?.IsOpen == true;
    // Pets also stay put while one of this app's own windows is active, so they do not jump back over it.
    public void RaisePets() { if (MenuOpen || Native.ForegroundProcess() == "KeyboardWorker") return; Native.RaisePet(hwnd); companion?.Raise(); bubble.Raise(); musicPlayer?.Raise(); }
    public Rect? MusicBounds => musicPlayer?.IsVisible == true ? new Rect(musicPlayer.Left, musicPlayer.Top, musicPlayer.ActualWidth, musicPlayer.ActualHeight) : null;
    public void OpenAdventure() => OpenAdventure(-1);
    public void OpenAdventure(int page)
    {
        if (adventureWindow != null) Pastel.Front(adventureWindow);
        else { adventureWindow = new AdventureWindow(this); adventureWindow.Closed += (_, _) => adventureWindow = null; adventureWindow.Show(); }
        if (page >= 0) adventureWindow.ShowPage(page);
    }
    // Story milestones are celebrated above the pet. While the pet is hidden or in quiet mode the newest one waits.
    CelebrationWindow? celebration; (string Headline, string Title, string Detail, string Action, Action Open)? pendingCelebration;
    public void Celebrate(string headline, string title, string detail, string action, int page) => CelebrateWith(headline, title, detail, action, () => OpenAdventure(page));
    public void CelebrateWith(string headline, string title, string detail, string action, Action open)
    {
        if (closing) return;
        if (hidden || IsQuiet) { pendingCelebration = (headline, title, detail, action, open); return; }
        pendingCelebration = null; celebration?.Close();
        celebration = new CelebrationWindow(this, headline, title, detail, action, open, true);
        var shown = celebration;
        shown.Closed += (_, _) => { if (ReferenceEquals(celebration, shown)) celebration = null; };
        shown.Show(); Express("happy", 6); companion?.Express("happy"); PlayVoice(false, "celebrate", "happy", "");
    }
    void ShowWaitingCelebration() { if (pendingCelebration is { } waiting && !hidden && !IsQuiet) CelebrateWith(waiting.Headline, waiting.Title, waiting.Detail, waiting.Action, waiting.Open); }
    public void OpenBackup(Window owner) => new BackupWindow(this) { Owner = owner, ShowInTaskbar = false }.ShowDialog();
    public void Restart()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = false })?.Dispose(); Close();
    }
    public void OpenUsage() => new UsagePerformanceWindow(this) { ShowInTaskbar = false }.ShowDialog();
    public void OpenMusicSettings() => new MusicSettingsWindow(this) { ShowInTaskbar = false }.ShowDialog();
    // Forget where a bubble was dragged to and place it automatically again.
    public void ResetBubble(bool second)
    {
        if (second) Pref.CompanionBubbleSpot = null; else Pref.BubbleSpot = null;
        Storage.Save(Pref); bubble.Reposition(); companion?.PlaceBubble();
    }
    // Windows keep the colours they were built with, so a new theme is applied by starting again.
    public void ChangeTheme(string id)
    {
        var next = Pref.Snapshot(); next.Theme = id; Apply(next, configureKeys: false); Restart();
    }
    public void ToggleFlip()
    {
        var next = Pref.Snapshot(); next.FlipImages = !next.FlipImages; Apply(next, configureKeys: false); _ = Presets.SaveAppearanceAsync(next);
    }
    public void SayTo(string id, string text, string mood)
    {
        if (id == CharacterId) { ChatMood(mood); Say(text); }
        else if (companion?.Preset.Id == id) { companion.Express(mood); companion.Say(text); }
    }
    void SyncMusic()
    {
        if (closing || Music == null) return; Display(state); companion?.RefreshMusic();
        bool visible = Pref.Music.Enabled && Pref.Music.PlayerVisible && Music.Song.Available && !hidden;
        if (visible) { musicPlayer ??= new MusicPlayerWindow(this); musicPlayer.Show(); musicPlayer.Update(); }
        else musicPlayer?.Sleep();
    }
    public Rect? OtherPet(Window anchor) => ReferenceEquals(anchor, this) ? companion?.PetBounds : PetBounds;
    public Rect? OtherBubble(Window anchor) => ReferenceEquals(anchor, this) ? companion?.BubbleRect : bubble.IsVisible ? new Rect(bubble.Left, bubble.Top, bubble.ActualWidth, bubble.ActualHeight) : null;
    public void SyncCompanion()
    {
        if (closing) return; var selected = Presets.Items.FirstOrDefault(p => p.Id == Pref.Duo.PresetId);
        if (!Pref.Duo.Enabled || selected == null) { var old = companion; companion = null; old?.Close(); if (selected == null && Pref.Duo.Enabled) { Pref.Duo = Pref.Duo with { Enabled = false }; Storage.Save(Pref); } return; }
        if (companion == null) { companion = new CompanionWindow(this, selected); companion.Show(); }
        else companion.Configure(selected);
        PartyChanged?.Invoke();
    }
    public void SetCompanion(bool enabled) { CancelAutomatic(); Pref.Duo = Pref.Duo with { Enabled = enabled }; SyncCompanion(); Storage.Save(Pref); }
    public async System.Threading.Tasks.Task LoadCompanion(string id)
    {
        if (IsChatBusy) throw new InvalidOperationException("대화 답변 생성이 끝난 뒤 친구를 바꿔 주세요.");
        var selected = Presets.Items.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("둘째 친구 프리셋을 선택해 주세요.");
        if (selected.Id == Pref.ActivePresetId) throw new InvalidOperationException("대표와 다른 프리셋을 선택해 주세요.");
        CancelAutomatic(); Pref.Duo = Pref.Duo with { Enabled = true, PresetId = selected.Id }; SyncCompanion();
        if (companion != null) { await companion.Ready(); companion.ResetPosition(); }
        await Storage.SaveAsync(Pref);
        if (companion == null || !companion.IsVisible) throw new InvalidOperationException("둘째 친구를 표시하지 못했습니다.");
    }
    internal CompanionWindow? DisplayedCompanion => companion;
    public void OpenDuo(int page) { OpenDuo(); duoWindow?.ShowPage(page); }
    public void OpenDuo()
    {
        if (duoWindow != null) { Pastel.Front(duoWindow); return; }
        duoWindow = new DuoWindow(this) { ShowInTaskbar = false }; duoWindow.Closed += (_, _) => duoWindow = null; duoWindow.Show();
    }
    public void ResetCompanionPosition() => companion?.ResetPosition();
    public void OpenDesktopSection(string section) { OpenDesktopOptions(); desktopWindow?.SelectTab(section); }
    public TypingWorkClock Work { get; } = StartupTrace.Time("작업 시간", WorkTimeStore.Load);
    public FocusClock FocusTimer { get; } = new();
    public event Action<ChatTurn>? MessageReceived;
    public event Action? PartyChanged;
    public event Action<string>? MessageStarted;
    public event Action<string>? MessageProgress;
    public event Action<string>? MessageFailed;
    bool sending;
    CancellationTokenSource? activeChatCancellation;
    DuoWindow? duoWindow;
    public bool IsChatBusy => sending || conversation.Busy;
    public void CancelMessage() => activeChatCancellation?.Cancel();
    public string CurrentMessageUser { get; private set; } = "";
    ForegroundMonitor? foregroundMonitor;
    readonly LazyImageCache imageCache;
    string foregroundName = "";
    GlobalHotkeys? hotkeys;
    QuickChatWindow? quickWindow;
    DesktopOptionsWindow? desktopWindow;
    TimerWindow? timerWindow;
    ShortcutGuideWindow? shortcutGuide;
    PresetsWindow? presetsWindow;
    // Created right after the pet is first drawn: loading the tray support was the slowest single step of starting up.
    Forms.NotifyIcon? tray;
    string trayNotice = "";
    void CreateTray()
    {
        if (closing || tray != null) return;
        tray = new Forms.NotifyIcon { Text = "Keyboard Worker", Icon = PetIcon.TrayIcon, Visible = true };
        tray.ContextMenuStrip = MakeTray(); tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
        if (trayNotice.Length > 0) { tray.ShowBalloonTip(5000, "단축키 설정", trayNotice, Forms.ToolTipIcon.Info); trayNotice = ""; }
    }
    InputMonitor? input;
    SettingsWindow? settings;
    ChatWindow? chatWindow;
    ProfileWindow? profileWindow;
    ConnectionsWindow? connectionsWindow;
    RestWindow? restWindow;
    IntPtr hwnd;
    bool hidden, lockedSession, wasPaused, closing;
    long lastWorkSave;
    long savedWorkMs;
    string savedWorkDay = "";
    bool manualHidden, fullscreenNow;
    bool resumeFocusAfterRest;
    AppRule policy = new();
    string state = "idle";
    bool thinking;
    string expression = "idle";
    long expressionUntil;
    readonly Image image = new() { Stretch = Stretch.Uniform };
    public Buddy()
    {
        Title = "Keyboard Worker"; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false;
        ResizeMode = ResizeMode.NoResize; StartupTrace.Mark("창 기본"); Resources = Pastel.Styles(); StartupTrace.Mark("테마");
        bubble = new PetBubbleWindow(this);
        StartupTrace.Mark("말풍선");
        conversation = new PetConversation(Chat);
        imageCache = new(Dispatcher, () => { StartupTrace.Image(); if (!closing) Display(state); });
        Music = new MusicMonitor(Dispatcher, SyncMusic); Adventure.Friend(CharacterId, Chat.Preferences.Profile.Name, Adventure.State.Friends.Count == 0 ? Game.State.Bond : 0);
        equipmentImages = new(Dispatcher, () => { if (!closing) { accessories.InvalidateVisual(); backAccessories.InvalidateVisual(); } });
        accessories.CustomImage = equipmentImages.Get;
        backAccessories.CustomImage = equipmentImages.Get;
        savedWorkMs = Work.TodayMs; savedWorkDay = Work.Day; StartupTrace.Mark("음악·캐시");
        var grid = new DockPanel(); var footer = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        quickButton = Pastel.Button("♡ 한마디", OpenQuickChat); quickButton.Padding = new Thickness(10, 3, 10, 3); quickButton.FontSize = 12;
        footer.Children.Add(quickButton); workLabel.Margin = new Thickness(0); workLabel.FontWeight = FontWeights.SemiBold; workLabel.Foreground = Pastel.Ink;
        workBackground = new Border
        {
            Child = workLabel,
            Background = Brushes.White,
            BorderBrush = Pastel.Line,
            BorderThickness = new Thickness(1.5, 1.5, 1.5, 3),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(0, 2, 0, 2)
        };
        footer.Children.Add(workBackground);
        footer.Children.Add(BuildTodo());
        gameBadge = Pastel.Button("Lv.1 · 20코인", () => OpenHome(3), true); gameBadge.Padding = new Thickness(8, 3, 8, 3); gameBadge.FontSize = 12; footer.Children.Add(gameBadge);
        DockPanel.SetDock(footer, Dock.Bottom); grid.Children.Add(footer);
        body = new Grid { Background = Brushes.Transparent };
        body.Children.Add(backAccessories);
        body.Children.Add(character);
        body.Children.Add(image);
        body.Children.Add(accessories);
        body.Children.Add(hearts);
        bounce = new TypingBounce(body);
        grid.Children.Add(body);
        Content = grid;
        body.MouseMove += (_, e) =>
        {
            if (hidden || Pref.ClickThrough || e.LeftButton == MouseButtonState.Pressed) return;
            var at = e.GetPosition(this); var pet = PetBounds; pet.Offset(-Left, -Top);
            if (!pet.Contains(at)) { strokes.Reset(); return; }
            if (Pref.PettingHearts && !light) hearts.Emit(e.GetPosition(body));
            if (Pref.MousePetting && strokes.Move(e.GetPosition(body), Environment.TickCount64)) Pat();
        };
        body.MouseLeave += (_, _) => strokes.Reset();
        Game.Changed += RefreshGame; Presets.Changed += SyncCompanion;
        Adventure.StageReached += (id, name, stage) => Dispatcher.BeginInvoke(() => StageUp(id, name, stage));
        Adventure.QuestReached += quest => Dispatcher.BeginInvoke(() => Celebrate("퀘스트 달성!", quest.Title, "목표한 작업 시간을 채웠어요. 이제 완료 이야기를 받을 수 있어요.", "퀘스트 보기", 2));
        MouseLeftButtonDown += (_, e) =>
        {
            if (FindButton(e.OriginalSource as DependencyObject)) return;
            CancelAutomatic();
            if (e.ClickCount == 2) { OpenHome(); e.Handled = true; return; } if (Pref.Locked) return;
            try { DragMove(); awayFromHome = false; ClampPosition(); SavePosition(); bubble.Reposition(); } catch (InvalidOperationException) { } e.Handled = true;
        };
        StartupTrace.Mark("캐릭터 화면");
        MouseWheel += (_, e) => { if (Native.CtrlDown) { e.Handled = true; ResizeBy(e.Delta > 0 ? 10 : -10); } };
        ContextMenu = MakeContext();
        Pastel.PlaceBeside(ContextMenu);
        ContextMenu.Closed += (_, _) => RaisePets();
        StartupTrace.Mark("우클릭 메뉴"); Icon = PetIcon.WindowIcon; StartupTrace.Mark("아이콘");
        clock.Tick += (_, _) => Tick();
        animation.Tick += (_, _) =>
        {
            animation.Stop(); Display(IdlePose());
        };
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd)!;
            source.AddHook((IntPtr h, int m, IntPtr w, IntPtr l, ref bool done) => { if (m == 0x21) { done = true; return new IntPtr(3); } return IntPtr.Zero; });
            try { input = new InputMonitor(source, OnInput, Pref.MouseActivity); }
            catch (Exception e) { MessageBox.Show("키보드 입력 감지를 시작하지 못했습니다.\n" + e.Message); Close(); return; }
            hotkeys = new GlobalHotkeys(source, Shortcut);
            foregroundMonitor = new ForegroundMonitor(Dispatcher, () => { RefreshPolicy(true); RaisePets(); Tick(); });
            try { hotkeys.Configure(Pref.Desktop.Shortcuts); } catch (Exception e) { trayNotice = e.Message; }
            Apply(Pref, false); ClampPosition(); clock.Start();
            SystemEvents.SessionSwitch += SessionSwitch;
            SystemEvents.PowerModeChanged += PowerChanged;
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(RefreshScale);
        StartupTrace.Mark("입력 연결");
        ContentRendered += (_, _) =>
        {
            StartupTrace.Shown(Pref.ImagePaths.ContainsKey("idle")); Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, CreateTray); Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, Welcome); ScheduleUpkeep();
            var patience = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; patience.Tick += (_, _) => { patience.Stop(); StartupTrace.Expire(); }; patience.Start();
        };
        Closing += (_, _) => Shutdown();
    }
    // Saves what is still in memory, then lets go of timers, hooks and every window this pet opened.
    void Shutdown()
    {
        closing = true; CancelAutomatic();
        companion?.Close(); duoWindow?.Close(); Music.Dispose(); musicPlayer?.Close(); celebration?.Close(); adventureWindow?.Close(); CloseBondWindows(); CloseProgressWindows(); CloseStageWindows(); voice.Dispose(); Progress.Save(true);
        Adventure.Save(true); Usage.SetWork(Work.Day, Work.TodayMs); Usage.Save(true);
        linesWindow?.Close(); hearts.Dispose(); SavePosition();
        Game.Save(true); Game.Changed -= RefreshGame; Presets.Changed -= SyncCompanion;
        WorkTimeStore.Save(Work);
        clock.Stop(); animation.Stop(); bounce.Dispose();
        foregroundMonitor?.Dispose(); imageCache.Dispose(); equipmentImages.Dispose(); hotkeys?.Dispose(); input?.Dispose(); audio.Dispose(); tray?.Dispose();
        SystemEvents.SessionSwitch -= SessionSwitch; SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        settings?.Close(); chatWindow?.Close(); profileWindow?.Close(); connectionsWindow?.Close(); restWindow?.Close(); desktopWindow?.Close(); timerWindow?.Close();
        shortcutGuide?.Close(); presetsWindow?.Close(); gameWindow?.Close(); quietWindow?.Close(); reactionPreview?.Close(); quickWindow?.Close();
        bubble.Close();
    }
    void SessionSwitch(object sender, SessionSwitchEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        lockedSession = e.Reason == SessionSwitchReason.SessionLock; bool resume = !lockedSession && !clock.IsEnabled && !closing;
        resumeFocusAfterRest = false;
        Work.Suspend();
        FocusTimer.Suspend(Environment.TickCount64);
        input?.Reset();
        animation.Stop();
        bounce.Reset();
        audio.Stop();
        bubble.Hide();
        activity.Reset(Environment.TickCount64);
        Display("idle");
        // Nothing can change while the screen is locked: save progress, hide, and stop ticking until unlock.
        if (lockedSession) { lastWorkSave = 0; Tick(); clock.Stop(); } else if (resume) { clock.Start(); Tick(); }
    });
    void PowerChanged(object sender, PowerModeChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        resumeFocusAfterRest = false;
        Work.Suspend();
        FocusTimer.Suspend(Environment.TickCount64);
        input?.Reset();
        animation.Stop();
        bounce.Reset();
        audio.Stop();
        bubble.Hide();
        activity.Reset(Environment.TickCount64);
        Display("idle");
    });
    void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RestoreLayout);
    // After a monitor is added, removed or rescaled: go back to the saved spot if it is on a screen again, otherwise move onto the nearest screen.
    void RestoreLayout()
    {
        if (closing || hwnd == IntPtr.Zero) return;
        if (Pref.Left != -9999) { Left = Pref.Left; Top = Pref.Top; }
        ClampPosition(); MarkHome(); RefreshScale(); companion?.RestoreLayout(); bubble.Reposition(); if (musicPlayer?.IsVisible == true) musicPlayer.Update(); RaisePets();
    }
    // While the saved spot is off-screen the pet waits on another monitor; keep the saved spot so it can return.
    bool awayFromHome;
    void MarkHome() => awayFromHome = Pref.Left != -9999 && (Math.Abs(Left - Pref.Left) > 1 || Math.Abs(Top - Pref.Top) > 1);
    // Sprites are decoded for the monitor's pixel density, so decode again when the pet lands on a different scale.
    void RefreshScale()
    {
        if (closing || hwnd == IntPtr.Zero) return;
        double dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        imageCache.Configure(Pref, Math.Clamp((int)Math.Ceiling((Pref.Size + 60) * dpi), 100, Pref.LowPower ? 320 : 1024)); RefreshGame(); Display(state);
        if (companion != null) companion.Configure(companion.Preset); bubble.Reposition();
    }
    void Display(string next)
    {
        next = ImageAssets.VisibleState(next, Pref.AdvancedImages);
        state = next;
        string selectedSlot = IsListening && Pref.ImagePaths.ContainsKey(ImageAssets.MusicKey(next)) ? ImageAssets.MusicKey(next) : next;
        BitmapSource? bmp = imageCache.Get(selectedSlot);
        string imageSlot = selectedSlot;
        if (bmp == null && selectedSlot != next) { bmp = imageCache.Get(next); imageSlot = next; }
        if (bmp == null)
        {
            if (next is "happy" or "sad" or "angry" or "surprised" or "shy" or "sleepy" or "caring") { bmp = imageCache.Get("chatting"); imageSlot = "chatting"; }
            if (bmp == null) { bmp = imageCache.Get("idle"); imageSlot = "idle"; }
        }
        var reference = imageCache.Get("idle");
        Pref.ImageAdjustments.TryGetValue(imageSlot, out var correction);
        PetImageLayout.Apply(image, Pref.Size + 60, Pref.Size, reference, correction, Pref.FlipImages);
        var viewport = PetImageLayout.Viewport(Pref.Size + 60, Pref.Size, reference);
        PetImageLayout.ApplyCorrection(accessories, viewport, bmp == null ? null : correction, Pref.FlipImages);
        PetImageLayout.ApplyCorrection(backAccessories, viewport, bmp == null ? null : correction, Pref.FlipImages);
        PetImageLayout.ApplyCorrection(character, viewport, null, Pref.FlipImages);
        if (bmp != null) { image.Source = bmp; image.Visibility = Visibility.Visible; character.Visibility = Visibility.Hidden; }
        else { image.Visibility = Visibility.Hidden; character.Visibility = Visibility.Visible; character.State = next; character.InvalidateVisual(); }
        if (!ReferenceEquals(accessories.AvatarImage, reference)) { accessories.AvatarImage = reference; backAccessories.AvatarImage = reference; accessories.InvalidateVisual(); backAccessories.InvalidateVisual(); }
    }
    public void Apply(Preferences p, bool configureKeys = true, bool persist = true, bool reloadImages = false)
    {
        p.Normalize();
        // Choosing a bubble side or offset in the settings means "place it for me" again.
        if ((p.Desktop.BubblePosition, p.Desktop.BubbleGap, p.Desktop.BubbleOffsetX, p.Desktop.BubbleOffsetY) != (Pref.Desktop.BubblePosition, Pref.Desktop.BubbleGap, Pref.Desktop.BubbleOffsetX, Pref.Desktop.BubbleOffsetY))
        {
            p.BubbleSpot = null;
            p.CompanionBubbleSpot = null;
        }
        if (configureKeys) hotkeys?.ConfigureIfChanged(Pref.Desktop.Shortcuts, p.Desktop.Shortcuts);
        reactionLimiter.Reset();
        bodyLimiter.Reset();
        bounce.Configure(p.Desktop.TypingBounce); bounce.LowPower = p.LowPower;
        Work.Suspend(); Pref = p; Pastel.ShowNotes = p.ShowNotes; body.Margin = p.Desktop.TypingBounce ? new Thickness(10, 18, 10, 8) : new Thickness(0);
        body.Width = p.Size + 60; body.Height = p.Size;
        Width = p.Size + 60 + body.Margin.Left + body.Margin.Right; RefreshTodo(); ResizePet(); Topmost = true; Native.RaisePet(hwnd);
        quickButton.Visibility = p.Desktop.QuickButton ? Visibility.Visible : Visibility.Collapsed; workBackground.Visibility = p.Desktop.ShowWorkOnPet ? Visibility.Visible : Visibility.Collapsed;
        RefreshGame();
        if (p.Left != -9999) { Left = p.Left; Top = p.Top; }
        else { Left = SystemParameters.WorkArea.Right - Width - 20; Top = SystemParameters.WorkArea.Bottom - Height - 20; }
        Native.Style(hwnd, p.ClickThrough);
        double dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        imageCache.Configure(p, Math.Clamp((int)Math.Ceiling((p.Size + 60) * dpi), 100, p.LowPower ? 320 : 1024), reloadImages); RefreshPolicy(true);
        if (reloadImages) { animation.Stop(); bounce.Reset(); expressionUntil = 0; activity.Reset(Environment.TickCount64); state = IdlePose(); }
        audio.Configure(p); Display(state); ClampPosition(); MarkHome(); bubble.Configure(); if (persist) Storage.Save(Pref);
        SyncCompanion(); if (!p.PettingHearts) hearts.Clear();
        hearts.LowPower = p.LowPower; Music.Configure(p.Music); SyncMusic(); input?.Watch(p.MouseActivity);
    }
    string IdlePose()
    {
        if (Pref.AdvancedImages)
        {
            if (thinking) return "thinking";
            if (Environment.TickCount64 < expressionUntil) return expression;
            if (chatWindow?.IsVisible == true) return "chatting";
        }
        return activity.Nudged ? "nudge" : "idle";
    }
    void Express(string pose, int seconds = 6)
    {
        if (!Pref.AdvancedImages) return;
        expression = pose; expressionUntil = Environment.TickCount64 + seconds * 1000L;
        if (!animation.IsEnabled) Display(IdlePose());
    }
    public void BeginChatThinking() { thinking = true; if (!animation.IsEnabled) Display(IdlePose()); }
    public void EndChatThinking() { thinking = false; if (!animation.IsEnabled) Display(IdlePose()); }
    public void ChatMood(string mood) => Express(mood == "neutral" ? "chatting" : mood, Pref.Desktop.ExpressionSeconds);
    bool ExpressionActive() => Pref.AdvancedImages && (thinking || Environment.TickCount64 < expressionUntil);
    void RefreshPolicy(bool force = false)
    {
        string name = Native.ForegroundProcess();
        if (name != foregroundName || force)
        {
            Work.Suspend(); var next = Pref.Desktop.Policy(name);
            if (next.NoReminder != policy.NoReminder) activity.Reset(Environment.TickCount64);
            if (next.Mute && !policy.Mute) audio.Stop(); policy = next; foregroundName = name; apps.Track(name, Native.ForegroundPath, Pref.Awareness, Environment.TickCount64);
        }
        fullscreenNow = Native.Fullscreen(hwnd); RefreshLight();
    }
    static bool FindButton(DependencyObject? item)
    {
        while (item != null) { if (item is Button) return true; item = item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item); } return false;
    }
    void Shortcut(string action)
    {
        if (action == "quick") OpenQuickChat();
        else if (action == "visibility") ToggleVisibility();
        else if (action == "settings") OpenSettings();
        else if (action == "quiet") ToggleQuiet();
        else if (action == "rest") ToggleRest();
    }
    void ToggleVisibility() { manualHidden = !manualHidden; Tick(); }
    void ClampPosition()
    {
        var screen = Forms.Screen.FromHandle(hwnd).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var a = transform.Transform(new Point(screen.Left, screen.Top)); var b = transform.Transform(new Point(screen.Right, screen.Bottom));
        Left = Math.Clamp(Left, a.X, Math.Max(a.X, b.X - Width)); Top = Math.Clamp(Top, a.Y, Math.Max(a.Y, b.Y - Height));
    }
    void SavePosition()
    {
        if (!awayFromHome) { Pref.Left = Left; Pref.Top = Top; }
        try { Storage.Save(Pref); } catch (Exception e) { if (!closing) MessageBox.Show("설정을 저장하지 못했습니다.\n" + e.Message); }
    }
    public long RestSeconds => Math.Max(Math.Max(0, (activity.PauseUntil - Environment.TickCount64 + 999) / 1000), FocusTimer.Phase == "휴식" ? (FocusTimer.RemainingMs + 999) / 1000 : 0);
    public void SetRest(int minutes)
    {
        long now = Environment.TickCount64; Work.Suspend(); minutes = Math.Clamp(minutes, 1, 1440); Pref.Desktop.QuickRestMinutes = minutes; Storage.Save(Pref);
        FocusTimer.SkipBreak(now);
        if (!activity.IsPaused(now)) resumeFocusAfterRest = FocusTimer.Running && !FocusTimer.Paused;
        if (resumeFocusAfterRest) FocusTimer.Suspend(now);
        bool already = activity.IsPaused(now); activity.Pause(now, minutes); wasPaused = true; bubble.Hide(); animation.Stop(); Display("idle");
        if (!already && Auto.Say("rest-start", null, "caring")) OfferScene("rest");
    }
    public void StopRest()
    {
        long now = Environment.TickCount64; Work.Suspend(); activity.Resume(now); FocusTimer.SkipBreak(now);
        if (resumeFocusAfterRest && FocusTimer.Running && FocusTimer.Paused) FocusTimer.TogglePause(now); resumeFocusAfterRest = false;
        bubble.Hide(); Display("idle");
    }
    public void Say(string text) { if (!hidden && !closing) bubble.ShowText(text, true, Pref.Desktop.ReplySeconds); }
    void ResizePet() => Height = Pref.Size + body.Margin.Top + body.Margin.Bottom + (Pref.Desktop.QuickButton ? 34 : 0) + (Pref.Desktop.ShowWorkOnPet ? 30 : 0) + (TodoVisible ? 28 : 0) + (Game.State.ShowBadge ? 32 : 0);
    void RefreshGame()
    {
        gameBadge.Content = $"Lv.{Game.Level} · {Game.State.Coins:N0}코인"; gameBadge.Visibility = Game.State.ShowBadge ? Visibility.Visible : Visibility.Collapsed;
        var outfit = Game.State.Snapshot(); accessories.Outfit = outfit; backAccessories.Outfit = outfit;
        var images = new Preferences { ImagePaths = outfit.CustomItems.Where(c => outfit.Equipped.Values.Contains(c.Id)).ToDictionary(c => c.Id, c => c.ImagePath) };
        double dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        equipmentImages.Configure(images, Math.Clamp((int)Math.Ceiling((Pref.Size + 60) * outfit.AccessoryScale * dpi), 100, 1024));
        accessories.InvalidateVisual(); backAccessories.InvalidateVisual(); ResizePet(); if (hwnd != IntPtr.Zero) ClampPosition();
    }
    void Pat()
    {
        Adventure.Pat(CharacterId, Chat.Preferences.Profile.Name, Environment.TickCount64);
        bool counted = Game.Pat(Environment.TickCount64, WorkTimeStore.Today); Express("pat", Pref.Desktop.PatSeconds); Patted(false, counted);
    }
}
