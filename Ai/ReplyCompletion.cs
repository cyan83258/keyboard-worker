using System;
using System.Text.Json;

namespace KeyboardWorker;

public sealed class ReplyLimitException : InvalidOperationException
{
    public string Partial { get; }
    public ReplyLimitException(string partial) : base("AI 출력 한도에 도달해 답변이 중간에 끝났습니다. 캐릭터 프로필 → 기억과 대화에서 출력 한도 또는 자동 이어받기를 조절해 주세요.") { Partial = partial; }
}
public static class ReplyCompletion
{
    public const int MaxCharacters = 32000;
    public static void Check(JsonElement root, string format, string text)
    {
        if (format == "Gemini" && root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0 && candidates[0].TryGetProperty("finishReason", out var finish))
        {
            if (finish.GetString() == "MAX_TOKENS") throw new ReplyLimitException(text);
            if (finish.GetString() is not "STOP" and not null) throw new InvalidOperationException("AI가 답변 생성을 중단했습니다. 종료 사유: " + finish.GetString());
        }
        if (format == "Responses" && root.TryGetProperty("status", out var status) && status.GetString() is "incomplete" or "failed")
        {
            if (root.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object && details.TryGetProperty("reason", out var reason) && reason.GetString() == "max_output_tokens") throw new ReplyLimitException(text);
            throw new InvalidOperationException("AI가 답변 생성을 완료하지 못했습니다. 미완성 답변은 저장하지 않았어요.");
        }
        if (format == "Chat Completions" && root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 && choices[0].TryGetProperty("finish_reason", out var ending) && ending.ValueKind == JsonValueKind.String)
        {
            if (ending.GetString() == "length") throw new ReplyLimitException(text);
            if (ending.GetString() is not "stop") throw new InvalidOperationException("AI가 답변 생성을 중단했습니다. 종료 사유: " + ending.GetString());
        }
    }
    public static string Join(string previous, string following)
    {
        for (int length = Math.Min(500, Math.Min(previous.Length, following.Length)); length >= 12; length--)
            if (previous.AsSpan(previous.Length - length).SequenceEqual(following.AsSpan(0, length))) return previous + following[length..];
        return previous + following;
    }
}
