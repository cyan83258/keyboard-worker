using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyboardWorker;

// The finer parameters of an AI connection: what goes into the request for each kind of API, and the context limit.
public static class AiParameterTests
{
    public static int Run()
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        bool Throws(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
        JsonObject Body(string format, string model, AiParameters p)
        {
            using var request = AiClient.Build(new AiConnection { Format = format, Model = model, Endpoint = "https://api.example.com/v1/{model}", Header = "Authorization", Bearer = true, Parameters = p }, "key", "system", new[] { new ChatTurn("이전", "답", DateTimeOffset.Now) }, "지금", 700);
            return JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!.AsObject();
        }
        double Num(JsonNode? node) => node!.GetValue<double>();

        // Nothing set: the request is as before
        var plain = Body("Chat Completions", "m", new AiParameters());
        Check(plain.Select(p => p.Key).OrderBy(k => k).SequenceEqual(new[] { "max_tokens", "messages", "model", "stream" }) && Num(plain["max_tokens"]) == 700, "With nothing set, nothing is added to a request");
        var flash = Body("Gemini", "gemini-2.5-flash", new AiParameters()); Check(Num(flash["generationConfig"]!["thinkingConfig"]!["thinkingBudget"]) == 0 && Body("Gemini", "gemini-2.5-pro", new AiParameters())["generationConfig"]!["thinkingConfig"] == null, "The quick Gemini models still answer without thinking unless told otherwise");

        // Sampling, in each API's own words
        var all = new AiParameters { Temperature = .7, TopP = .9, TopK = 40, FrequencyPenalty = .2, PresencePenalty = -.3, RepetitionPenalty = 1.1, MinP = .05, TopA = .2 };
        var chat = Body("Chat Completions", "m", all);
        Check(Num(chat["temperature"]) == .7 && Num(chat["top_p"]) == .9 && Num(chat["top_k"]) == 40 && Num(chat["frequency_penalty"]) == .2 && Num(chat["presence_penalty"]) == -.3 && Num(chat["repetition_penalty"]) == 1.1 && Num(chat["min_p"]) == .05 && Num(chat["top_a"]) == .2 && chat["reasoning_effort"] == null && chat["verbosity"] == null, "A chat-completions request carries every sampling parameter under its usual name");
        var responses = Body("Responses", "m", all);
        Check(Num(responses["temperature"]) == .7 && Num(responses["top_p"]) == .9 && responses["top_k"] == null && responses["frequency_penalty"] == null && responses["repetition_penalty"] == null && responses["min_p"] == null, "A responses request carries only the sampling parameters that API knows");
        var gemini = Body("Gemini", "gemini-2.5-pro", all)["generationConfig"]!.AsObject();
        Check(Num(gemini["temperature"]) == .7 && Num(gemini["topP"]) == .9 && Num(gemini["topK"]) == 40 && Num(gemini["frequencyPenalty"]) == .2 && Num(gemini["presencePenalty"]) == -.3 && gemini["repetitionPenalty"] == null && gemini["minP"] == null && Num(gemini["maxOutputTokens"]) == 700, "A Gemini request carries them inside its generation settings");

        // Reasoning effort and verbosity
        string Effort(string key) => (string)Body("Chat Completions", "m", new AiParameters { ReasoningEffort = key })["reasoning_effort"]!;
        Check(Effort("none") == "none" && Effort("minimal") == "minimal" && Effort("low") == "low" && Effort("medium") == "medium" && Effort("high") == "high" && Effort("max") == "xhigh", "Reasoning effort is sent in the API's words, the strongest as its highest setting");
        var thoughtful = Body("Responses", "m", new AiParameters { ReasoningEffort = "high", Verbosity = "low" });
        Check((string)thoughtful["reasoning"]!["effort"]! == "high" && (string)thoughtful["text"]!["verbosity"]! == "low" && thoughtful["reasoning_effort"] == null && (string)Body("Chat Completions", "m", new AiParameters { Verbosity = "high" })["verbosity"]! == "high", "The responses API takes effort and verbosity in their own objects");
        int Budget(string key, string model = "gemini-2.5-flash") => (int)Num(Body("Gemini", model, new AiParameters { ReasoningEffort = key })["generationConfig"]!["thinkingConfig"]!["thinkingBudget"]);
        Check(Budget("none") == 0 && Budget("minimal") < Budget("low") && Budget("low") < Budget("medium") && Budget("medium") < Budget("high") && Budget("high") < Budget("max") && Budget("high", "gemini-2.5-pro") == Budget("high"), "Gemini 2 models get a thinking budget that grows with the effort");
        string Level(string key) => (string)Body("Gemini", "gemini-3.8-flash", new AiParameters { ReasoningEffort = key })["generationConfig"]!["thinkingConfig"]!["thinkingLevel"]!;
        Check(Level("none") == "low" && Level("minimal") == "low" && Level("low") == "low" && Level("medium") == "medium" && Level("high") == "high" && Level("max") == "high" && Body("Gemini", "gemini-3.8-flash", new AiParameters { ReasoningEffort = "low" })["generationConfig"]!["thinkingConfig"]!["thinkingBudget"] == null, "Newer Gemini models get a thinking level instead");
        Check(Body("Gemini", "gemini-2.5-flash", new AiParameters { Verbosity = "high" })["verbosity"] == null && Body("Gemini", "gemini-2.5-flash", new AiParameters { Verbosity = "high" })["generationConfig"]!["verbosity"] == null, "Gemini has no verbosity setting, so none is sent");

        // What the user adds and takes out
        var custom = Body("Chat Completions", "m", new AiParameters { Temperature = .7, IncludeBody = "{ \"seed\": 7, \"temperature\": 1.5, \"provider\": { \"order\": [\"a\"] } }", ExcludeBody = "max_tokens, stream\nno_such_name" });
        Check(Num(custom["seed"]) == 7 && Num(custom["temperature"]) == 1.5 && (string)custom["provider"]!["order"]![0]! == "a" && custom["max_tokens"] == null && custom["stream"] == null && custom["messages"] != null, "Added body parameters are merged in (and win over the boxes); excluded ones are left out");
        var deep = Body("Gemini", "gemini-2.5-flash", new AiParameters { IncludeBody = "{\"generationConfig\":{\"seed\":3},\"safetySettings\":[]}", ExcludeBody = "generationConfig.thinkingConfig" })["generationConfig"]!.AsObject();
        Check(Num(deep["seed"]) == 3 && Num(deep["maxOutputTokens"]) == 700 && deep["thinkingConfig"] == null, "An added object is merged into the one already there, and a dotted name reaches inside");
        Check(Throws(() => Body("Chat Completions", "m", new AiParameters { IncludeBody = "seed: 7" })) && Throws(() => Body("Chat Completions", "m", new AiParameters { IncludeBody = "[1,2]" })) && Throws(() => new AiParameters { IncludeBody = "\"text\"" }.Extra()) && new AiParameters { IncludeBody = "  " }.Extra() == null, "Added body parameters must be a JSON object");
        var headers = AiRequestOptions.Headers("HTTP-Referer: https://example.com/a:b\nX-Title:  Keyboard Worker \n\nnot a header\n: empty\nBad Name: x\nHost: evil\nContent-Length: 5\nX-Empty:\nX-Inject: a\r\nX-Second: b");
        Check(headers.Select(h => h.Name).SequenceEqual(new[] { "HTTP-Referer", "X-Title", "X-Inject", "X-Second" }) && headers[0].Value == "https://example.com/a:b" && headers[1].Value == "Keyboard Worker" && headers[2].Value == "a", "Headers are read one per line; lines that are not headers and the ones a request sets itself are dropped");
        using (var request = AiClient.Build(new AiConnection { Format = "Chat Completions", Model = "m", Endpoint = "https://api.example.com/v1", Header = "Authorization", Bearer = true, Parameters = new AiParameters { IncludeHeaders = "X-Title: KW\nX-Title: Again\nHTTP-Referer: https://example.com" } }, "secret", "", Array.Empty<ChatTurn>(), "hi", 100))
            Check(request.Headers.GetValues("X-Title").Single() == "Again" && request.Headers.GetValues("HTTP-Referer").Single() == "https://example.com" && request.Headers.GetValues("Authorization").Single() == "Bearer secret", "Added headers travel with the request beside the key");

        // Staying within range
        var wild = new AiParameters { Temperature = 9, TopP = -1, TopK = 0, FrequencyPenalty = double.NaN, PresencePenalty = 5, RepetitionPenalty = -3, MinP = 2, TopA = double.PositiveInfinity, ReasoningEffort = "extreme", Verbosity = "chatty", IncludeBody = new string('x', 9000), ExcludeBody = null!, IncludeHeaders = null! }; wild.Normalize();
        Check(wild.Temperature == 2 && wild.TopP == 0 && wild.TopK == 1 && wild.FrequencyPenalty == null && wild.PresencePenalty == 2 && wild.RepetitionPenalty == 0 && wild.MinP == 1 && wild.TopA == null && wild.ReasoningEffort == "" && wild.Verbosity == "" && wild.IncludeBody.Length == 4000 && wild.ExcludeBody == "" && wild.IncludeHeaders == "", "Parameters are kept within the ranges the APIs accept");
        Check(AiParameters.Efforts.Select(e => e.Key).SequenceEqual(new[] { "", "none", "minimal", "low", "medium", "high", "max" }) && AiParameters.Verbosities.Select(v => v.Key).SequenceEqual(new[] { "", "low", "medium", "high" }) && !AiParameterPanel.Any(new AiParameters()) && AiParameterPanel.Any(new AiParameters { TopK = 5 }) && AiParameterPanel.Any(new AiParameters { IncludeHeaders = "a: b" }), "Six efforts and three verbosities can be chosen, beside sending none");
        var saved = new ChatPreferences(); saved.Connections[0].Parameters = new AiParameters { Temperature = .5, ReasoningEffort = "low" }; saved.Connections[1].Parameters = null!; saved.MaxContextTokens = 120; saved.Normalize();
        var copy = ChatRepository.Clone(saved); copy.Connections[0].Parameters.Temperature = 1;
        Check(saved.Connections[0].Parameters.Temperature == .5 && copy.Connections[0].Parameters.ReasoningEffort == "low" && saved.Connections[1].Parameters != null && saved.MaxContextTokens == 500 && new ChatPreferences().MaxContextTokens == 0, "Parameters belong to their connection, are copied with it, and the context limit has a sensible floor");
        var reloaded = JsonSerializer.Deserialize<ChatPreferences>(JsonSerializer.Serialize(saved))!; reloaded.Normalize();
        Check(reloaded.Connections[0].Parameters.Temperature == .5 && reloaded.Connections[0].Parameters.TopK == null && reloaded.MaxContextTokens == 500, "They are saved with the chat settings");

        // The context limit
        Check(AiRequestOptions.Estimate("") == 0 && AiRequestOptions.Estimate(null) == 0 && AiRequestOptions.Estimate("가나다") == 3 && AiRequestOptions.Estimate("abcdef") == 2 && AiRequestOptions.Estimate("a") == 1, "Tokens are estimated on the safe side: about one per Korean letter, one per three others");
        var history = Enumerable.Range(0, 10).Select(i => new ChatTurn("질문 " + i + new string('가', 40), "대답 " + i + new string('나', 40), DateTimeOffset.Now)).ToArray();
        Check(ReferenceEquals(AiRequestOptions.Fit(history, "시스템", "지금", 0), history) && ReferenceEquals(AiRequestOptions.Fit(history, "시스템", "지금", 100000), history), "Without a limit, or within it, the whole conversation is sent");
        var fitted = AiRequestOptions.Fit(history, "시스템", "지금", 400);
        Check(fitted.Length is > 0 and < 10 && fitted[^1].User.StartsWith("질문 9") && fitted[0].User == history[10 - fitted.Length].User && AiRequestOptions.Estimate("시스템") + AiRequestOptions.Estimate("지금") + fitted.Sum(t => AiRequestOptions.Estimate(t.User) + AiRequestOptions.Estimate(t.Assistant) + 8) <= 400, "Over the limit, the oldest exchanges are left out and the newest kept");
        Check(AiRequestOptions.Fit(history, "시스템", "지금", 400, 300).Length < fitted.Length && AiRequestOptions.Fit(history, new string('가', 5000), "지금", 400).Length == 0, "Room is left for the answer, and a profile that fills the limit alone still goes out with the new message");

        // What a server says when it refuses
        Check(AiClient.Explain("{\"error\":{\"message\":\"Unknown parameter: 'top_a'.\\nSee docs.\",\"type\":\"invalid_request_error\"}}") == "Unknown parameter: 'top_a'. See docs." && AiClient.Explain("{\"error\":\"model not found\"}") == "model not found" && AiClient.Explain("[{\"error\":{\"message\":\"bad\"}}]") == "bad" && AiClient.Explain("{\"message\":\"plain\"}") == "plain" && AiClient.Explain("<html>502</html>") == "" && AiClient.Explain("") == "" && AiClient.Explain("{\"error\":{\"message\":\"" + new string('x', 900) + "\"}}").Length == 240, "A refusal's message is passed on in one short line");
        return count;
    }
}
