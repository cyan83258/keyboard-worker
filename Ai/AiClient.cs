using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

public sealed class AiClient
{
    static readonly HttpClient Shared = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    readonly HttpClient http;
    readonly bool trackUsage;
    public AiClient(HttpClient? client = null) { http = client ?? Shared; trackUsage = client == null; }
    public static HttpRequestMessage Build(AiConnection c, string key, string system, ChatTurn[] history, string text, int maxTokens, bool streaming = false)
    {
        if (string.IsNullOrWhiteSpace(c.Model)) throw new InvalidOperationException("AI 연결에서 모델 ID를 입력해 주세요.");
        string address = c.Endpoint.Trim().Replace("{model}", Uri.EscapeDataString(c.Model.Trim()));
        if (streaming && c.Format == "Gemini")
        {
            address = address.Replace(":generateContent", ":streamGenerateContent");
            if (!address.Contains("alt=sse", StringComparison.OrdinalIgnoreCase)) address += (address.Contains('?') ? "&" : "?") + "alt=sse";
        }
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new InvalidOperationException("전체 HTTPS 엔드포인트를 입력해 주세요. 로컬 서버는 HTTP도 가능합니다.");
        object payload;
        var messages = new List<object>();
        if (c.Format == "Gemini")
        {
            foreach (var t in history) { messages.Add(new { role = "user", parts = new[] { new { text = t.User } } }); messages.Add(new { role = "model", parts = new[] { new { text = t.Assistant } } }); }
            messages.Add(new { role = "user", parts = new[] { new { text } } });
            var generation = new Dictionary<string, object> { ["maxOutputTokens"] = maxTokens };
            if (c.Model.Trim() is "gemini-2.5-flash" or "gemini-2.5-flash-lite") generation["thinkingConfig"] = new { thinkingBudget = 0 };
            payload = new { systemInstruction = new { parts = new[] { new { text = system } } }, contents = messages, generationConfig = generation };
        }
        else
        {
            if (c.Format == "Chat Completions") messages.Add(new { role = "system", content = system });
            foreach (var t in history) { messages.Add(new { role = "user", content = t.User }); messages.Add(new { role = "assistant", content = t.Assistant }); }
            messages.Add(new { role = "user", content = text });
            payload = c.Format == "Responses" ? (object)new { model = c.Model.Trim(), instructions = system, input = messages, max_output_tokens = maxTokens, store = false, stream = streaming } : new { model = c.Model.Trim(), messages, max_tokens = maxTokens, stream = streaming };
        }
        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        if (streaming) request.Headers.Accept.Add(new("text/event-stream"));
        if (key.Length > 0 && c.Header.Length > 0)
        {
            if (!request.Headers.TryAddWithoutValidation(c.Header.Trim(), (c.Bearer ? "Bearer " : "") + key)) { request.Dispose(); throw new InvalidOperationException("인증 헤더 이름을 확인해 주세요."); }
        }
        return request;
    }
    public static string Parse(string format, string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        var chunks = new List<string>();
        if (format == "Gemini")
        {
            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0 && candidates[0].TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                foreach (var part in parts.EnumerateArray()) if (!(part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) && part.TryGetProperty("text", out var value)) chunks.Add(value.GetString() ?? "");
        }
        else if (format == "Responses")
        {
            if (root.TryGetProperty("output", out var output)) foreach (var item in output.EnumerateArray())
                if (item.TryGetProperty("content", out var content)) foreach (var part in content.EnumerateArray())
                    if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text" && part.TryGetProperty("text", out var value)) chunks.Add(value.GetString() ?? "");
        }
        else if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 && choices[0].TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var value))
        {
            if (value.ValueKind == JsonValueKind.String) chunks.Add(value.GetString() ?? "");
            else if (value.ValueKind == JsonValueKind.Array) foreach (var part in value.EnumerateArray()) if (part.TryGetProperty("text", out var v)) chunks.Add(v.GetString() ?? "");
        }
        string result = string.Join("\n", chunks);
        ReplyCompletion.Check(root, format, result);
        if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("AI가 텍스트 답변을 반환하지 않았습니다. 출력 한도, 모델 또는 API 응답 형식을 확인해 주세요.");
        if (result.Length > ReplyCompletion.MaxCharacters) throw new InvalidOperationException("답변이 앱의 최대 길이를 초과했습니다. 출력 한도를 줄여 주세요.");
        return result;
    }
    public async Task<string> Send(ChatPreferences settings, ChatTurn[] history, string text, CancellationToken cancellation, Action<string>? progress = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        string accumulated = "", lastReported = ""; int tokens = settings.MaxOutputTokens;
        void Report(string value) { if (progress == null || value == lastReported) return; lastReported = value; progress(value); }
        for (int attempt = 0; ; attempt++)
        {
            var context = accumulated.Length == 0 ? history : history.Append(new ChatTurn(text, accumulated, DateTimeOffset.Now)).ToArray();
            string message = accumulated.Length == 0 ? text : "방금 답변이 출력 한도에서 끊겼습니다. 이미 보낸 내용을 반복하거나 새 인사를 하지 말고, 끊긴 마지막 문장 바로 뒤부터 이어서 답변을 끝까지 완성하세요.";
            try
            {
                string next = await SendOnce(settings, context, message, tokens, deadline.Token, progress == null ? null : partial => Report(ReplyCompletion.Join(accumulated, partial))).ConfigureAwait(false);
                string complete = ReplyCompletion.Join(accumulated, next).Trim();
                if (complete.Length > ReplyCompletion.MaxCharacters) throw new InvalidOperationException("답변이 앱의 최대 길이를 초과했습니다. 출력 한도를 줄여 주세요.");
                Report(complete); return complete;
            }
            catch (ReplyLimitException limited)
            {
                accumulated = ReplyCompletion.Join(accumulated, limited.Partial);
                Report(accumulated);
                if (accumulated.Length > ReplyCompletion.MaxCharacters) throw new InvalidOperationException("답변이 앱의 최대 길이를 초과했습니다.");
                if (!settings.AutoContinue || attempt >= 2) throw new ReplyLimitException(accumulated);
                // Empty output can mean the reasoning budget consumed the whole limit.
                if (limited.Partial.Length == 0) tokens = Math.Min(16000, tokens * 2);
                cancellation.ThrowIfCancellationRequested();
                if (deadline.IsCancellationRequested) throw new InvalidOperationException($"답변 대기 시간 {settings.RequestTimeoutSeconds}초를 넘었습니다. 대기 시간을 늘릴 수 있어요.");
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new InvalidOperationException($"답변 대기 시간 {settings.RequestTimeoutSeconds}초를 넘었습니다. 캐릭터 프로필 → 기억과 대화에서 대기 시간을 늘릴 수 있어요.");
            }
        }
    }
    async Task<string> SendOnce(ChatPreferences settings, ChatTurn[] history, string text, int tokens, CancellationToken cancellation, Action<string>? progress)
    {
        var c = settings.Current;
        bool streaming = settings.Streaming && progress != null;
        using var request = Build(c, SecretVault.Unprotect(c.Secret), CharacterPrompt.Build(settings.Profile), history, text, tokens, streaming);
        using var usage = trackUsage ? AiUsage.Current.Begin(settings.Budget, settings.UsageKind, tokens) : null;
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string reason = (int)response.StatusCode switch { 401 or 403 => "API 키 또는 모델 이용 권한을 확인해 주세요.", 429 => "요청 한도 또는 잔액을 확인한 뒤 다시 시도해 주세요.", 400 or 404 => "모델 ID, 엔드포인트와 API 형식을 확인해 주세요.", _ => "서버에 연결하지 못했습니다. 잠시 후 다시 시도해 주세요." };
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode} · {reason}");
        }
        using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        if (streaming && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            string live = await SseReplies.Read(stream, c.Format, progress!, cancellation, usage == null ? null : usage.Capture).ConfigureAwait(false); usage?.Succeeded(); return live;
        }
        using var bytes = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        { if (bytes.Length + count > 1024 * 1024) throw new InvalidOperationException("응답이 너무 큽니다. 출력 한도를 줄여 주세요."); bytes.Write(buffer, 0, count); }
        string json = Encoding.UTF8.GetString(bytes.ToArray()); using (var metadata = JsonDocument.Parse(json)) usage?.Capture(metadata.RootElement);
        string result = Parse(c.Format, json); usage?.Succeeded(); if (streaming) progress!(result); return result;
    }
}
