using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KeyboardWorker;

public static class ExpandedFeatureTests
{
    sealed class Replies : HttpMessageHandler
    {
        public int Calls; public string Request = "";
        readonly Queue<string> replies;
        public Replies(params string[] text) { replies = new(text); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++; Request = await request.Content!.ReadAsStringAsync(token); string text = replies.Dequeue();
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text } } }, finishReason = "STOP" } }, usageMetadata = new { totalTokenCount = 200 } }), Encoding.UTF8, "application/json") };
        }
    }
    public static int Run(string root)
    {
        int count = 0; void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        string folder = Path.Combine(root, "expanded-features");
        Directory.CreateDirectory(folder);
        var book = new AdventureBook(folder);
        var game = new PetGame(Path.Combine(folder, "game"));
        game.State.Coins = 100;
        var a = book.Friend("a", "A"); a.Favorite = "작은 책"; var b = book.Friend("b", "B"); b.Favorite = "음악 편지";
        book.Gift("a", "A", "작은 책", game); book.Gift("b", "B", "꽃다발", game);
        Check(a.Bond == 12 && b.Bond == 5 && a.Discovered && !b.Discovered && game.State.Coins == 80, "Character-specific gift preferences, independent affection and coin spending");
        Check(book.Pat("a", "A", 1000) && book.Pat("b", "B", 1000) && !book.Pat("a", "A", 2000), "Petting cooldown is independent for each character");
        var quest = new StoryQuest { CharacterId = "a", Title = "별빛 다리", Premise = "작은 모험을 시작한다", Minutes = 5 }; book.AddQuest(quest);
        book.Work("b", "B", 10000); Check(quest.ProgressMs == 0, "Only the quest's character receives its work progress");
        for (int i = 0; i < 30; i++) book.Work("a", "A", 10000);
        Check(quest.Ready && a.Bond == 18 && b.Bond == 6, "Confirmed work minutes progress quest and individual affection");
        book.AddStory(quest, "별빛을 건너", "친구는 작은 다리를 건너 새로운 이야기를 발견했다.", game, 99); string storyId = quest.StoryId;
        book.AddStory(quest, "중복", "중복 이야기", game, 0);
        Check(book.State.Stories.Count == 1 && book.State.Materials.Count == 0, "Ordinary story is text-first and completing same quest cannot duplicate loot");
        book.Favorite(storyId); book.SetOptions(new() { KeepStories = 1 });
        var next = new StoryQuest { CharacterId = "a", Title = "두번째", ProgressMs = 300000, Minutes = 5 }; book.AddQuest(next); bool full = false;
        try { book.AddStory(next, "새 이야기", "본문", game, 0); } catch (InvalidOperationException) { full = true; }
        Check(full && book.State.Materials.Count == 0 && book.State.Stories[0].Favorite, "Favorite story protection blocks overflow before awarding rare items");
        book.SetOptions(new() { KeepStories = 10 }); book.AddStory(next, "새 이야기", "본문", game, 0);
        Check(book.State.Materials["반짝이는 별가루"] == 1, "Rare material is awarded only on designated low probability roll");
        var decoration = new StoryQuest { CharacterId = "a", Title = "장식", ProgressMs = 300000, Minutes = 5 }; book.AddQuest(decoration); book.AddStory(decoration, "빛나는 리본", "본문", game, 5);
        Check(book.State.Stories[^1].Loot.StartsWith("희귀 장식") && game.State.Owned.Count == 1, "Rare decoration grants one existing wardrobe item");
        book.TalkPair("a", "b");
        book.WorkPair("a", "b", 300000);
        Check(book.Pair("a", "b") == book.Pair("b", "a") && book.Pair("a", "b").Bond == 3, "Pair relationship is symmetric and grows with conversations and shared work");
        book.DeleteStory(storyId); book.Save(true); OrderedStore.FlushAsync().GetAwaiter().GetResult(); var loaded = new AdventureBook(folder);
        Check(loaded.State.Friends["a"].Bond == a.Bond && loaded.State.Stories.All(s => s.Id != storyId) && loaded.State.Materials.Count == 1, "Friendship, story deletion and collection survive reload independently");
        string day = "2026-10-10"; var meter = new AiUsage(Path.Combine(folder, "usage"), () => day); var budget = new AiBudget { Calls = 3, AutomaticCalls = 1, Tokens = 1000 };
        using (var ticket = meter.Begin(budget, "AutoQuest", 200)) { using var json = JsonDocument.Parse("{\"usageMetadata\":{\"totalTokenCount\":250}}"); ticket.Capture(json.RootElement); ticket.Succeeded(); }
        Check(meter.Snapshot().Tokens == 250 && meter.Snapshot().AutomaticCalls == 1 && meter.Snapshot().Estimated == 0, "Daily usage captures actual provider total tokens");
        bool autoBlocked = false; try { meter.Begin(budget, "AutoDuo", 100); } catch (InvalidOperationException) { autoBlocked = true; }
        Check(autoBlocked && meter.Snapshot().Calls == 1, "Automatic daily cap blocks request before counting or sending");
        using (var pending = meter.Begin(budget, "Chat", 600))
        {
            bool reserved = false;
            try { meter.Begin(budget, "Chat", 200); } catch (InvalidOperationException) { reserved = true; } Check(reserved, "Concurrent output reservations prevent exceeding daily token budget");
        }
        Check(meter.Snapshot().Failed == 1 && meter.Snapshot().Estimated == 1 && meter.Snapshot().Tokens == 850, "Unknown failed usage is conservatively estimated and marked");
        day = "2026-10-11"; Check(meter.Snapshot().Calls == 0 && meter.Snapshot().Tokens == 0, "Next local day starts a new AI budget");
        string interruptedPath = Path.Combine(folder, "interrupted-usage");
        var interrupted = new AiUsage(interruptedPath, () => day);
        var abandoned = interrupted.Begin(budget, "Chat", 200);
        OrderedStore.FlushAsync().GetAwaiter().GetResult();
        var recovered = new AiUsage(interruptedPath, () => day).Snapshot();
        Check(recovered.Tokens == 200 && recovered.Pending == 0 && recovered.Failed == 1, "Restart conservatively recovers unfinished reserved usage instead of resetting budget");
        abandoned.Dispose();
        var prefs = new ChatPreferences(); prefs.Automatic = prefs.Automatic with { Batch = true, BatchSize = 3 };
        using var batchHandler = new Replies("{\"lines\":[\"첫 한마디\",\"둘째 한마디\",\"셋째 한마디\"]}", "{\"lines\":[\"새 프로필 대사\",\"다른 대사\",\"새 대사\"]}");
        using var batchHttp = new HttpClient(batchHandler);
        var batch = new AutomaticBatch();
        var line1 = batch.Get(prefs, Array.Empty<ChatTurn>(), true, 60, "", CancellationToken.None, new AiClient(batchHttp)).GetAwaiter().GetResult();
        var line2 = batch.Get(prefs, Array.Empty<ChatTurn>(), true, 120, line1.Text, CancellationToken.None, new AiClient(batchHttp)).GetAwaiter().GetResult();
        Check(batchHandler.Calls == 1 && line1.Text == "첫 한마디" && line2.Text == "둘째 한마디", "Automatic batch produces and reuses distinct lines with one API request");
        prefs.Profile.Name = "새 친구";
        Check(batch.Get(prefs, Array.Empty<ChatTurn>(), true, 120, "", CancellationToken.None, new AiClient(batchHttp)).GetAwaiter().GetResult().Text == "새 프로필 대사" && batchHandler.Calls == 2, "Profile change invalidates prior character's cached lines");
        using var questHandler = new Replies("{\"title\":\"달빛 도서관\",\"premise\":\"책을 찾는 여행\",\"minutes\":5}", "{\"title\":\"책 속의 별\",\"body\":\"친구는 오래된 책에서 별빛을 발견했다.\"}");
        using var questHttp = new HttpClient(questHandler);
        var generated = AdventureAi.Quest(prefs, "a", new(), CancellationToken.None, new AiClient(questHttp)).GetAwaiter().GetResult();
        var narrative = AdventureAi.Story(prefs, generated, new(), CancellationToken.None, new AiClient(questHttp)).GetAwaiter().GetResult();
        Check(generated.CharacterId == "a" && generated.Minutes == 5 && narrative.Title == "책 속의 별" && questHandler.Calls == 2, "AI quest and completed story parse separate structured outputs");
        var repo = new ChatRepository(Path.Combine(folder, "target-chat"));
        repo.Apply(new ChatPreferences { Mode = "역할극" });
        repo.Add(new("A에게", "A대답", DateTimeOffset.Now, "A", "primary:a"));
        repo.Add(new("B에게", "B대답", DateTimeOffset.Now, "B", "secondary:b"));
        using var targetHandler = new Replies("B의 답변"); using var targetHttp = new HttpClient(targetHandler); var conversation = new PetConversation(repo, new AiClient(targetHttp));
        var answer = conversation.Send("안녕", false, false, CancellationToken.None, target: new Persona { Name = "B", Speech = "B만의 말투" }, targetKey: "secondary:b").GetAwaiter().GetResult();
        using (var json = JsonDocument.Parse(targetHandler.Request)) Check(answer.Turn.CharacterName == "B" && answer.Turn.TargetKey == "secondary:b" && json.RootElement.GetProperty("contents").GetArrayLength() == 3 && json.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString()!.Contains("역할극"), "Recipient and chat mode route the right persona and context");
        Check(repo.Context("primary:a").Length == 1 && repo.Context("secondary:b").Length == 2 && repo.Context("both").Length == 3, "Conversation histories remain separate per recipient and both-mode can see all");
        string imagePath = Path.Combine(folder, "shared.png");
        var source = BitmapSource.Create(20, 20, 96, 96, PixelFormats.Bgra32, null, new byte[1600], 80);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (var stream = File.Create(imagePath)) encoder.Save(stream);
        using var firstCache = new LazyImageCache(Dispatcher.CurrentDispatcher, () => { });
        using var secondCache = new LazyImageCache(Dispatcher.CurrentDispatcher, () => { });
        var images = new Preferences { ImagePaths = new() { ["idle"] = imagePath } };
        firstCache.Configure(images, 64);
        secondCache.Configure(images, 64);
        var frame = new DispatcherFrame();
        long until = Environment.TickCount64 + 3000;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (firstCache.Get("idle") != null && secondCache.Get("idle") != null || Environment.TickCount64 >= until) { timer.Stop(); frame.Continue = false; } };
        timer.Start();
        Dispatcher.PushFrame(frame);
        Check(firstCache.Get("idle")?.IsFrozen == true && ReferenceEquals(firstCache.Get("idle"), secondCache.Get("idle")) && firstCache.DecodeRequests + secondCache.DecodeRequests == 1, "Two pet caches share one frozen image and one decode");
        firstCache.Dispose();
        Check(secondCache.Get("idle") != null, "Disposing first pet cache does not remove second pet's sprite");
        Check(MusicMonitor.Browser("Chrome") && !MusicMonitor.Browser("Spotify") && ImageAssets.MusicKey("typing-left") == "music-left" && ImageAssets.MusicKey("happy") == "happy", "Music source filtering and dedicated sprite mapping preserve other expressions");
        var commands = new List<string>();
        using var music = new MusicMonitor(Dispatcher.CurrentDispatcher, () => { });
        music.TestCommands = command => { commands.Add(command); return Task.FromResult(true); };
        music.Control("previous").GetAwaiter().GetResult(); music.Control("toggle").GetAwaiter().GetResult(); music.Control("next").GetAwaiter().GetResult();
        Check(commands.SequenceEqual(new[] { "previous", "toggle", "next" }), "Player transport commands dispatch in requested order without live playback changes");
        return count;
    }
}
