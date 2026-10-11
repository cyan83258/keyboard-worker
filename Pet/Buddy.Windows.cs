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

// Opening the other windows, quiet mode, and the right-click / tray menus.
public sealed partial class Buddy
{
    public void OpenQuickChat()
    {
        if (quickWindow != null) { Pastel.Front(quickWindow); return; } manualHidden = false; Tick(); bubble.Hide();
        quickWindow = new QuickChatWindow(this); quickWindow.Closed += (_, _) => { quickWindow = null; activity.Reset(Environment.TickCount64); }; quickWindow.Show();
    }
    public void OpenDesktopOptions()
    {
        if (desktopWindow != null) { Pastel.Front(desktopWindow); return; } desktopWindow = new DesktopOptionsWindow(this);
        desktopWindow.Closed += (_, _) => desktopWindow = null;
        desktopWindow.Show();
    }
    public void OpenTimers()
    {
        if (timerWindow != null) { Pastel.Front(timerWindow); return; } timerWindow = new TimerWindow(this); timerWindow.Closed += (_, _) => timerWindow = null; timerWindow.Show();
    }
    public void OpenPresets()
    {
        if (presetsWindow != null) { Pastel.Front(presetsWindow); return; }
        presetsWindow = new PresetsWindow(this); presetsWindow.Closed += (_, _) => presetsWindow = null; presetsWindow.Show();
    }
    public void OpenGame()
    {
        if (gameWindow != null) { Pastel.Front(gameWindow); return; }
        gameWindow = new GameWindow(this); gameWindow.Closed += (_, _) => gameWindow = null; gameWindow.Show();
    }
    public void GameNotice(string text) { if (!IsQuiet && Game.State.Announcements && !IsChatBusy && !bubble.IsVisible && !string.IsNullOrEmpty(text)) Say(text); }
    public void StartQuiet(int minutes) { quiet.Start(Environment.TickCount64, minutes); audio.Stop(); bounce.Reset(); if (!bubble.IsReply) bubble.Hide(); Tick(); }
    public void StopQuiet() { quiet.Stop(); Tick(); }
    public void ToggleQuiet() { if (IsQuiet) StopQuiet(); else StartQuiet(0); }
    public void OpenQuiet()
    {
        if (quietWindow != null) { Pastel.Front(quietWindow); return; } quietWindow = new QuietWindow(this); quietWindow.Closed += (_, _) => quietWindow = null; quietWindow.Show();
    }
    public void OpenReactionPreview(Window owner, Func<DesktopOptions> options)
    {
        if (reactionPreview != null) { Pastel.Front(reactionPreview); return; }
        reactionPreview = new ReactionPreviewWindow(this, options) { Owner = owner, ShowInTaskbar = false }; reactionPreview.Closed += (_, _) => reactionPreview = null; reactionPreview.Show();
    }
    public void ApplyCharacterPreset(CharacterPreset preset, bool clearHistory)
    {
        if (IsChatBusy) throw new InvalidOperationException("답변 생성이 끝나거나 중단한 뒤 캐릭터를 바꿔 주세요.");
        CancelAutomatic();
        var next = preset.Prepare(Pref, Chat.Preferences);
        Apply(next.Settings); Chat.Apply(next.Chat); if (clearHistory) Chat.Clear(); expressionUntil = 0; bubble.Hide(); Display("idle");
    }
    public bool ShortcutRegistered(string action) => hotkeys?.IsRegistered(action) == true;
    public void OpenShortcutGuide()
    {
        if (shortcutGuide != null) { Pastel.Front(shortcutGuide); return; }
        shortcutGuide = new ShortcutGuideWindow(this); shortcutGuide.Closed += (_, _) => shortcutGuide = null; shortcutGuide.Show();
    }
    public void OpenRest()
    {
        if (restWindow != null) { Pastel.Front(restWindow); return; } restWindow = new RestWindow(this); restWindow.Closed += (_, _) => restWindow = null; restWindow.Show();
    }
    public void OpenChat()
    {
        if (chatWindow != null) { Pastel.Front(chatWindow); return; } chatWindow = new ChatWindow(this, Chat);
        chatWindow.Closed += (_, _) => { chatWindow = null; thinking = false; activity.Reset(Environment.TickCount64); Display(IdlePose()); };
        chatWindow.Show();
        Display(IdlePose());
    }
    public void OpenProfile()
    {
        if (profileWindow != null) { Pastel.Front(profileWindow); return; } profileWindow = new ProfileWindow(Chat);
        profileWindow.Closed += (_, _) => { profileWindow = null; activity.Reset(Environment.TickCount64); };
        profileWindow.Show();
    }
    public void OpenConnections()
    {
        if (connectionsWindow != null) { Pastel.Front(connectionsWindow); return; } connectionsWindow = new ConnectionsWindow(Chat);
        connectionsWindow.Closed += (_, _) => { connectionsWindow = null; activity.Reset(Environment.TickCount64); };
        connectionsWindow.Show();
    }
    public void OpenSettings()
    {
        if (settings != null) { Pastel.Front(settings); return; }
        activity.Reset(Environment.TickCount64); bubble.Hide(); audio.Stop();
        settings = new SettingsWindow(this, audio);
        settings.Closed += (_, _) => { settings = null; activity.Reset(Environment.TickCount64); };
        settings.Show();
    }
    // The menus hold only what is done to the pet right now. Looking at things is in the home window, changing things in the settings.
    void ToggleRest() { if (RestSeconds > 0) StopRest(); else SetRest(Pref.Desktop.QuickRestMinutes); }
    ContextMenu MakeContext()
    {
        var menu = Pastel.Menu(new ContextMenu());
        void Add(System.Windows.Controls.ItemsControl parent, string title, Action action) { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); parent.Items.Add(item); }
        MenuItem Group(string title) { var item = new MenuItem { Header = title }; menu.Items.Add(item); return item; }
        Add(menu, "한마디 보내기", OpenQuickChat);
        Add(menu, "홈 열기", OpenHome);
        Add(menu, "빠른 설정", OpenQuickPanel);
        var modes = Group("상황 프로필");
        foreach (var mode in AllModes) { var chosen = mode; Add(modes, (mode.Id == Pref.ActiveMode ? "✓ " : "") + mode.Name, () => ApplyMode(chosen.Id)); }
        if (Pref.Modes.Count > 0) modes.Items.Add(new Separator());
        Add(modes, "프로필 만들기 · 고치기…", OpenQuickPanel);
        var now = Group("지금");
        Add(now, "숨기기 / 보이기", ToggleVisibility);
        Add(now, "조용한 모드 켜기 / 끄기", ToggleQuiet);
        Add(now, "휴식 시작 / 중단", ToggleRest);
        Add(now, "위치 고정 / 해제", () => { Pref.Locked = !Pref.Locked; SavePosition(); });
        Add(now, "말풍선 위치 자동으로 되돌리기", () => ResetBubble(false));
        Add(menu, "설정", OpenSettings); menu.Items.Add(new Separator()); Add(menu, "종료", Close); return menu;
    }
    Forms.ContextMenuStrip MakeTray()
    {
        var menu = new Forms.ContextMenuStrip();
        void Add(Forms.ToolStripItemCollection parent, string label, Action action) => parent.Add(label, null, (_, _) => Dispatcher.Invoke(action));
        Forms.ToolStripMenuItem Group(string title) { var item = new Forms.ToolStripMenuItem(title); menu.Items.Add(item); return item; }
        Add(menu.Items, "한마디 보내기", OpenQuickChat);
        Add(menu.Items, "홈 열기", OpenHome);
        Add(menu.Items, "빠른 설정", OpenQuickPanel);
        var modes = Group("상황 프로필");
        foreach (var mode in AllModes) { var chosen = mode; Add(modes.DropDownItems, (mode.Id == Pref.ActiveMode ? "✓ " : "") + mode.Name, () => ApplyMode(chosen.Id)); }
        Add(modes.DropDownItems, "프로필 만들기 · 고치기…", OpenQuickPanel);
        var now = Group("지금");
        Add(now.DropDownItems, "숨기기 / 보이기", ToggleVisibility);
        Add(now.DropDownItems, "조용한 모드 켜기 / 끄기", ToggleQuiet);
        Add(now.DropDownItems, "휴식 시작 / 중단", ToggleRest);
        // The tray is the way back when the pet cannot be reached with the mouse.
        Add(now.DropDownItems, "클릭 통과 / 해제", () => { Pref.ClickThrough = !Pref.ClickThrough; Native.Style(hwnd, hidden || Pref.ClickThrough); SavePosition(); });
        Add(now.DropDownItems, "화면 안으로 위치 복원", () => { Pref.Left = -9999; Apply(Pref); ResetCompanionPosition(); });
        Add(menu.Items, "설정", OpenSettings); Add(menu.Items, "종료", Close); return menu;
    }
}