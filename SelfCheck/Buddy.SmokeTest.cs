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

// In-app smoke test (--smoke-test): opens every window once and captures screenshots.
public sealed partial class Buddy
{
    public void CaptureSmokeTest()
    {
        foreach (var pose in new[] { "idle", "typing-left", "typing-right", "nudge" })
        {
            Display(pose); UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render((Visual)Content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "preview-" + pose + ".png")); encoder.Save(stream);
        }
        OpenSettings(); settings!.UpdateLayout();
        Pastel.Capture(settings, Path.Combine(AppContext.BaseDirectory, "ui-settings.png")); settings.CapturePages(AppContext.BaseDirectory); settings.CaptureCategories(AppContext.BaseDirectory);
        OpenProfile();
        Pastel.Capture(profileWindow!, Path.Combine(AppContext.BaseDirectory, "ui-profile.png"));
        profileWindow!.CaptureMemory(Path.Combine(AppContext.BaseDirectory, "ui-reply-settings.png"));
        OpenConnections(); Pastel.Capture(connectionsWindow!, Path.Combine(AppContext.BaseDirectory, "ui-connections.png"));
        OpenRest(); Pastel.Capture(restWindow!, Path.Combine(AppContext.BaseDirectory, "ui-rest.png"));
        OpenChat(); Pastel.Capture(chatWindow!, Path.Combine(AppContext.BaseDirectory, "ui-chat.png"));
        MessageStarted?.Invoke("잠깐 같이 이야기할까?"); MessageProgress?.Invoke("좋아, 옆에서 함께 톡톡…");
        Pastel.Capture(chatWindow!, Path.Combine(AppContext.BaseDirectory, "ui-streaming-chat.png")); MessageFailed?.Invoke("화면 검사 예시 · 저장하지 않았어요.");
        OpenQuickChat(); Pastel.Capture(quickWindow!, Path.Combine(AppContext.BaseDirectory, "ui-quick-chat.png"));
        OpenDesktopOptions(); Pastel.Capture(desktopWindow!, Path.Combine(AppContext.BaseDirectory, "ui-desktop.png")); desktopWindow!.CaptureTabs(AppContext.BaseDirectory);
        OpenReactionPreview(desktopWindow!, () => Pref.Desktop.Snapshot());
        Pastel.Capture(reactionPreview!, Path.Combine(AppContext.BaseDirectory, "ui-reaction-preview.png"));
        reactionPreview!.Close();
        OpenQuiet(); Pastel.Capture(quietWindow!, Path.Combine(AppContext.BaseDirectory, "ui-quiet-mode.png"));
        OpenTimers(); Pastel.Capture(timerWindow!, Path.Combine(AppContext.BaseDirectory, "ui-timers.png"));
        OpenShortcutGuide(); Pastel.Capture(shortcutGuide!, Path.Combine(AppContext.BaseDirectory, "ui-shortcut-guide.png"));
        OpenPresets(); Pastel.Capture(presetsWindow!, Path.Combine(AppContext.BaseDirectory, "ui-presets.png"));
        OpenGame(); Pastel.Capture(gameWindow!, Path.Combine(AppContext.BaseDirectory, "ui-game.png")); gameWindow!.CaptureTabs(AppContext.BaseDirectory);
        var gearEditor = new CustomGearWindow(Game); gearEditor.Show(); Pastel.Capture(gearEditor, Path.Combine(AppContext.BaseDirectory, "ui-custom-gear.png")); gearEditor.Close();
        var originalOutfit = accessories.Outfit;
        var demo = new GameState();
        demo.Equipped["head"] = "pink-bow";
        demo.Equipped["effect"] = "sparkles";
        accessories.Outfit = demo;
        accessories.InvalidateVisual();
        UpdateLayout();
        var dressed = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
        dressed.Render((Visual)Content);
        var dressEncoder = new PngBitmapEncoder();
        dressEncoder.Frames.Add(BitmapFrame.Create(dressed));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "ui-accessory-preview.png"))) dressEncoder.Save(stream);
        accessories.Outfit = originalOutfit; accessories.InvalidateVisual();
        try { string old = Path.Combine(AppContext.BaseDirectory, "ui-test-data"); if (Directory.Exists(old)) Directory.Delete(old, true); } catch (IOException) { }
        var sampleRepo = new ChatRepository(Path.Combine(AppContext.BaseDirectory, "ui-test-data", Guid.NewGuid().ToString("N"))); sampleRepo.Preferences.KeepTurns = 200;
        for (int n = 0; n < 200; n++) sampleRepo.Turns.Add(new ChatTurn("화면 검사 메시지 " + n, "이 대화는 화면 가상화 검사에 쓰는 예시입니다. 새 답장만 추가하고 보이는 대화만 그려요 ♡", DateTimeOffset.Now, "모모"));
        var sampleChat = new ChatWindow(this, sampleRepo); sampleChat.Show(); sampleChat.UpdateLayout();
        int realized = sampleChat.RealizedTurns;
        if (realized <= 0 || realized >= sampleChat.DisplayedTurns) throw new Exception("Chat virtualization is not active.");
        Pastel.Capture(sampleChat, Path.Combine(AppContext.BaseDirectory, "ui-chat-virtualized.png")); sampleChat.Close();
        if (foregroundMonitor?.Registered != true) throw new Exception("Foreground event hook registration failed.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "optimization-checks.txt"), $"Foreground event hook: registered\nPeriodic check: {clock.Interval.TotalMilliseconds} ms (previous: 250 ms)\nChat turns: 200; realized item containers: {realized}\nImage cache: shared files reuse one decode; advanced states loaded on demand (covered by self-test)\nStorage: one background queue, atomic replacement and shutdown flush\nStreaming UI: updates limited to 10 per second\nThese are structural checks, not a CPU/memory benchmark.\n");
        bubble.ShowText("잠깐 한마디 해도 좋아. 여기서 같이 톡톡 해볼게 ♡", true, Pref.Desktop.ReplySeconds); Pastel.Capture(bubble, Path.Combine(AppContext.BaseDirectory, "ui-floating-reply.png"));
        if (!Native.NoActivate(new WindowInteropHelper(bubble).Handle)) throw new Exception("Reply bubble must not activate the foreground window.");
        if (!Native.RemoveSystemBorder(new WindowInteropHelper(settings).Handle)) throw new Exception("DWM border suppression was not applied.");
        var correctionBitmap = imageCache.Get("idle") ?? new RenderTargetBitmap(160, 160, 96, 96, PixelFormats.Pbgra32);
        var correctionEditor = new ImageAdjustmentWindow("왼손 타이핑", imageCache.Get("typing-left") ?? correctionBitmap, correctionBitmap, new(), _ => { });
        correctionEditor.Show();
        Pastel.Capture(correctionEditor, Path.Combine(AppContext.BaseDirectory, "ui-image-correction.png"));
        correctionEditor.Close();
        var cleanupEditor = new AssetCleanupWindow(this); cleanupEditor.Show(); Pastel.Capture(cleanupEditor, Path.Combine(AppContext.BaseDirectory, "ui-asset-cleanup.png")); cleanupEditor.Close();
        var automaticEditor = new AutomaticDialogueWindow(this);
        automaticEditor.Show();
        Pastel.Capture(automaticEditor, Path.Combine(AppContext.BaseDirectory, "ui-automatic-dialogue.png"));
        automaticEditor.Close();
        var duoEditor = new DuoWindow(this);
        duoEditor.Show();
        Pastel.Capture(duoEditor, Path.Combine(AppContext.BaseDirectory, "ui-duo-settings.png"));
        duoEditor.CaptureInteraction(Path.Combine(AppContext.BaseDirectory, "ui-duo-interaction.png"));
        duoEditor.Close();
        var party = new CelebrationWindow(this, "새 이야기 도착", "달빛 도서관의 작은 열쇠", "이야기책에 담았어요 · 보상: 희귀 재료 · 반짝이는 별가루", "이야기 읽기", () => { }, false);
        party.Show();
        Pastel.Capture(party, Path.Combine(AppContext.BaseDirectory, "ui-celebration.png"));
        party.Close();
        var touchEditor = new PetTouchWindow(this); touchEditor.Show(); Pastel.Capture(touchEditor, Path.Combine(AppContext.BaseDirectory, "ui-petting-settings.png")); touchEditor.Close();
        var bookEditor = new AdventureWindow(this); bookEditor.Show(); bookEditor.CaptureTabs(AppContext.BaseDirectory); bookEditor.Close();
        var usageEditor = new UsagePerformanceWindow(this); usageEditor.Show(); Pastel.Capture(usageEditor, Path.Combine(AppContext.BaseDirectory, "ui-usage-performance.png")); usageEditor.Close();
        var backupEditor = new BackupWindow(this); backupEditor.Show(); Pastel.Capture(backupEditor, Path.Combine(AppContext.BaseDirectory, "ui-backup.png")); backupEditor.Close();
        var statsView = new StatsWindow(this); statsView.Show(); Pastel.Capture(statsView, Path.Combine(AppContext.BaseDirectory, "ui-stats.png")); statsView.Close();
        var linesView = new LineHistoryWindow(this); linesView.Show(); Pastel.Capture(linesView, Path.Combine(AppContext.BaseDirectory, "ui-line-history.png")); linesView.Close();
        string shots = AppContext.BaseDirectory;
        var lineBook = new LineBookWindow(this); lineBook.Show(); Pastel.Capture(lineBook, Path.Combine(shots, "ui-line-book.png")); lineBook.Close();
        var voiceEditor = new VoiceWindow(this); voiceEditor.Show(); Pastel.Capture(voiceEditor, Path.Combine(shots, "ui-voice.png")); voiceEditor.Close();
        var days = new AnniversaryWindow(this); days.Show(); Pastel.Capture(days, Path.Combine(shots, "ui-anniversary.png")); days.Close();
        var kept = new CollectionWindow(this); kept.Show(); Pastel.Capture(kept, Path.Combine(shots, "ui-collection.png")); kept.Close();
        var pairEditor = new PairWindow(this); pairEditor.Show();
        for (int i = 0; i < pairEditor.Pages; i++) { pairEditor.ShowPage(i); pairEditor.UpdateLayout(); Pastel.Capture(pairEditor, Path.Combine(shots, "ui-pair-" + i + ".png")); }
        pairEditor.Close();
        var manual = new GuideWindow(this); manual.Show();
        for (int i = 0; i < manual.Count; i++) { manual.SelectIndex(i); Pastel.Capture(manual, Path.Combine(shots, "ui-guide-" + i + ".png")); }
        manual.Close();
        var homeView = new HomeWindow(this); homeView.Show();
        for (int i = 0; i < homeView.Pages; i++) { homeView.ShowPage(i); homeView.UpdateLayout(); Pastel.Capture(homeView, Path.Combine(shots, "ui-home-" + i + ".png")); }
        homeView.Close();
        var pageView = new CharacterWindow(this); pageView.Show(); pageView.UpdateLayout(); Pastel.Capture(pageView, Path.Combine(shots, "ui-character.png"));
        var artView = new StageSettingsWindow(this, StageCharacters()[0]); artView.Show();
        for (int i = 0; i < artView.Pages; i++) { artView.ShowPage(i); artView.UpdateLayout(); Pastel.Capture(artView, Path.Combine(shots, "ui-stage-settings-" + i + ".png")); }
        artView.Close(); pageView.Close();
        var textView = new MessengerWindow(this); textView.Show(); textView.UpdateLayout(); Pastel.Capture(textView, Path.Combine(shots, "ui-messenger.png"));
        var textSettings = new StageSettingsWindow(this, StageCharacters()[0], true); textSettings.Show(); textSettings.UpdateLayout(); Pastel.Capture(textSettings, Path.Combine(shots, "ui-messenger-settings.png")); textSettings.Close(); textView.Close();
        var stickerView = new StickerWindow(this); stickerView.Show(); stickerView.UpdateLayout(); Pastel.Capture(stickerView, Path.Combine(shots, "ui-stickers.png")); stickerView.Close();
        var roomView = new StageRoomWindow(this, StageModeNow.Thread(StageCharacters()[0].Id), StageCharacters()[0].Name); roomView.Show(); roomView.UpdateLayout(); Pastel.Capture(roomView, Path.Combine(shots, "ui-stage-room.png")); roomView.Close();
        foreach (bool keptView in new[] { false, true }) { var historyView = new StageHistoryWindow(this, StageCharacters()[0].Id, keptView); historyView.Show(); historyView.UpdateLayout(); Pastel.Capture(historyView, Path.Combine(shots, keptView ? "ui-stage-clips.png" : "ui-stage-history.png")); historyView.Close(); }
        // The two surfaces with a made-up answer: stage directions apart from the speech, a long answer in pages, and text messages.
        string sampleAnswer = "(팔짱을 낀 채 창밖을 본다) 흥, 이제야 왔군. 기다리게 한 값은 치러야지. 오늘 있었던 일을 하나도 빠짐없이 말해 보아라. (작게 웃으며) 지루한 이야기라도 들어는 주마. 다만 변명은 듣지 않겠다.";
        var stageSample = new StageView { Width = 760, Height = 520 }; var sampleHost = new Window { Content = stageSample, Width = 780, Height = 560, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None }; sampleHost.Show(); sampleHost.UpdateLayout();
        stageSample.Present("루카", sampleAnswer, false); sampleHost.UpdateLayout(); Pastel.Capture(sampleHost, Path.Combine(shots, "ui-stage-sample.png")); sampleHost.Close();
        var textSample = new MessengerView { Width = 760, Height = 520 }; var textHost = new Window { Content = textSample, Width = 780, Height = 560, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None }; textHost.Show();
        textSample.Load(new[] { new ChatTurn("뭐 해?", "별일 없다.\n너야말로 이 시간에 무슨 일이냐.", DateTimeOffset.Now.AddMinutes(-3), "루카"), new ChatTurn("그냥 생각나서", "…흥.\n싫지는 않군.", DateTimeOffset.Now, "루카") }); textSample.Asked("내일 볼래?"); textSample.Streaming("생각해 보지."); textHost.UpdateLayout(); Pastel.Capture(textHost, Path.Combine(shots, "ui-messenger-sample.png")); textHost.Close();
        var giftView = new GiftWindow(this); giftView.Show();
        for (int i = 0; i < giftView.Pages; i++) { giftView.ShowPage(i); giftView.UpdateLayout(); Pastel.Capture(giftView, Path.Combine(shots, "ui-gifts-" + i + ".png")); }
        giftView.Close();
        // One gift of every rarity, as the cards look after a draw.
        var cardRow = new WrapPanel { Margin = new Thickness(16), Background = Pastel.Paper }; foreach (var sampleGift in new[] { "꽃다발", "향초", "오르골", "별빛 향수", "달의 반지" }.Select(n => GiftShop.Find(n)!)) { var giftCard = GiftParts.Card(sampleGift, sampleGift.Stars); GiftParts.Pop(giftCard, sampleGift, false); cardRow.Children.Add(giftCard); }
        cardRow.Children.Add(GiftParts.Card(GiftShop.All[0], -1, true));
        var cardHost = new Window { Content = cardRow, Width = 920, Height = 240, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None }; cardHost.Show(); cardHost.UpdateLayout(); Pastel.Capture(cardHost, Path.Combine(shots, "ui-gift-cards.png")); cardHost.Close();
        var pickView = new GiftPicker(this, StageCharacters()[0]); pickView.Show(); Pastel.Capture(pickView, Path.Combine(shots, "ui-gift-picker.png")); pickView.Close();
        var pairView = new PairStageWindow(this); pairView.Show(); pairView.UpdateLayout(); Pastel.Capture(pairView, Path.Combine(shots, "ui-pair-stage.png")); pairView.Close();
        var fontView = new FontWindow(this); fontView.Show(); Pastel.Capture(fontView, Path.Combine(shots, "ui-fonts.png")); fontView.Close();
        var goalsView = new ProgressWindow(this); goalsView.Show();
        for (int i = 0; i < goalsView.Pages; i++) { goalsView.ShowPage(i); goalsView.UpdateLayout(); Pastel.Capture(goalsView, Path.Combine(shots, "ui-progress-" + i + ".png")); }
        goalsView.Close();
        var quickView = new QuickPanelWindow(this); quickView.Show(); Pastel.Capture(quickView, Path.Combine(shots, "ui-quick-panel.png")); quickView.Close();
        var findView = new ChatSearchWindow(this); findView.Show(); Pastel.Capture(findView, Path.Combine(shots, "ui-chat-search.png")); findView.Close();
        var hello = new WelcomeWindow(this); hello.Show(); Pastel.Capture(hello, Path.Combine(shots, "ui-welcome.png")); hello.Close();
        var awareEditor = new AwarenessWindow(this); awareEditor.Show(); Pastel.Capture(awareEditor, Path.Combine(AppContext.BaseDirectory, "ui-awareness.png")); awareEditor.Close();
        var actualSong = Music.Song;
        var actualLine = Music.Timeline;
        Music.Preview(new SongState("별빛 산책 — 머리 위에서 함께 흐르는 긴 제목의 음악", "우리의 작은 플레이리스트", true, "Chrome", true), new TimelineState(TimeSpan.FromSeconds(83), TimeSpan.FromSeconds(225), Environment.TickCount64, false, true));
        var playerPreview = new MusicPlayerWindow(this); playerPreview.Show(); playerPreview.Update(); Pastel.Capture(playerPreview, Path.Combine(AppContext.BaseDirectory, "ui-music-player.png"));
        var unfolded = Pref.Music;
        Pref.Music = unfolded with { Compact = true };
        playerPreview.Update();
        Pastel.Capture(playerPreview, Path.Combine(AppContext.BaseDirectory, "ui-music-player-compact.png"));
        Pref.Music = unfolded;
        playerPreview.Close();
        var musicEditor = new MusicSettingsWindow(this);
        musicEditor.Show();
        Pastel.Capture(musicEditor, Path.Combine(AppContext.BaseDirectory, "ui-music-settings.png"));
        musicEditor.Close();
        Music.Preview(actualSong, actualLine);
        var samplePreset = CharacterPreset.Capture("둘째 예시", Pref, Chat.Preferences.Profile.Snapshot()); samplePreset.Profile.Name += " · 둘째";
        bool temporaryPartner = companion == null;
        var previewPartner = companion ?? new CompanionWindow(this, samplePreset, false);
        if (temporaryPartner) previewPartner.Show();
        previewPartner.UpdateLayout();
        previewPartner.Raise();
        var partnerFrame = new DispatcherFrame(); long partnerDeadline = Environment.TickCount64 + 2000;
        var partnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        partnerTimer.Tick += (_, _) => { if (previewPartner.ImagesReady || Environment.TickCount64 >= partnerDeadline) { partnerTimer.Stop(); partnerFrame.Continue = false; } };
        partnerTimer.Start();
        Dispatcher.PushFrame(partnerFrame);
        if (!Native.IsTopmost(new WindowInteropHelper(previewPartner).Handle) || !Native.NoActivate(new WindowInteropHelper(previewPartner).Handle)) throw new Exception("Secondary pet stacking/focus check failed.");
        previewPartner.Tap(); previewPartner.Capture(Path.Combine(AppContext.BaseDirectory, "ui-secondary-pet.png"));
        var settleFrame = new DispatcherFrame(); long settleDeadline = Environment.TickCount64 + 1000; var settleCheck = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        settleCheck.Tick += (_, _) => { if (previewPartner.CurrentPose == "idle" || Environment.TickCount64 >= settleDeadline) { settleCheck.Stop(); settleFrame.Continue = false; } };
        settleCheck.Start();
        Dispatcher.PushFrame(settleFrame);
        if (previewPartner.CurrentPose != "idle") throw new Exception("Secondary pet did not settle after typing."); if (temporaryPartner) previewPartner.Close();
        hearts.Emit(new Point(body.ActualWidth * .15, body.ActualHeight * .2)); hearts.InvalidateVisual(); UpdateLayout();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "heart-render-check.txt"), $"Overlay size: {hearts.ActualWidth} x {hearts.ActualHeight}; drawing: {VisualTreeHelper.GetDrawing(hearts)?.Bounds}; body: {body.ActualWidth} x {body.ActualHeight}");
        Pastel.Capture(this, Path.Combine(AppContext.BaseDirectory, "ui-petting-hearts.png")); hearts.Clear();
        ContextMenu!.PlacementTarget = this; ContextMenu.IsOpen = true; ContextMenu.UpdateLayout();
        var menuStart = ContextMenu.PointToScreen(new Point(0, 0));
        var menuEnd = ContextMenu.PointToScreen(new Point(ContextMenu.ActualWidth, ContextMenu.ActualHeight));
        var petStart = PointToScreen(new Point(0, 0));
        var petEnd = PointToScreen(new Point(ActualWidth, ActualHeight));
        bool menuCoversPet = menuStart.X < petEnd.X - 1 && menuEnd.X > petStart.X + 1 && menuStart.Y < petEnd.Y - 1 && menuEnd.Y > petStart.Y + 1; ContextMenu.IsOpen = false;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "menu-placement-check.txt"), $"pet {petStart} - {petEnd}; menu {menuStart} - {menuEnd}; covers pet: {menuCoversPet}");
        if (menuCoversPet) throw new Exception("Pet context menu opened over the pet.");
        if (ContextMenu!.Items.OfType<MenuItem>().Any(item => item.Header?.ToString()?.Contains("쓰다듬기") == true) || ContextMenu.Items.Count > 8) throw new Exception("Pet context menu grouping failed.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-test-result.txt"), "PASS: raw input, poses, white timer background, settings, image UI/correction, profile/stream toggle, presets, shared chat, quick input, floating reply, desktop options, timers, game tabs/shop/outfit layers, foreground hook, virtualized chat, DWM border, icon, asset cleanup UI, cleanup\n");
    }
}
