using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace KeyboardWorker;

// The novel: SillyTavern presets read in, the request made from the parts that are on, any list of messages sent, and
// what the model thought taken back.
public static class NovelTests
{
    const string Preset = """
        {
          "temperature": 1.2, "top_p": 0.9, "top_k": 0, "min_p": 0.05, "repetition_penalty": 1, "openai_max_context": 16000, "openai_max_tokens": 3000,
          "stream_openai": true, "show_thoughts": true, "reasoning_effort": "high", "assistant_prefill": "",
          "prompts": [
            { "identifier": "main", "name": "주 지시", "role": "system", "content": "{{char}}의 이야기를 {{user}}에게 들려준다." },
            { "identifier": "nsfw", "name": "꺼둔 것", "role": "user", "content": "쓰지 않는 지시" },
            { "identifier": "chatHistory", "name": "Chat History", "system_prompt": true, "marker": true },
            { "identifier": "charDescription", "name": "Char Description", "marker": true },
            { "identifier": "scenario", "name": "Scenario", "marker": true },
            { "identifier": "last", "name": "마지막 지시", "role": "user", "content": "마지막 요청: {{lastMessage}}" },
            { "identifier": "jailbreak", "name": "끝맺음", "role": "assistant", "content": "확인. 이어서:" },
            { "identifier": "unused", "name": "순서에 없는 것", "role": "system", "content": "안 쓰임" }
          ],
          "prompt_order": [
            { "character_id": 100000, "order": [ { "identifier": "main", "enabled": true } ] },
            { "character_id": 100001, "order": [
              { "identifier": "main", "enabled": true }, { "identifier": "charDescription", "enabled": true }, { "identifier": "scenario", "enabled": true },
              { "identifier": "nsfw", "enabled": false }, { "identifier": "chatHistory", "enabled": true }, { "identifier": "last", "enabled": true }, { "identifier": "jailbreak", "enabled": true } ] }
          ]
        }
        """;
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        bool Throws(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }

        // ---- reading a SillyTavern preset ----
        var (set, sampling) = NovelPrompts.ReadSillyTavern(Preset, "테스트 프리셋");
        Check(set.Name == "테스트 프리셋" && set.Parts.Select(p => p.Identifier).SequenceEqual(new[] { "main", "charDescription", "scenario", "nsfw", "chatHistory", "last", "jailbreak", "unused" }), "The parts come in the order of the user's own list (100001), the rest after");
        Check(!set.Parts.Single(p => p.Identifier == "nsfw").Enabled && !set.Parts.Single(p => p.Identifier == "unused").Enabled && set.Parts.Single(p => p.Identifier == "main").Enabled && set.Parts.Single(p => p.Identifier == "chatHistory").Marker && !set.Parts.Single(p => p.Identifier == "jailbreak").Marker && set.Parts.Single(p => p.Identifier == "jailbreak").Role == PromptMessage.Assistant, "Each part is on or off as in the preset, and markers are told from messages");
        Check(sampling.Parameters is { Temperature: 1.2, TopP: 0.9, TopK: null, MinP: 0.05, RepetitionPenalty: null, ReasoningEffort: "high" } && sampling.MaxContext == 16000 && sampling.MaxOutput == 3000 && sampling.Stream == true && sampling.ShowThinking == true, "The sampling settings of the preset are read, and its 'off' values left out");
        Check(Throws(() => NovelPrompts.ReadSillyTavern("{}", "x")) && Throws(() => NovelPrompts.ReadSillyTavern("not json", "x")) && Throws(() => NovelPrompts.ReadSillyTavern("{\"prompts\":[]}", "x")), "What is not a preset is refused with a message");

        // ---- making a request ----
        var cast = new[] { new Persona { Name = "루카", Personality = "냉정함", Examples = "흥." }, new Persona { Name = "미오", Personality = "다정함" } };
        var story = Enumerable.Range(0, 6).Select(i => new ChatTurn(i == 0 ? "" : "지시 " + i, "본문 " + i + " " + new string('가', 300), DateTimeOffset.Now.AddMinutes(i), "루카 · 미오 · 소설")).ToArray();
        var scene = new NovelScene(cast, "나", "고양이를 좋아함", "비 오는 밤의 도서관", "- 둘은 예전에 다퉜다.", story, "둘이 화해한다");
        var messages = NovelPrompts.Build(set, scene);
        Check(messages[0] == new PromptMessage(PromptMessage.System, "루카의 이야기를 나에게 들려준다.") && messages[1].Text.StartsWith("[루카의 설정]") && messages[1].Text.Contains("냉정함") && messages[2].Text.StartsWith("[미오의 설정]") && messages[3].Text == "[상황 · 작가 노트]\n비 오는 밤의 도서관", "Markers become the cast, the scene and macros the names");
        Check(messages[4].Text.StartsWith("[지금까지의 줄거리]") && messages[5] == new PromptMessage(PromptMessage.User, NovelPrompts.Asked("")) && messages[6].Role == PromptMessage.Assistant && messages[6].Text.StartsWith("본문 0") && messages[^3] == new PromptMessage(PromptMessage.User, "둘이 화해한다") && messages[^2].Text == "마지막 요청: 둘이 화해한다" && messages[^1] == new PromptMessage(PromptMessage.Assistant, "확인. 이어서:"), "The story so far sits where the marker is, then what is asked now, then the parts after it");
        Check(!messages.Any(m => m.Text.Contains("쓰지 않는 지시") || m.Text.Contains("안 쓰임")) && messages.Count(m => m.Role == PromptMessage.Assistant) == 7, "Parts that are off are not sent");
        static int Tokens(System.Collections.Generic.IEnumerable<PromptMessage> m) => m.Sum(x => AiRequestOptions.Estimate(x.Text) + 4);
        int bare = Tokens(NovelPrompts.Build(set, scene with { Story = Array.Empty<ChatTurn>() })), part = AiRequestOptions.Estimate(NovelPrompts.Asked(story[5].User)) + 4 + AiRequestOptions.Estimate(story[5].Assistant) + 4, limit = bare + 300 + part * 2 + 10;
        var fitted = NovelPrompts.Build(set, scene, limit, 300);
        Check(fitted.Count(m => m.Text.StartsWith("본문")) == 2 && fitted.Any(m => m.Text.StartsWith("본문 5")) && fitted.Any(m => m.Text.StartsWith("본문 4")) && fitted[^3].Text == "둘이 화해한다" && Tokens(fitted) + 300 <= limit, "The story is cut from its oldest end to fit the context, the newest parts and the request kept");
        var plain = NovelPrompts.Standard(); plain.Parts.RemoveAll(p => p.Identifier == "chatHistory"); plain.Prefill = "{{char}}은(는)";
        var tail = NovelPrompts.Build(plain, scene with { Story = Array.Empty<ChatTurn>(), Summary = "" });
        Check(tail[^2] == new PromptMessage(PromptMessage.User, "둘이 화해한다") && tail[^1] == new PromptMessage(PromptMessage.Assistant, "루카은(는)") && tail.Any(m => m.Text.StartsWith("[사용자(나)]") && m.Text.Contains("고양이")), "Without the marker the story goes at the end; a prefill is the model's first words; the persona is told");
        Check(NovelPrompts.Fill("{{char}}·{{user}}·{{group}}·{{unknown}}", scene) == "루카·나·루카, 미오·{{unknown}}" && NovelPrompts.Normalize(null).Parts.Any(p => p.Identifier == "chatHistory") && NovelPrompts.Normalize(new NovelPromptSet { Parts = new() { new NovelPromptPart { Role = "weird", Content = null! } } }).Parts.Single() is { Role: PromptMessage.System, Content: "", Name: "프롬프트" }, "Unknown macros stay; an empty set falls back to the standard one; parts are cleaned");

        // ---- a guide for one part, going on, and the prompt shaped as SillyTavern does ----
        var guided = NovelPrompts.Build(set, scene with { Guide = "{{char}}이(가) 먼저 사과한다" });
        int asked = guided.FindIndex(m => m.Text == "둘이 화해한다");
        Check(guided[asked + 1] == new PromptMessage(PromptMessage.System, "[다음 전개 지시 · 이번 장면에만 적용]\n루카이(가) 먼저 사과한다") && !messages.Any(m => m.Text.Contains("다음 전개 지시")), "A guide follows what is asked now, and only when there is one");
        Check(NovelPrompts.Build(set, scene with { Direction = NovelPrompts.Continue }).Any(m => m.Text == NovelPrompts.Continue), "Going on asks the model to continue the last part");
        var raw = new[] { new PromptMessage("system", "a"), new PromptMessage("system", "b"), new PromptMessage("user", "c"), new PromptMessage("user", "d"), new PromptMessage("assistant", "e") };
        Check(NovelPrompts.Shape(raw, true, false, false).Count == 5 && NovelPrompts.Shape(raw, true, true, false).Select(m => m.Text).SequenceEqual(new[] { "a\n\nb", "c", "d", "e" }) && NovelPrompts.Shape(raw, true, false, true).Select(m => m.Text).SequenceEqual(new[] { "a\n\nb", "c\n\nd", "e" }), "System messages in a row can be squashed, any role in a row merged");
        Check(NovelPrompts.Shape(raw, false, false, false).All(m => m.Role != "system") && NovelPrompts.Shape(raw, false, false, true).First() == new PromptMessage("user", "a\n\nb\n\nc\n\nd"), "Without a system prompt, system messages go as the user's");

        var list = new[] { new PromptMessage(PromptMessage.System, "규칙"), new PromptMessage(PromptMessage.Assistant, "확인"), new PromptMessage(PromptMessage.System, "설정"), new PromptMessage(PromptMessage.User, "지시"), new PromptMessage(PromptMessage.User, "추가"), new PromptMessage(PromptMessage.Assistant, "이어서:") };
        string Body(string format, bool thoughts = false) { using var request = AiClient.Build(new AiConnection { Format = format, Model = "gemini-3-pro", Endpoint = "https://api.example.com/v1/{model}:generateContent", Header = "x-key" }, "k", list, 1000, false, thoughts); return request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(); }
        using (var gemini = JsonDocument.Parse(Body("Gemini", true)))
        {
            var g = gemini.RootElement; var contents = g.GetProperty("contents");
            Check(g.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString() == "규칙" && contents.GetArrayLength() == 4 && contents[0].GetProperty("role").GetString() == "user" && contents[1].GetProperty("role").GetString() == "model" && contents[2].GetProperty("parts")[0].GetProperty("text").GetString() == "설정\n\n지시\n\n추가" && contents[3].GetProperty("role").GetString() == "model", "For Gemini the leading system message is the system prompt, messages of one role in a row are joined, and the user speaks first");
            Check(g.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("includeThoughts").GetBoolean(), "Gemini is asked for its thoughts when they are wanted");
        }
        using (var chat = JsonDocument.Parse(Body("Chat Completions")))
        {
            var roles = chat.RootElement.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()).ToArray();
            Check(roles.SequenceEqual(new[] { "system", "assistant", "system", "user", "user", "assistant" }) && !Body("Gemini").Contains("includeThoughts"), "Chat Completions keeps every role; thoughts are only asked for when wanted");
        }
        using (var responses = JsonDocument.Parse(Body("Responses"))) Check(responses.RootElement.GetProperty("instructions").GetString() == "규칙" && responses.RootElement.GetProperty("input").GetArrayLength() == 5, "Responses has the system prompt as instructions");

        // ---- what the model thought ----
        using (var g = JsonDocument.Parse("{\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"생각 중\"},{\"text\":\"본문\"}]}}]}")) Check(AiClient.Thoughts("Gemini", g.RootElement) == "생각 중", "Gemini's thought parts are its thoughts");
        using (var d = JsonDocument.Parse("{\"choices\":[{\"message\":{\"content\":\"본문\",\"reasoning_content\":\"추론\"}}]}")) Check(AiClient.Thoughts("Chat Completions", d.RootElement) == "추론", "A reasoning field is read");
        using (var none = JsonDocument.Parse("{\"choices\":[{\"message\":{\"content\":\"본문\"}}]}")) Check(AiClient.Thoughts("Chat Completions", none.RootElement) == "", "No thoughts, nothing");
        string sse = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"먼저 \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"reasoning_content\":\"생각\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"이야기\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        string thought = ""; string text = SseReplies.Read(new MemoryStream(Encoding.UTF8.GetBytes(sse)), "Chat Completions", _ => { }, CancellationToken.None, null, t => thought = t).GetAwaiter().GetResult();
        Check(text == "이야기" && thought == "먼저 생각", "Thoughts arrive apart from the text while streaming");

        // ---- the typeface the novel is read in ----
        string bookFont = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets", "Fonts", "RIDIBatang.otf"));
        if (File.Exists(bookFont))
        {
            var family = FontAssets.Get(bookFont); var face = family.GetTypefaces().First(); bool found = face.TryGetGlyphTypeface(out var glyphs);
            Check(!ReferenceEquals(family, FontAssets.Standard) && found && glyphs.FontUri.AbsolutePath.EndsWith("RIDIBatang.otf", StringComparison.OrdinalIgnoreCase), "The novel's typeface is read from its file: " + family.Source + " -> " + (found ? glyphs.FontUri.ToString() : "nothing"));
        }

        // ---- the novel's settings and rooms ----
        string home = Path.Combine(root, "novel"); var book = new NovelBook(home); var turn = new ChatTurn("지시", "본문", DateTimeOffset.Now);
        Check(book.State.SystemPrompt && !book.State.Squash && !book.State.MergeRoles && book.State.Model == "", "A new novel sends system messages as they are, unjoined, with the connection's model");
        Check(book.State.Prompts.Parts.Count > 0 && book.State.MaxOutput == 4000 && book.State.Stream && !book.State.ShowThinking && NovelBook.Page("a", "") == "novel:a" && NovelBook.Page("a", "b") == "novel:a+b" && NovelBook.Page("a", "a") == "novel:a", "A new novel starts with the standard prompt and its own limits");
        book.State.Prompts = set; book.State.MaxContext = 1; book.State.Parameters = sampling.Parameters; book.Keep("novel:a", turn, "  생각  "); book.Keep("novel:a", turn with { At = turn.At.AddMinutes(1) }, " "); OrderedStore.FlushAsync().GetAwaiter().GetResult();
        var again = new NovelBook(home);
        Check(again.State.Prompts.Name == "테스트 프리셋" && again.State.Prompts.Parts.Count == 8 && again.State.MaxContext == 2000 && again.State.Parameters.Temperature == 1.2 && again.Thought("novel:a", turn) == "생각" && again.State.Thoughts.Count == 1, "The novel's settings and what the model thought are kept");
        Check(StageMode.Kind("novel:a") == StageMode.Novel && StageMode.Kind("novel:a+b#12345678") == StageMode.Novel && StageMode.Kind("pair:a+b") == StageMode.Pair, "The novel is a kind of page of its own");
        return count;
    }
}
