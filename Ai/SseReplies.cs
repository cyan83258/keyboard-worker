using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

public static class SseReplies
{
    public static async Task<string> Read(Stream stream, string format, Action<string> progress, CancellationToken cancellation, Action<JsonElement>? usage = null)
    {
        using var bounded = new BoundedStream(stream, 2 * 1024 * 1024);
        using var reader = new StreamReader(bounded, Encoding.UTF8, true, 4096, true);
        var record = new StringBuilder(); var answer = new StringBuilder(); bool finished = false, limited = false;
        long lastReport = long.MinValue / 2; string lastText = "";
        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellation).ConfigureAwait(false);
            if (line == null) { if (record.Length > 0) Consume(record.ToString()); break; }
            if (line.Length == 0) { if (record.Length > 0) { Consume(record.ToString()); record.Clear(); } if (finished) break; continue; }
            if (line.StartsWith("data:", StringComparison.Ordinal)) { if (record.Length > 0) record.Append('\n'); record.Append(line.AsSpan(5).TrimStart()); }
            if (record.Length > 256 * 1024) throw new InvalidOperationException("스트리밍 응답 조각이 너무 큽니다.");
        }
        if (!finished) throw new InvalidOperationException("답변 수신 중 연결이 끊겼습니다. 미완성 답변은 대화로 저장하지 않았어요.");
        string result = answer.ToString(); if (result != lastText) progress(result);
        if (limited) throw new ReplyLimitException(result);
        if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("AI가 텍스트 답변을 반환하지 않았습니다."); return result;
        void Consume(string json)
        {
            if (json.Trim() == "[DONE]") { finished = true; return; }
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement; string delta = "";
            usage?.Invoke(root);
            if (root.TryGetProperty("error", out _) || (root.TryGetProperty("type", out var failure) && failure.GetString() is "error" or "response.failed"))
                throw new InvalidOperationException("AI가 답변 생성을 완료하지 못했습니다. 연결 설정 또는 출력 한도를 확인해 주세요.");
            if (format == "Responses")
            {
                string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                if (type == "response.output_text.delta" && root.TryGetProperty("delta", out var d)) delta = d.GetString() ?? "";
                if (type is "response.completed" or "response.incomplete")
                {
                    finished = true;
                    if (root.TryGetProperty("response", out var response))
                    {
                        if (answer.Length == 0)
                        {
                            try { delta = AiClient.Parse("Responses", response.GetRawText()); }
                            catch (ReplyLimitException e) { delta = e.Partial; limited = true; }
                        }
                        try { ReplyCompletion.Check(response, "Responses", answer.ToString()); }
                        catch (ReplyLimitException) { limited = true; }
                    }
                    if (type == "response.incomplete" && !limited) throw new InvalidOperationException("AI가 답변을 완료하지 못했습니다. 미완성 답변은 저장하지 않았어요.");
                }
            }
            else if (format == "Gemini")
            {
                if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var candidate = candidates[0];
                    if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                        foreach (var part in parts.EnumerateArray())
                            if (!(part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) && part.TryGetProperty("text", out var text)) delta += text.GetString();
                    if (candidate.TryGetProperty("finishReason", out var reason))
                    {
                        if (reason.GetString() is "STOP" or "MAX_TOKENS") { finished = true; limited = reason.GetString() == "MAX_TOKENS"; }
                        else throw new InvalidOperationException("AI가 답변을 중단했습니다. 미완성 답변은 저장하지 않았어요.");
                    }
                }
            }
            else if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var d) && d.TryGetProperty("content", out var text) && text.ValueKind == JsonValueKind.String) delta = text.GetString() ?? "";
                if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                {
                    if (reason.GetString() is not "stop" and not "length") throw new InvalidOperationException("AI가 답변 생성을 중단했습니다. 미완성 답변은 저장하지 않았어요.");
                    finished = true; limited = reason.GetString() == "length";
                }
            }
            if (delta.Length > 0)
            {
                if (answer.Length + delta.Length > ReplyCompletion.MaxCharacters) throw new InvalidOperationException("답변이 너무 깁니다. 출력 한도를 줄여 주세요.");
                answer.Append(delta); long now = Environment.TickCount64;
                if (now - lastReport >= 50) { lastReport = now; lastText = answer.ToString(); progress(lastText); }
            }
        }
    }
    sealed class BoundedStream : Stream
    {
        readonly Stream source; readonly long max; long count;
        public BoundedStream(Stream source, long max) { this.source = source; this.max = max; }
        void Count(int size) { count += size; if (count > max) throw new InvalidOperationException("스트리밍 응답이 너무 큽니다."); }
        public override int Read(byte[] buffer, int offset, int size) { int n = source.Read(buffer, offset, size); Count(n); return n; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { int n = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); Count(n); return n; }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => count; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
