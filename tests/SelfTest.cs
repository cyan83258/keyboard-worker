using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

public static class SelfTest
{
    public static void Run()
    {
        int count = 0;
        void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        var p = new Preferences(); var a = new Activity(0);
        Check(!a.TryNudge(59999, p), "No early reminder");
        Check(a.TryNudge(60000, p), "Reminder at threshold");
        Check(!a.TryNudge(60001, p), "No reminder storm");
        Check(a.TryNudge(120000, p), "Repeat at interval");
        a.Input(120010, true); Check(!a.Nudged && a.IsTyping(120010), "Typing clears reminder");
        Check(!a.IsTyping(120310), "Typing settles after 300ms");
        Check(!a.TryNudge(180009, p), "Input resets idle deadline");
        p.Repeat = false; Check(a.TryNudge(180010, p) && !a.TryNudge(999999, p), "One-shot reminder");
        a.Pause(1000000, 5); Check(a.IsPaused(1299999) && !a.TryNudge(1299999, p), "Pause suppresses reminder");
        a.Resume(1300000); Check(!a.IsPaused(1300000) && !a.TryNudge(1300000, p), "Resume gives fresh interval");
        a.Input(1300001, false); Check(!a.IsTyping(1300001), "Mouse is not typing");
        p.Size = -1; p.Volume = 500; p.IdleSeconds = 0; p.Normalize(); Check(p.Size == 100 && p.Volume == 100 && p.IdleSeconds == 5, "Normalize settings");
        var gate = new KeyPressGate(); var hand = new HandReaction(); int strokes = 0;
        for (int n = 0; n < 20; n++) if (gate.Process(30, 65, 0)) { Check(hand.Tap() == "typing-left", "First key uses left hand"); strokes++; }
        Check(strokes == 1, "Held-key repeats do not animate"); Check(!gate.Process(30, 65, 1), "Key-up does not animate");
        Check(gate.Process(30, 65, 0) && hand.Tap() == "typing-right", "Second press uses right hand");
        Check(gate.Process(48, 66, 0) && hand.Tap() == "typing-left", "Third press alternates");
        gate.Clear(); Check(gate.Process(30, 65, 0), "Resume clears held keys");
        var limiter = new ReactionLimiter(); var limitedHands = new HandReaction(); Check(limiter.Accept(0, 200) && limitedHands.Tap() == "typing-left", "First limited reaction is immediate");
        Check(!limiter.Accept(50, 200) && !limiter.Accept(199, 200), "Fast keys do not advance reaction");
        Check(limiter.Accept(200, 200) && limitedHands.Tap() == "typing-right", "Accepted reactions alternate visible hands");
        limiter.Reset(); Check(limiter.Accept(201, 2000), "Changing pacing resets reaction deadline");
        Check(limiter.Accept(202, 0) && limiter.Accept(203, 0), "Zero interval restores per-key reactions");
        var prefs = new ChatPreferences(); string prompt = CharacterPrompt.Build(prefs.Profile);
        Check(prompt.Contains(prefs.Profile.Name) && prompt.Contains("삭제된 과거"), "Persona and memory boundaries in prompt");
        string secret = SecretVault.Protect("test-only-key"); Check(secret != "test-only-key" && SecretVault.Unprotect(secret) == "test-only-key", "DPAPI encryption roundtrip");
        string testDir = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N")); var repo = new ChatRepository(testDir);
        prefs.KeepTurns = 3; prefs.ContextTurns = 2; repo.Apply(prefs);
        for (int n = 0; n < 8; n++) repo.Add(new ChatTurn("user-" + n, "pet-" + n, DateTimeOffset.Now));
        Check(repo.Turns.Count == 3 && repo.Turns[0].User == "user-5", "Retain newest complete turns only");
        Check(repo.Context().Length == 2 && repo.Context()[0].User == "user-6", "Context subset");
        OrderedStore.FlushAsync().GetAwaiter().GetResult(); var reloaded = new ChatRepository(testDir); Check(reloaded.Turns.Count == 3, "History survives restart");
        prefs.KeepTurns = 1; prefs.ContextTurns = 2; repo.Apply(prefs);
        Check(repo.Turns.Count == 1 && prefs.ContextTurns == 1, "Reducing retention prunes immediately");
        OrderedStore.FlushAsync().GetAwaiter().GetResult();
        string disk = File.ReadAllText(Path.Combine(testDir, "chat-history.json"));
        Check(!disk.Contains("user-5"), "Pruned turns removed from disk");
        prefs.KeepTurns = 0; repo.Apply(prefs); repo.Add(new ChatTurn("not-stored", "reply", DateTimeOffset.Now));
        OrderedStore.FlushAsync().GetAwaiter().GetResult(); Check(repo.Turns.Count == 0 && new ChatRepository(testDir).Turns.Count == 0, "Zero retention stores no conversation");
        Check(AiClient.Parse("Gemini", "{\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"private\"},{\"text\":\"hello\"}]}}]}") == "hello", "Gemini response skips thought");
        Check(AiClient.Parse("Responses", "{\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"hello\"}]}]}") == "hello", "Responses text parsing");
        Check(AiClient.Parse("Chat Completions", "{\"choices\":[{\"message\":{\"content\":\"hello\"}}]}") == "hello", "Chat Completions text parsing");
        bool empty = false; try { AiClient.Parse("Gemini", "{}"); } catch (InvalidOperationException) { empty = true; } Check(empty, "Empty answer is not stored as successful reply");
        foreach (var connection in AiConnection.Defaults())
        {
            connection.Model = "test-model";
            using var request = AiClient.Build(connection, "test-key", prompt, new[] { new ChatTurn("previous", "reply", DateTimeOffset.Now) }, "new", 600);
            string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(body);
            Check(body.Contains("previous") && body.Contains("new") && !body.Contains("test-key"), connection.Name + " context and key separation");
            if (connection.Format == "Responses") Check(!doc.RootElement.GetProperty("store").GetBoolean(), "Responses server storage disabled");
            if (connection.Format == "Gemini") Check(request.Headers.Contains("x-goog-api-key") && doc.RootElement.GetProperty("contents").GetArrayLength() == 3, "Gemini request schema");
        }
        bool rejected = false;
        try { using var bad = AiClient.Build(new AiConnection { Endpoint = "http://example.com", Model = "x" }, "", "", Array.Empty<ChatTurn>(), "", 600); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Remote plaintext endpoint rejected");
        var mockSettings = new ChatPreferences(); mockSettings.Connections[0].Secret = secret;
        using var fake = new HttpClient(new FakeHandler()); var client = new AiClient(fake);
        Check(client.Send(mockSettings, Array.Empty<ChatTurn>(), "hello", CancellationToken.None).GetAwaiter().GetResult() == "mock reply", "Async request roundtrip");
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool cancelled = false;
        try { client.Send(mockSettings, Array.Empty<ChatTurn>(), "hello", cancel.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Request cancellation");
        using var failedHttp = new HttpClient(new FakeHandler(HttpStatusCode.TooManyRequests)); bool rateLimited = false;
        try
        {
            new AiClient(failedHttp).Send(mockSettings, Array.Empty<ChatTurn>(), "hello", CancellationToken.None).GetAwaiter().GetResult();
        } catch (InvalidOperationException e) { rateLimited = e.Message.Contains("429") && !e.Message.Contains("test-only-key"); }
        Check(rateLimited, "Rate limit response without exposing credentials");
        prefs = new ChatPreferences();
        prefs.Profile.Name = "테스트 펫";
        var copy = ChatRepository.Clone(prefs);
        copy.Profile.Name = "변경";
        Check(prefs.Profile.Name == "테스트 펫", "Request uses independent settings snapshot");
        var tagged = PetMood.Parse("좋아, 같이 하자!\n[pet-mood:happy]"); Check(tagged.Text == "좋아, 같이 하자!" && tagged.Mood == "happy", "Mood tag does not leak into chat history");
        Check(PetMood.Parse("안아줄게. 힘들었구나.").Mood == "caring", "Mood fallback when API ignores tag");
        Check(PetMood.Parse("안녕\n[pet-mood:UNKNOWN]").Text == "안녕" && PetMood.Parse("안녕\n[pet-mood:UNKNOWN]").Mood == "neutral", "Unsupported mood tag safely removed");
        Check(ImageAssets.VisibleState("pat", false) == "idle" && ImageAssets.VisibleState("happy", true) == "happy", "Advanced reactions respect on/off");
        Check(ImageAssets.VisibleState("typing-left", false) == "typing-left", "Advanced off preserves typing");
        var source = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgr32, null, new byte[16 * 16 * 4], 16 * 4);
        string png = Path.Combine(testDir, "sample.png"), jpg = Path.Combine(testDir, "sample.jpg");
        var pngEncoder = new PngBitmapEncoder(); pngEncoder.Frames.Add(BitmapFrame.Create(source)); using (var stream = File.Create(png)) pngEncoder.Save(stream);
        var jpgEncoder = new JpegBitmapEncoder(); jpgEncoder.Frames.Add(BitmapFrame.Create(source)); using (var stream = File.Create(jpg)) jpgEncoder.Save(stream);
        Check(ImageAssets.Validate(png) == (16, 16), "PNG slot validation"); Check(ImageAssets.Validate(jpg) == (16, 16), "JPG slot validation");
        string imported = ImageAssets.Import(jpg, testDir); Check(imported.EndsWith(".jpg") && File.Exists(imported) && imported != jpg, "Per-slot import copies JPEG");
        Check(ImageAssets.Read(imported, 16).IsFrozen, "Decoded image reusable and frozen");
        string invalid = Path.Combine(testDir, "invalid.png"); File.WriteAllText(invalid, "invalid"); bool invalidImage = false;
        try { ImageAssets.Validate(invalid); } catch { invalidImage = true; } Check(invalidImage, "Invalid image rejected");
        File.Copy(png, Path.Combine(testDir, "idle.png"));
        var legacy = new Preferences { SkinPath = testDir };
        legacy.Normalize();
        Check(legacy.ImagePaths.ContainsKey("idle"), "Legacy folder settings migrate to individual slots");
        Check(PetIcon.WindowIcon.PixelWidth == 256 && PetIcon.TrayIcon.Width > 0, "Cute WPF and tray icons load");
        Check(typeof(PetIcon).Assembly.GetManifestResourceStream("KeyboardWorker.pet.ico") != null, "Icon embedded in released application");
        var work = new TypingWorkClock("day1"); work.Key(1000, "day1", 2000); Check(work.TodayMs == 0, "First key has no assumed duration");
        work.Key(1500, "day1", 2000); work.Key(1600, "day1", 2000); Check(work.TodayMs == 600, "Count confirmed intervals between keys");
        Check(work.TodayMs == 600 && !work.IsActive(5000, 2000), "Idle tail not counted");
        work.Key(10000, "day1", 2000); Check(work.TodayMs == 600, "Long idle gap excluded"); work.Key(11000, "day1", 2000); Check(work.TodayMs == 1600, "Typing resumes after idle");
        work.Suspend(); work.Key(11500, "day1", 2000); Check(work.TodayMs == 1600, "Excluded activity breaks intervals");
        work.Paused = true; work.Key(12000, "day1", 2000); Check(work.TodayMs == 1600, "Paused work clock ignores keys");
        work.Paused = false; work.Key(12500, "day1", 2000); work.Key(13500, "day1", 2000); Check(work.TodayMs == 2600, "Manual resume starts fresh interval");
        work.ResetSession(); Check(work.SessionMs == 0 && work.TodayMs == 2600, "Session reset preserves today");
        work.RollDay("day2"); Check(work.TodayMs == 0 && work.Day == "day2", "Daily rollover");
        var focus = new FocusClock(); focus.Start(0, 1, 1, false); Check(focus.Tick(59000) == null && focus.RemainingMs == 1000, "Focus countdown");
        Check(focus.Tick(60000) != null && focus.Phase == "휴식", "Focus enters break once");
        focus.TogglePause(61000); long left = focus.RemainingMs; focus.Tick(100000); Check(focus.RemainingMs == left, "Paused focus timer does not advance");
        focus.TogglePause(120000); Check(focus.Tick(120000 + left) != null && !focus.Running, "Single cycle completes after break");
        focus.Start(0, 1, 1, true); focus.Tick(60000); focus.Tick(120000); Check(focus.Phase == "집중", "Focus repeat starts next cycle");
        focus.Suspend(125000); Check(focus.Paused && focus.RemainingMs == 55000, "Lock pauses focus timer");
        focus.Start(0, 1, 1, true); focus.Tick(60000); focus.SkipBreak(65000); Check(focus.Phase == "집중" && focus.RemainingMs == 60000, "Stop rest skips repeating focus break");
        focus.Start(0, 1, 1, false); focus.Tick(60000); focus.SkipBreak(65000); Check(!focus.Running, "Stop rest ends single-cycle break");
        var desktop = new DesktopOptions();
        desktop.AppRules.Add(new AppRule { Process = "chrome.exe", Hide = true });
        desktop.AppRules.Add(new AppRule { Process = "CHROME", Mute = true, ExcludeWork = true });
        var matchPolicy = desktop.Policy("Chrome"); Check(matchPolicy.Hide && matchPolicy.Mute && matchPolicy.ExcludeWork, "App rules match and merge case-insensitively");
        Check(!desktop.Policy("Code").Hide, "Unmatched app remains unaffected");
        Check(GlobalHotkeys.Parse("Ctrl+Alt+Q") == (3u, 81u), "Shortcut parsing");
        bool badShortcut = false; try { GlobalHotkeys.Parse("Q"); } catch (InvalidOperationException) { badShortcut = true; } Check(badShortcut, "Bare key rejected as global shortcut");
        Check(DesktopOptions.ValidColor("#875477") && !DesktopOptions.ValidColor("#GGGGGG"), "Bubble color validation");
        var sharedRepo = new ChatRepository(Path.Combine(testDir, "shared-chat")); sharedRepo.Apply(ChatRepository.Clone(mockSettings));
        var handler = new FakeHandler(); using var sharedHttp = new HttpClient(handler); var session = new PetConversation(sharedRepo, new AiClient(sharedHttp));
        var quickTurn = session.Send("hello", true, true, CancellationToken.None).GetAwaiter().GetResult();
        Check(sharedRepo.Turns.Count == 1 && quickTurn.Turn.Assistant == "mock reply", "Quick conversation commits to shared history");
        using (var body = JsonDocument.Parse(handler.LastBody)) Check(body.RootElement.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32() == sharedRepo.Preferences.MaxOutputTokens, "Quick reply keeps configured budget instead of truncating at 350 tokens");
        session.Send("continue", false, false, CancellationToken.None).GetAwaiter().GetResult();
        using (var body = JsonDocument.Parse(handler.LastBody)) Check(body.RootElement.GetProperty("contents").GetArrayLength() == 3, "Full chat continues quick chat context");
        using var slowHttp = new HttpClient(new FakeHandler(delay: 100)); var slowSession = new PetConversation(sharedRepo, new AiClient(slowHttp));
        var firstSend = slowSession.Send("first", true, false, CancellationToken.None); bool duplicate = false;
        try { slowSession.Send("second", true, false, CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidOperationException) { duplicate = true; }
        firstSend.GetAwaiter().GetResult(); Check(duplicate && !slowSession.Busy, "Concurrent chat sends rejected and busy state cleared");
        int storedTurns = sharedRepo.Turns.Count; using var stopSend = new CancellationTokenSource(10); bool stopWorked = false;
        try { slowSession.Send("cancel", true, false, stopSend.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { stopWorked = true; }
        Check(stopWorked && sharedRepo.Turns.Count == storedTurns && !slowSession.Busy, "Cancelled quick send stores no turn");
        count += FeatureTests.Run(testDir, png, mockSettings);
        count += GameTests.Run(testDir);
        count += CustomGearTests.Run(testDir, png);
        count += BounceTests.Run();
        count += ReactionFeatureTests.Run();
        count += ImageReplacementTests.Run(testDir);
        count += HotkeyConflictTests.Run();
        count += PresetDialogueTests.Run(testDir);
        count += PetPolishTests.Run(testDir);
        count += ConvenienceTests.Run(testDir);
        count += SoundPresetTests.Run(testDir);
        count += ReplyRecoveryTests.Run(testDir);
        count += AutomaticDialogueTests.Run(testDir);
        count += DesktopCompanionTests.Run(testDir);
        count += ExpandedFeatureTests.Run(testDir);
        count += BackupTests.Run(testDir);
        count += FlipTests.Run(testDir);
        count += DietTests.Run(testDir);
        count += BubbleSpotTests.Run();
        count += ThemeTests.Run(testDir);
        count += TalkTests.Run(testDir);
        count += PresetPackageTests.Run(testDir);
        count += AwarenessTests.Run();
        count += DailyTests.Run(testDir);
        count += TrackerTests.Run();
        count += SecurityTests.Run(testDir);
        count += BondTests.Run(testDir);
        count += ProgressTests.Run(testDir);
        count += StageTests.Run(testDir);
        count += ModTests.Run(testDir);
        count += UpdateTests.Run(testDir);
        count += GiftTests.Run(testDir);
        count += AiParameterTests.Run();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), $"PASS: {count} checks\n");
    }
    sealed class FakeHandler : HttpMessageHandler
    {
        readonly HttpStatusCode status;
        readonly int delay;
        public string LastBody { get; private set; } = "";
        public FakeHandler(HttpStatusCode status = HttpStatusCode.OK, int delay = 0) { this.status = status; this.delay = delay; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken); if (delay > 0) await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"mock reply\"}]}}]}") };
        }
    }
}
