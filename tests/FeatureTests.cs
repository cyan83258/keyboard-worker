using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace KeyboardWorker;

public static class FeatureTests
{
    public static int Run(string root, string png, ChatPreferences preferences)
    {
        int checks = 0;
        void Check(bool result, string name) { if (!result) throw new Exception(name); checks++; }
        string ReadSse(string format, string body, ConcurrentQueue<string>? progress = null)
        {
            using var stream = new FragmentStream(Encoding.UTF8.GetBytes(body));
            return SseReplies.Read(stream, format, text => progress?.Enqueue(text), CancellationToken.None).GetAwaiter().GetResult();
        }
        const string chat = ": keep-alive\r\n\r\ndata: {\"choices\":[{\"delta\":{\"content\":\"안녕\"},\"finish_reason\":null}]}\r\n\r\ndata: {\"choices\":[{\"delta\":{\"content\":\" 친구\"},\"finish_reason\":\"stop\"}]}\r\n\r\ndata: [DONE]\r\n\r\n";
        var chunks = new ConcurrentQueue<string>();
        Check(ReadSse("Chat Completions", chat, chunks) == "안녕 친구", "Fragmented UTF8 chat SSE");
        Check(chunks.Count == 2 && chunks.First() == "안녕", "Incremental text before completion");
        const string responses = "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"hello\"}\n\nevent: response.completed\ndata: {\"type\":\"response.completed\"}\n\n";
        Check(ReadSse("Responses", responses) == "hello", "Responses events");
        const string gemini = "data: {\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"hidden\"},{\"text\":\"hello\"}]}}]}\n\ndata: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\" friend\"}]},\"finishReason\":\"STOP\"}]}\n\n";
        Check(ReadSse("Gemini", gemini) == "hello friend", "Gemini stream excludes thoughts");
        Check(ReadSse("Chat Completions", "data: {\"choices\":\n" + "data: [{\"delta\":{\"content\":\"multi\"},\"finish_reason\":\"stop\"}]}\n\n") == "multi", "Multi-line SSE data");
        bool incomplete = false;
        try { ReadSse("Chat Completions", "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n"); } catch (InvalidOperationException) { incomplete = true; } Check(incomplete, "Unexpected stream EOF rejected");
        bool failure = false; try { ReadSse("Responses", "data: {\"type\":\"response.failed\"}\n\n"); } catch (InvalidOperationException) { failure = true; } Check(failure, "Failure event rejected");
        bool chatFailure = false;
        try { ReadSse("Chat Completions", "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"},\"finish_reason\":\"error\"}]}\n\n"); } catch (InvalidOperationException) { chatFailure = true; } Check(chatFailure, "Chat stream terminal error rejected");
        Check(PetMood.DisplayText("안녕\n[pe") == "안녕" && PetMood.DisplayText("안녕\n[pet-mood:happy]") == "안녕", "Split mood tags stay hidden");
        Check(PetMood.DisplayText("목록 [오늘]") == "목록 [오늘]", "Normal brackets stay visible");
        foreach (var connection in AiConnection.Defaults())
        {
            connection.Model = "test"; using var request = AiClient.Build(connection, "test-key", "system", Array.Empty<ChatTurn>(), "hello", 600, true);
            if (connection.Format == "Gemini") Check(request.RequestUri!.ToString().Contains(":streamGenerateContent") && request.RequestUri.Query.Contains("alt=sse"), "Gemini streaming route");
            else { using var json = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()); Check(json.RootElement.GetProperty("stream").GetBoolean(), connection.Name + " stream flag"); }
        }
        var streamSettings = preferences.Snapshot(); streamSettings.Connections[0].Format = "Chat Completions"; streamSettings.Connections[0].Endpoint = "https://example.test/chat";
        using var http = new HttpClient(new SseHandler(chat));
        var repository = new ChatRepository(Path.Combine(root, "sse-history"));
        repository.Apply(streamSettings);
        var session = new PetConversation(repository, new AiClient(http));
        var live = new ConcurrentQueue<string>(); var result = session.Send("hello", false, false, CancellationToken.None, live.Enqueue).GetAwaiter().GetResult();
        Check(live.Count == 2 && repository.Turns.Count == 1 && result.Turn.Assistant == "안녕 친구", "Streaming service commits once");
        Check(result.Turn.CharacterName == streamSettings.Profile.Name, "Character name preserved in completed turn");
        using var brokenHttp = new HttpClient(new SseHandler("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n")); bool disconnected = false;
        try { new PetConversation(repository, new AiClient(brokenHttp)).Send("broken", false, false, CancellationToken.None, _ => { }).GetAwaiter().GetResult(); } catch (InvalidOperationException) { disconnected = true; }
        Check(disconnected && repository.Turns.Count == 1, "Disconnected reply never stored");
        using var slowHttp = new HttpClient(new SseHandler(chat, 30)); using var cancel = new CancellationTokenSource(10); bool cancelled = false;
        try { new PetConversation(repository, new AiClient(slowHttp)).Send("cancel", false, false, cancel.Token, _ => { }).GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && repository.Turns.Count == 1, "Streaming read cancellation never stored");
        var original = new Preferences();
        original.ImagePaths["idle"] = png;
        var store = new PresetStore(Path.Combine(root, "presets"));
        var saved = store.Add("친구 A", original, streamSettings.Profile);
        original.ImagePaths.Clear(); streamSettings.Profile.Name = "changed";
        Check(saved.Images.Count == 1 && saved.Profile.Name != "changed", "Preset is isolated snapshot");
        original.Desktop.WorkGapMs = 4000; var prepared = saved.Prepare(original, streamSettings);
        Check(prepared.Settings.Desktop.WorkGapMs == 4000 && prepared.Chat.Current.Secret == streamSettings.Current.Secret, "Preset switch preserves timers and credentials");
        Check(prepared.Chat.Profile.Name == saved.Profile.Name && prepared.Settings.ActivePresetId == saved.Id && original.ImagePaths.Count == 0, "Preset switch uses copied appearance and persona");
        OrderedStore.FlushAsync().GetAwaiter().GetResult();
        var loaded = new PresetStore(Path.Combine(root, "presets"));
        Check(loaded.Items.Count == 1 && loaded.Items[0].Images["idle"] == png, "Preset reload and image links");
        string presetJson = File.ReadAllText(Path.Combine(root, "presets", "character-presets.json"));
        Check(!presetJson.Contains("Secret") && !presetJson.Contains("Connections") && !presetJson.Contains("test-only-key"), "Preset excludes connections and keys");
        store.Update(saved, "친구 B", original, streamSettings.Profile);
        OrderedStore.FlushAsync().GetAwaiter().GetResult();
        Check(new PresetStore(Path.Combine(root, "presets")).Items[0].Name == "친구 B", "Preset update persists");
        store.Delete(store.Items[0]); OrderedStore.FlushAsync().GetAwaiter().GetResult(); Check(new PresetStore(Path.Combine(root, "presets")).Items.Count == 0, "Preset deletion persists");
        string orderedPath = Path.Combine(root, "ordered.json");
        for (int n = 0; n < 40; n++) OrderedStore.SaveJson(orderedPath, new { Number = n });
        OrderedStore.FlushAsync().GetAwaiter().GetResult();
        using (var json = JsonDocument.Parse(File.ReadAllText(orderedPath))) Check(json.RootElement.GetProperty("Number").GetInt32() == 39, "Background writes retain submission order");
        string blockingFile = Path.Combine(root, "blocked-parent"); File.WriteAllText(blockingFile, "file"); bool rejected = false;
        try { OrderedStore.SaveJsonAsync(Path.Combine(blockingFile, "test.json"), new { Value = 1 }).GetAwaiter().GetResult(); } catch (IOException) { rejected = true; } Check(rejected, "Background write failure observable");
        OrderedStore.SaveJsonAsync(orderedPath, new { Number = 42 }).GetAwaiter().GetResult(); Check(File.ReadAllText(orderedPath).Contains("42"), "Writer continues after failure");
        string recoverPath = Path.Combine(blockingFile, "test.json");
        File.Delete(blockingFile);
        Directory.CreateDirectory(blockingFile);
        OrderedStore.SaveJsonAsync(recoverPath, new { Value = 2 }).GetAwaiter().GetResult();
        Check(OrderedStore.Errors.Length == 0, "Write error clears after successful retry");
        var dispatcher = Dispatcher.CurrentDispatcher; using var cache = new LazyImageCache(dispatcher, () => { }); var images = new Preferences { AdvancedImages = true };
        foreach (var key in new[] { "idle", "typing-left", "typing-right", "happy" }) images.ImagePaths[key] = png;
        cache.Configure(images, 128); PumpUntil(() => cache.CachedCount == 1); Check(cache.DecodeRequests == 1, "Same file shared by basic states decoded once");
        cache.Configure(images.Snapshot(), 128); Check(cache.DecodeRequests == 1 && cache.Get("happy") != null, "Unchanged assets reuse cache");
        string lazy = Path.Combine(root, "lazy.png");
        File.Copy(png, lazy);
        images.ImagePaths["sad"] = lazy;
        cache.Configure(images, 128);
        Check(cache.DecodeRequests == 1, "Unused advanced state not decoded");
        cache.Get("sad");
        PumpUntil(() => cache.CachedCount == 2);
        Check(cache.DecodeRequests == 2, "Advanced state decoded only on request");
        return checks;
    }
    static void PumpUntil(Func<bool> ready)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) }; long end = Environment.TickCount64 + 5000;
        timer.Tick += (_, _) => { if (ready() || Environment.TickCount64 > end) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        if (!ready()) throw new Exception("Image cache load timeout");
    }
    sealed class SseHandler : HttpMessageHandler
    {
        readonly string data; readonly int delay;
        public SseHandler(string data, int delay = 0) { this.data = data; this.delay = delay; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var content = new StreamContent(new FragmentStream(Encoding.UTF8.GetBytes(data), delay));
            content.Headers.ContentType = new("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
    sealed class FragmentStream : MemoryStream
    {
        readonly int delay;
        public FragmentStream(byte[] data, int delay = 0) : base(data) { this.delay = delay; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (delay > 0) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return base.Read(buffer.Span[..Math.Min(3, buffer.Length)]);
        }
    }
}
