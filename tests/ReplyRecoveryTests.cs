using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

public static class ReplyRecoveryTests
{
    sealed class Replies : HttpMessageHandler
    {
        readonly Queue<(string Body, string Type)> replies;
        public readonly List<string> Requests = new();
        public Replies(params (string Body, string Type)[] items) { replies = new(items); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); Requests.Add(await request.Content!.ReadAsStringAsync(cancellation));
            var item = replies.Dequeue(); return new(HttpStatusCode.OK) { Content = new StringContent(item.Body, Encoding.UTF8, item.Type) };
        }
    }
    static ChatPreferences Settings(string format) => new() { MaxOutputTokens = 2048, Streaming = true, Connections = new() { new() { Format = format, Model = "test", Endpoint = "https://example.com/api", Secret = "" } } };
    static string Chat(string text, string finish) => JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = text }, finish_reason = finish } } });
    static string Gemini(string text, string finish) => JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text } } }, finishReason = finish } } });
    static string Response(string text, string status) => JsonSerializer.Serialize(new { status, incomplete_details = new { reason = "max_output_tokens" }, output = new[] { new { content = new[] { new { type = "output_text", text } } } } });
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        using var chatHandler = new Replies((Chat("오늘은 ", "length"), "application/json"), (Chat("작업하자.", "stop"), "application/json")); using var chatHttp = new HttpClient(chatHandler);
        var repository = new ChatRepository(Path.Combine(root, "recovered-replies")); repository.Apply(Settings("Chat Completions"));
        var conversation = new PetConversation(repository, new AiClient(chatHttp)); var progress = new List<string>();
        var turn = conversation.Send("함께 작업", true, false, CancellationToken.None, progress.Add).GetAwaiter().GetResult();
        Check(turn.Turn.Assistant == "오늘은 작업하자." && repository.Turns.Count == 1 && chatHandler.Requests.Count == 2, "Truncated response continued and committed as one complete conversation");
        using (var body = JsonDocument.Parse(chatHandler.Requests[1])) Check(body.RootElement.GetProperty("messages")[2].GetProperty("content").GetString() == "오늘은 " && progress[^1] == "오늘은 작업하자.", "Continuation includes partial reply and reports accumulated answer");
        using var geminiHandler = new Replies(("data: " + Gemini("토큰 한도 ", "MAX_TOKENS") + "\n\n", "text/event-stream"), ("data: " + Gemini("이어받음", "STOP") + "\n\n", "text/event-stream"));
        using var geminiHttp = new HttpClient(geminiHandler);
        Check(new AiClient(geminiHttp).Send(Settings("Gemini"), Array.Empty<ChatTurn>(), "test", CancellationToken.None, _ => { }).GetAwaiter().GetResult() == "토큰 한도 이어받음", "Gemini MAX_TOKENS stream triggers continuation without losing final delta");
        string incomplete = "data: " + JsonSerializer.Serialize(new { type = "response.incomplete", response = JsonSerializer.Deserialize<JsonElement>(Response("Hello ", "incomplete")) }) + "\n\n";
        string completed = "data: " + JsonSerializer.Serialize(new { type = "response.completed", response = JsonSerializer.Deserialize<JsonElement>(Response("world", "completed")) }) + "\n\n";
        using var responsesHandler = new Replies((incomplete, "text/event-stream"), (completed, "text/event-stream")); using var responsesHttp = new HttpClient(responsesHandler);
        Check(new AiClient(responsesHttp).Send(Settings("Responses"), Array.Empty<ChatTurn>(), "test", CancellationToken.None, _ => { }).GetAwaiter().GetResult() == "Hello world", "Responses incomplete event recovers final embedded text and continues");
        using var emptyHandler = new Replies((Gemini("", "MAX_TOKENS"), "application/json"), (Gemini("사고 후 답변", "STOP"), "application/json")); using var emptyHttp = new HttpClient(emptyHandler);
        Check(new AiClient(emptyHttp).Send(Settings("Gemini"), Array.Empty<ChatTurn>(), "test", CancellationToken.None).GetAwaiter().GetResult() == "사고 후 답변", "Reasoning-only output can retry with larger budget");
        using (var body = JsonDocument.Parse(emptyHandler.Requests[1])) Check(body.RootElement.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32() == 4096, "Reasoning-only retry increases budget within bounded limit");
        using var disabledHandler = new Replies((Chat("미완성", "length"), "application/json"));
        using var disabledHttp = new HttpClient(disabledHandler);
        var disabled = Settings("Chat Completions");
        disabled.AutoContinue = false;
        bool stopped = false;
        try { new AiClient(disabledHttp).Send(disabled, Array.Empty<ChatTurn>(), "test", CancellationToken.None).GetAwaiter().GetResult(); } catch (ReplyLimitException e) { stopped = e.Partial == "미완성"; }
        Check(stopped && disabledHandler.Requests.Count == 1, "Auto continuation can be disabled and reports unfinished reply");
        using var exhaustedHandler = new Replies((Chat("한", "length"), "application/json"), (Chat("두", "length"), "application/json"), (Chat("세", "length"), "application/json"));
        using var exhaustedHttp = new HttpClient(exhaustedHandler);
        bool bounded = false;
        try { new PetConversation(repository, new AiClient(exhaustedHttp)).Send("test", false, false, CancellationToken.None).GetAwaiter().GetResult(); } catch (ReplyLimitException e) { bounded = e.Partial == "한두세"; }
        Check(bounded && exhaustedHandler.Requests.Count == 3 && repository.Turns.Count == 1, "Automatic continuation is bounded and unfinished answer is never committed");
        using var interruptedHandler = new Replies(("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n", "text/event-stream"));
        using var interruptedHttp = new HttpClient(interruptedHandler);
        bool disconnected = false;
        try { new AiClient(interruptedHttp).Send(Settings("Chat Completions"), Array.Empty<ChatTurn>(), "test", CancellationToken.None, _ => { }).GetAwaiter().GetResult(); } catch (InvalidOperationException) { disconnected = true; }
        Check(disconnected && interruptedHandler.Requests.Count == 1, "Disconnected stream is not retried as a token limit");
        using var filteredHandler = new Replies((Chat("partial", "content_filter"), "application/json")); using var filteredHttp = new HttpClient(filteredHandler);
        bool filtered = false;
        try { new AiClient(filteredHttp).Send(Settings("Chat Completions"), Array.Empty<ChatTurn>(), "test", CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidOperationException) { filtered = true; }
        Check(filtered && filteredHandler.Requests.Count == 1, "Provider stop other than token limit is not automatically continued");
        string longText = new('가', 12000); Check(AiClient.Parse("Chat Completions", Chat(longText, "stop")) == longText, "Successful long reply is not silently sliced at ten thousand characters");
        repository.Add(new ChatTurn("long", longText, DateTimeOffset.Now)); OrderedStore.FlushAsync().GetAwaiter().GetResult();
        Check(new ChatRepository(Path.Combine(root, "recovered-replies")).Turns[^1].Assistant.Length == 12000, "Full long reply survives history reload");
        var old = new ChatPreferences { MaxOutputTokens = 600 };
        old.Normalize();
        Check(old.MaxOutputTokens == 2048 && old.RequestTimeoutSeconds == 180, "Previous default token budget upgraded without quick reply cap");
        var custom = new ChatPreferences { MaxOutputTokens = 700 }; custom.Normalize(); Check(custom.MaxOutputTokens == 700, "Custom token budget remains user-controlled");
        Check(ReplyCompletion.Join("abcdefghijklmnopqrstuv", "mnopqrstuv more") == "abcdefghijklmnopqrstuvmnopqrstuv more", "Short coincidence is not removed as overlap");
        Check(ReplyCompletion.Join("abcdefghijklmnopqrstuv", "klmnopqrstuv more") == "abcdefghijklmnopqrstuv more", "Continuation deduplicates meaningful repeated suffix");
        using var canceledHandler = new Replies((Chat("not sent", "stop"), "application/json"));
        using var canceledHttp = new HttpClient(canceledHandler);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        bool userStopped = false;
        try { new AiClient(canceledHttp).Send(Settings("Chat Completions"), Array.Empty<ChatTurn>(), "test", cancel.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { userStopped = true; }
        Check(userStopped && canceledHandler.Requests.Count == 0, "User cancellation is honored without automatic extra request");
        return count;
    }
}
