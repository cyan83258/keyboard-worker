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

        // ---- every direction goes as the input of its part, how many parts fit, and the prompt shaped as SillyTavern does ----
        Check(messages.Count(m => m.Role == PromptMessage.User && m.Text.StartsWith("지시 ")) == 5 && !messages.Any(m => m.Text.Contains("다음 전개 지시")), "Every earlier direction is sent as the input of its part");
        Check(NovelPrompts.Kept(set, scene) == 6 && NovelPrompts.Kept(set, scene, limit, 300) == 2, "How many parts go is told as the request is built");
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
        Check(!book.State.Welcomed && !again.State.Welcomed, "The note about the model waits to be read");

        // ---- summaries ----
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var parts = Enumerable.Range(0, 12).Select(i => new ChatTurn("", "부분 " + (i + 1), start.AddMinutes(i))).ToArray();
        var options = NovelSummaries.Normalize(new NovelSummaryOptions { Auto = true, Every = 4, Group = 3, KeepRecent = 2, Context = 1 });
        NovelSummary Over(int a, int b, string text) => new(parts[a].At.UtcTicks, parts[b].At.UtcTicks, text, start);
        var nothing = Array.Empty<NovelSummary>();
        Check(NovelSummaries.Pending(parts, nothing).Length == 12 && NovelSummaries.Ready(options, parts, nothing).Length == 10 && NovelSummaries.Due(options, parts, nothing), "Waiting parts, all but the newest, are due once there are enough");
        Check(!NovelSummaries.Due(new NovelSummaryOptions { Auto = false }, parts, nothing), "Nothing is summarized by itself when that is off");
        var runs = NovelSummaries.Runs(new[] { 0, 1, 2, 3, 4, 7, 8 }, 3);
        Check(runs.Count == 3 && runs[0].SequenceEqual(new[] { 0, 1, 2 }) && runs[1].SequenceEqual(new[] { 3, 4 }) && runs[2].SequenceEqual(new[] { 7, 8 }), "Runs keep neighbours together, at most a group long");
        var first = Over(0, 2, "* 시나리오: 처음"); var second = Over(3, 5, "* 시나리오: 그다음");
        Check(NovelSummaries.Label(first, parts) == "#1~3" && NovelSummaries.Label(Over(4, 4, "x"), parts) == "#5" && NovelSummaries.Label(first, parts.Skip(3).ToArray()) == "(지운 부분)", "Summaries are labelled by the numbers of their parts");
        var summarized = new[] { first, second };
        Check(NovelSummaries.Pending(parts, summarized).Length == 6 && NovelSummaries.Ready(options, parts, summarized).SequenceEqual(new[] { 6, 7, 8, 9 }), "Summarized parts no longer wait");
        var (system, ask) = NovelSummaries.Request(options, parts, new[] { 6, 7 }, summarized, "루카, 미오");
        Check(system.Contains("* 시나리오:") && system.Contains("* 관계:") && !system.Contains("* 감정:") && ask.Contains("[등장인물] 루카, 미오") && ask.Contains("#7\n부분 7") && ask.Contains("#8\n부분 8") && ask.Contains("그다음") && !ask.Contains("처음"), "A request asks for the headings that are on, numbers the parts and shows the summary before them");
        Check(NovelSummaries.Clean("```\n#7~8 요약\n* 시나리오: 끝\n```") == "* 시나리오: 끝", "A fence or a number header the model adds is taken off");
        var (digest, sent) = NovelSummaries.Apply(options, parts, summarized);
        Check(digest.StartsWith("[시나리오 요약]") && digest.IndexOf("### #1~3") < digest.IndexOf("### #4~6") && sent.Length == 6 && sent[0].Assistant == "부분 7", "Summarized parts go as their summaries, the rest as they are");
        Check(NovelSummaries.Apply(options, parts.Take(5).ToArray(), summarized).Story.Length == 2, "The newest parts are sent as they are even when summarized");
        Check(NovelSummaries.Apply(new NovelSummaryOptions { Inject = false }, parts, summarized).Story.Length == 12 && NovelSummaries.Apply(new NovelSummaryOptions { Budget = 500 }, parts, new[] { Over(0, 2, new string('가', 4000)) }).Digest == "", "Summaries are left out when switched off or over the budget");
        var digestScene = NovelPrompts.Build(NovelPrompts.Standard(), new NovelScene(Array.Empty<Persona>(), "나", "", "", "", sent, "", digest), 100000);
        string sentText = string.Join("\n", digestScene.Select(m => m.Text));
        Check(sentText.Contains("### #1~3") && sentText.Contains("부분 7") && !sentText.Contains("부분 2\n"), "The novel's request carries the digest in place of the summarized parts");
        book.Keep("novel:a", first); book.Keep("novel:a", second); book.Keep("novel:a", Over(6, 8, "셋째")); book.Keep("novel:a", Over(1, 2, "겹침"));
        Check(book.SummariesOf("novel:a").Count == 3 && book.SummariesOf("novel:a")[0].Text == "겹침" && !book.SummariesOf("novel:a").Contains(first), "A new summary replaces the ones it overlaps");
        var kept = book.SummariesOf("novel:a")[0]; book.Rewrite("novel:a", kept, "  고침  "); OrderedStore.FlushAsync().GetAwaiter().GetResult();
        var reread = new NovelBook(home);
        Check(reread.SummariesOf("novel:a").Count == 3 && reread.SummariesOf("novel:a")[0].Text == "고침" && reread.Forget("novel:a", reread.SummariesOf("novel:a")[0]) && reread.SummariesOf("novel:a").Count == 2 && reread.SummariesOf("novel:b").Count == 0, "Summaries are kept, edited and forgotten per story");
        reread.Hide("novel:a", parts.Take(3), true); reread.Hide("novel:a", parts.Skip(1).Take(1), false); OrderedStore.FlushAsync().GetAwaiter().GetResult();
        var eyes = new NovelBook(home);
        Check(eyes.IsHidden("novel:a", parts[0]) && !eyes.IsHidden("novel:a", parts[1]) && eyes.IsHidden("novel:a", parts[2]) && !eyes.IsHidden("novel:b", parts[0]), "Closed eyes are kept per story");
        return count;
    }
}
