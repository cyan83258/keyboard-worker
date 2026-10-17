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
    public static HttpRequestMessage Build(AiConnection c, string key, string system, ChatTurn[] history, string text, int maxTokens, bool streaming = false) => Build(c, key, Messages(system, history, text), maxTokens, streaming);
    // A system prompt, the exchanges so far and the new message, as the list of messages every request is made from.
    public static List<PromptMessage> Messages(string system, IEnumerable<ChatTurn> history, string text)
    {
        var list = new List<PromptMessage> { new(PromptMessage.System, system) };
        foreach (var t in history) { list.Add(new(PromptMessage.User, t.User)); list.Add(new(PromptMessage.Assistant, t.Assistant)); }
        list.Add(new(PromptMessage.User, text)); return list;
    }
    // Any list of messages, in the words of the connection's kind of API. The system messages at the head become its system
    // prompt; a later one is sent as a system message where the API knows them, else as the user's. thoughts: ask the model
    // to hand back what it thought (Gemini), so the novel window can show it.
    public static HttpRequestMessage Build(AiConnection c, string key, IReadOnlyList<PromptMessage> all, int maxTokens, bool streaming = false, bool thoughts = false)
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
        int lead = 0; while (lead < all.Count && all[lead].Role == PromptMessage.System) lead++;
        string system = string.Join("\n\n", all.Take(lead).Select(m => m.Text)); var rest = all.Skip(lead).ToList();
        object payload; var messages = new List<object>();
        if (c.Format == "Gemini")
        {
            // Gemini knows two roles and wants them taking turns: what follows in the same role is joined to what came before.
            var turns = new List<(string Role, StringBuilder Text)>();
            foreach (var m in rest) { string role = m.Role == PromptMessage.Assistant ? "model" : "user"; if (turns.Count > 0 && turns[^1].Role == role) turns[^1].Text.Append("\n\n").Append(m.Text); else turns.Add((role, new StringBuilder(m.Text))); }
            // It also wants the user to speak first.
            if (turns.Count > 0 && turns[0].Role == "model") turns.Insert(0, ("user", new StringBuilder("(시작)")));
            foreach (var (role, text) in turns) messages.Add(new { role, parts = new[] { new { text = text.ToString() } } });
            var generation = new Dictionary<string, object> { ["maxOutputTokens"] = maxTokens };
            // Unless the user chose a reasoning effort, these two answer without thinking first (short lines need none).
            if (c.Model.Trim() is "gemini-2.5-flash" or "gemini-2.5-flash-lite" && !thoughts) generation["thinkingConfig"] = new { thinkingBudget = 0 };
            payload = lead > 0 ? new { systemInstruction = new { parts = new[] { new { text = system } } }, contents = messages, generationConfig = generation } : new { contents = messages, generationConfig = generation };
        }
        else
        {
            if (c.Format == "Chat Completions" && lead > 0) messages.Add(new { role = "system", content = system });
            foreach (var m in rest) messages.Add(new { role = m.Role, content = m.Text });
            payload = c.Format == "Responses" ? (object)new { model = c.Model.Trim(), instructions = system, input = messages, max_output_tokens = maxTokens, store = false, stream = streaming } : new { model = c.Model.Trim(), messages, max_tokens = maxTokens, stream = streaming };
        }
        // The connection's own parameters (sampling, reasoning effort, what the user added or took out) go in last.
        var body = JsonSerializer.SerializeToNode(payload)!.AsObject(); var parameters = c.Parameters ?? new(); AiRequestOptions.Apply(body, c.Format, c.Model.Trim(), parameters);
        if (thoughts && c.Format == "Gemini" && body["generationConfig"] is System.Text.Json.Nodes.JsonObject generationConfig)
        {
            if (generationConfig["thinkingConfig"] is not System.Text.Json.Nodes.JsonObject thinking) generationConfig["thinkingConfig"] = thinking = new System.Text.Json.Nodes.JsonObject();
            thinking["includeThoughts"] = true;
        }
        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        AiRequestOptions.AddHeaders(request, parameters);
        if (streaming) request.Headers.Accept.Add(new("text/event-stream"));
        if (key.Length > 0 && c.Header.Length > 0)
        {
            if (!request.Headers.TryAddWithoutValidation(c.Header.Trim(), (c.Bearer ? "Bearer " : "") + key)) { request.Dispose(); throw new InvalidOperationException("인증 헤더 이름을 확인해 주세요."); }
        }
        return request;
    }
    // What the model thought before answering, when it says so: Gemini's thought parts, or the reasoning field some
    // Chat Completions servers add (DeepSeek, OpenRouter). "" when there is none.
    public static string Thoughts(string format, JsonElement root)
    {
        var text = new StringBuilder();
        if (format == "Gemini")
        {
            if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0 && candidates[0].TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                foreach (var part in parts.EnumerateArray()) if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True && part.TryGetProperty("text", out var value)) text.Append(value.GetString());
        }
        else if (format != "Responses" && root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var choice = choices[0]; var holder = choice.TryGetProperty("delta", out var delta) ? delta : choice.TryGetProperty("message", out var message) ? message : default;
            if (holder.ValueKind == JsonValueKind.Object) foreach (string name in new[] { "reasoning_content", "reasoning" }) if (holder.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) { text.Append(value.GetString()); break; }
        }
        return text.ToString();
    }    public static string Parse(string format, string json)
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
    // A long piece from a whole list of messages (the novel window). progress: the text as it arrives; thinking: what the
    // model thought, when it says. Cut off by the output limit, what came is kept; the writer can ask to go on.
    public async Task<string> Compose(ChatPreferences settings, IReadOnlyList<PromptMessage> messages, CancellationToken cancellation, Action<string>? progress = null, Action<string>? thinking = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        var c = settings.Current; bool streaming = settings.Streaming && progress != null; int tokens = settings.MaxOutputTokens;
        try
        {
            using var request = Build(c, SecretVault.Unprotect(c.Secret), messages, tokens, streaming, thinking != null);
            return await Exchange(settings, request, tokens, streaming, progress, thinking, deadline.Token).ConfigureAwait(false);
        }
        catch (ReplyLimitException limited) when (limited.Partial.Trim().Length > 0) { return limited.Partial.Trim(); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new InvalidOperationException($"답변 대기 시간 {settings.RequestTimeoutSeconds}초를 넘었습니다."); }
    }    // The message in an error answer, shortened to one line; "" when there is none.
    public static string Explain(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 200000) return "";
        try
        {
            using var json = JsonDocument.Parse(body); var root = json.RootElement; if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0];
            if (root.ValueKind != JsonValueKind.Object) return "";
            string text = root.TryGetProperty("error", out var error) ? error.ValueKind == JsonValueKind.String ? error.GetString() ?? "" : error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() ?? "" : ""
                : root.TryGetProperty("message", out var plain) && plain.ValueKind == JsonValueKind.String ? plain.GetString() ?? "" : "";
            return SafeFiles.OneLine(text, 240);
        }
        catch (JsonException) { return ""; }
    }
    async Task<string> SendOnce(ChatPreferences settings, ChatTurn[] history, string text, int tokens, CancellationToken cancellation, Action<string>? progress)
    {
        var c = settings.Current;
        bool streaming = settings.Streaming && progress != null;
        string system = CharacterPrompt.Build(settings.Profile); history = AiRequestOptions.Fit(history, system, text, settings.MaxContextTokens, tokens);
        using var request = Build(c, SecretVault.Unprotect(c.Secret), system, history, text, tokens, streaming);
        return await Exchange(settings, request, tokens, streaming, progress, null, cancellation).ConfigureAwait(false);
    }
    async Task<string> Exchange(ChatPreferences settings, HttpRequestMessage request, int tokens, bool streaming, Action<string>? progress, Action<string>? thinking, CancellationToken cancellation)
    {
        var c = settings.Current;
        using var usage = trackUsage ? AiUsage.Current.Begin(settings.Budget, settings.UsageKind, tokens) : null;
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string reason = (int)response.StatusCode switch { 401 or 403 => "API 키 또는 모델 이용 권한을 확인해 주세요.", 429 => "요청 한도 또는 잔액을 확인한 뒤 다시 시도해 주세요.", 400 or 404 => "모델 ID, 엔드포인트와 API 형식을 확인해 주세요.", _ => "서버에 연결하지 못했습니다. 잠시 후 다시 시도해 주세요." };
            // What the server said is usually the quickest way to see which parameter it did not accept.
            string said = ""; try { said = Explain(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)); } catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException) { }
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode} · {reason}" + (said.Length > 0 ? " (서버 응답: " + said + ")" : ""));
        }
        using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        if (streaming && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            string live = await SseReplies.Read(stream, c.Format, progress!, cancellation, usage == null ? null : usage.Capture, thinking).ConfigureAwait(false); usage?.Succeeded(); return live;
        }
        using var bytes = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        { if (bytes.Length + count > 4 * 1024 * 1024) throw new InvalidOperationException("응답이 너무 큽니다. 출력 한도를 줄여 주세요."); bytes.Write(buffer, 0, count); }
        string json = Encoding.UTF8.GetString(bytes.ToArray());
        using (var metadata = JsonDocument.Parse(json)) { usage?.Capture(metadata.RootElement); if (thinking != null && Thoughts(c.Format, metadata.RootElement) is { Length: > 0 } thought) thinking(thought); }
        string result = Parse(c.Format, json); usage?.Succeeded(); if (streaming) progress!(result); return result;
    }
}
