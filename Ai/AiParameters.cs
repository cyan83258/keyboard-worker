using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyboardWorker;

// The finer settings of one AI connection. A number left empty (null) is not sent, so the server's own default applies.
public sealed class AiParameters
{
    public static readonly (string Key, string Name)[] Efforts = { ("", "기본값 (보내지 않음)"), ("none", "없음"), ("minimal", "최소"), ("low", "낮음"), ("medium", "보통"), ("high", "높음"), ("max", "최대") };
    public static readonly (string Key, string Name)[] Verbosities = { ("", "기본값 (보내지 않음)"), ("low", "간결하게"), ("medium", "보통"), ("high", "자세하게") };
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? TopK { get; set; }
    public double? FrequencyPenalty { get; set; }
    public double? PresencePenalty { get; set; }
    public double? RepetitionPenalty { get; set; }
    public double? MinP { get; set; }
    public double? TopA { get; set; }
    public string ReasoningEffort { get; set; } = "";   // how hard the model thinks before answering
    public string Verbosity { get; set; } = "";         // how much it writes
    public string IncludeBody { get; set; } = "";       // a JSON object merged into the request body
    public string ExcludeBody { get; set; } = "";       // names taken out of the body (commas or lines; "a.b" reaches inside)
    public string IncludeHeaders { get; set; } = "";    // "Name: value" per line
    public AiParameters Snapshot() => (AiParameters)MemberwiseClone();
    public void Normalize()
    {
        static double? Range(double? value, double low, double high) => value is double v && double.IsFinite(v) ? Math.Clamp(v, low, high) : null;
        Temperature = Range(Temperature, 0, 2); TopP = Range(TopP, 0, 1); TopK = TopK is int k ? Math.Clamp(k, 1, 1000) : null;
        FrequencyPenalty = Range(FrequencyPenalty, -2, 2); PresencePenalty = Range(PresencePenalty, -2, 2); RepetitionPenalty = Range(RepetitionPenalty, 0, 2); MinP = Range(MinP, 0, 1); TopA = Range(TopA, 0, 1);
        if (Efforts.All(e => e.Key != ReasoningEffort)) ReasoningEffort = ""; if (Verbosities.All(v => v.Key != Verbosity)) Verbosity = "";
        static string Cut(string? text) { text = (text ?? "").Replace("\r\n", "\n").Trim(); return text[..Math.Min(text.Length, 4000)]; }
        IncludeBody = Cut(IncludeBody); ExcludeBody = Cut(ExcludeBody); IncludeHeaders = Cut(IncludeHeaders);
    }
    // The body the user wants merged in, checked. Throws with a readable message when it is not a JSON object.
    public JsonObject? Extra()
    {
        if (IncludeBody.Trim().Length == 0) return null;
        try { return JsonNode.Parse(IncludeBody) as JsonObject ?? throw new InvalidOperationException("'본문에 추가할 파라미터'는 { } 로 감싼 JSON 객체여야 합니다."); }
        catch (JsonException e) { throw new InvalidOperationException("'본문에 추가할 파라미터'의 JSON 형식이 올바르지 않습니다. " + e.Message); }
    }
}

// Puts a connection's parameters into a request, in the words each kind of API uses, and keeps the conversation within the context limit.
public static class AiRequestOptions
{
    static void Set(JsonObject body, string name, double? value) { if (value is double number) body[name] = number; }
    static JsonObject Inside(JsonObject body, string name) { if (body[name] is not JsonObject inner) body[name] = inner = new JsonObject(); return inner; }
    // "max" is the strongest setting each API knows.
    static string Effort(string key) => key == "max" ? "xhigh" : key;
    public static void Apply(JsonObject body, string format, string model, AiParameters p)
    {
        if (format == "Gemini")
        {
            var config = Inside(body, "generationConfig");
            Set(config, "temperature", p.Temperature); Set(config, "topP", p.TopP); if (p.TopK is int k) config["topK"] = k; Set(config, "frequencyPenalty", p.FrequencyPenalty); Set(config, "presencePenalty", p.PresencePenalty);
            if (p.ReasoningEffort.Length > 0)
            {
                // Gemini 3 takes a level; earlier models take a number of tokens the model may spend on thinking (0 = none).
                // The lowest level every Gemini 3 model accepts is "low" ("minimal" is refused by some), so "none" and "minimal" become that.
                bool levels = !model.Contains("gemini-2", StringComparison.OrdinalIgnoreCase) && !model.Contains("gemini-1", StringComparison.OrdinalIgnoreCase);
                config["thinkingConfig"] = levels
                    ? new JsonObject { ["thinkingLevel"] = p.ReasoningEffort switch { "none" or "minimal" or "low" => "low", "medium" => "medium", _ => "high" } }
                    : new JsonObject { ["thinkingBudget"] = p.ReasoningEffort switch { "none" => 0, "minimal" => 512, "low" => 2048, "medium" => 8192, "high" => 16384, _ => 24576 } };
            }
        }
        else if (format == "Responses")
        {
            Set(body, "temperature", p.Temperature); Set(body, "top_p", p.TopP);
            if (p.ReasoningEffort.Length > 0) Inside(body, "reasoning")["effort"] = Effort(p.ReasoningEffort);
            if (p.Verbosity.Length > 0) Inside(body, "text")["verbosity"] = p.Verbosity;
        }
        else
        {
            Set(body, "temperature", p.Temperature); Set(body, "top_p", p.TopP); if (p.TopK is int k) body["top_k"] = k;
            Set(body, "frequency_penalty", p.FrequencyPenalty); Set(body, "presence_penalty", p.PresencePenalty); Set(body, "repetition_penalty", p.RepetitionPenalty); Set(body, "min_p", p.MinP); Set(body, "top_a", p.TopA);
            if (p.ReasoningEffort.Length > 0) body["reasoning_effort"] = Effort(p.ReasoningEffort);
            if (p.Verbosity.Length > 0) body["verbosity"] = p.Verbosity;
        }
        // What the user adds wins over what was put there; an object is merged into an object so one added field does not wipe the rest.
        if (p.Extra() is { } extra)
            foreach (var (name, value) in extra.ToArray())
            {
                extra.Remove(name);
                if (value is JsonObject added && body[name] is JsonObject present) foreach (var (inner, innerValue) in added.ToArray()) { added.Remove(inner); present[inner] = innerValue; }
                else body[name] = value;
            }
        foreach (string name in Names(p.ExcludeBody))
        {
            var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); JsonObject? at = body;
            for (int i = 0; i < parts.Length - 1 && at != null; i++) at = at[parts[i]] as JsonObject;
            if (parts.Length > 0) at?.Remove(parts[^1]);
        }
    }
    public static string[] Names(string list) => list.Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    // "Name: value" lines as headers. Lines that are not headers, and the few a request must set itself, are left out.
    public static (string Name, string Value)[] Headers(string lines)
    {
        var headers = new List<(string, string)>();
        foreach (string line in lines.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = line.IndexOf(':'); if (colon <= 0) continue; string name = line[..colon].Trim(), value = line[(colon + 1)..].Trim();
            if (name.Length == 0 || value.Length == 0 || name.Any(c => c <= ' ' || c > '~' || "()<>@,;:\\\"/[]?={}".Contains(c)) || name.ToLowerInvariant() is "host" or "content-length" or "content-type" or "transfer-encoding" or "connection") continue;
            headers.Add((name, value));
        }
        return headers.ToArray();
    }
    public static void AddHeaders(HttpRequestMessage request, AiParameters p)
    {
        foreach (var (name, value) in Headers(p.IncludeHeaders)) { request.Headers.Remove(name); request.Headers.TryAddWithoutValidation(name, value); }
    }

    // A rough count of tokens, on the safe side: about one per Korean letter and one per three other letters.
    public static int Estimate(string? text) => string.IsNullOrEmpty(text) ? 0 : (Encoding.UTF8.GetByteCount(text) + 2) / 3;
    // Drops the oldest exchanges until the request fits the context limit (0 = no limit). The profile and the new message always stay.
    public static ChatTurn[] Fit(ChatTurn[] history, string system, string text, int maxContextTokens, int replyTokens = 0)
    {
        if (maxContextTokens <= 0 || history.Length == 0) return history;
        int room = maxContextTokens - Estimate(system) - Estimate(text) - Math.Max(0, replyTokens), used = 0, keep = 0;
        for (int i = history.Length - 1; i >= 0; i--) { used += Estimate(history[i].User) + Estimate(history[i].Assistant) + 8; if (used > room) break; keep++; }
        return keep == history.Length ? history : history[^keep..];
    }
}
