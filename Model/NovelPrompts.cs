using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeyboardWorker;

// One part of the novel prompt, as in SillyTavern's prompt manager: a message in a role, or a marker (Marker) that stands
// for something the program fills in - the characters, the user's persona, the scene, the story so far. Each can be
// switched on and off and the order is the order they are sent in.
public sealed class NovelPromptPart
{
    public string Identifier { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = PromptMessage.System;
    public string Content { get; set; } = "";
    public bool Marker { get; set; }
    public bool Enabled { get; set; } = true;
    public NovelPromptPart Snapshot() => (NovelPromptPart)MemberwiseClone();
}
public sealed class NovelPromptSet
{
    public string Name { get; set; } = "기본 소설 프롬프트";
    public List<NovelPromptPart> Parts { get; set; } = new();
    // Put in the model's mouth at the end, so it goes on from there (SillyTavern's "assistant prefill"). Empty = none.
    public string Prefill { get; set; } = "";
    public NovelPromptSet Snapshot() => new() { Name = Name, Prefill = Prefill, Parts = Parts.Select(p => p.Snapshot()).ToList() };
}
// What a request for the next part of the story is made of: who is in it, the user, the room's note and memory, the story so far and what to write now.
// Digest: the story's summaries (NovelSummaries.Apply), sent in place of the parts they cover.
public sealed record NovelScene(IReadOnlyList<Persona> Cast, string UserName, string UserNotes, string Note, string Summary, IReadOnlyList<ChatTurn> Story, string Direction, string Digest = "");
// Settings read from a SillyTavern preset besides the prompts (null = the preset says nothing about it).
public sealed record NovelSampling(AiParameters Parameters, int? MaxContext, int? MaxOutput, bool? Stream, bool? ShowThinking);

public static class NovelPrompts
{
    public const int MaxParts = 80, MaxContent = 20000;
    // The markers the program knows and what each stands for.
    public static readonly (string Id, string Name)[] Markers =
    {
        ("charDescription", "캐릭터 설정"), ("charPersonality", "캐릭터 성격"), ("personaDescription", "사용자 페르소나"), ("scenario", "상황 · 작가 노트"),
        ("dialogueExamples", "대사 예시"), ("worldInfoBefore", "세계관 (앞)"), ("worldInfoAfter", "세계관 (뒤)"), ("chatHistory", "지금까지의 이야기")
    };
    public static string MarkerName(string id) => Markers.FirstOrDefault(m => m.Id == id).Name ?? id;
    static NovelPromptPart Mark(string id) => new() { Identifier = id, Name = MarkerName(id), Marker = true };
    static NovelPromptPart Text(string id, string name, string role, string content, bool on = true) => new() { Identifier = id, Name = name, Role = role, Content = content, Enabled = on };
    public static NovelPromptSet Standard() => new()
    {
        Parts = new()
        {
            Text("main", "소설 작성 지시", PromptMessage.System, """
                # 소설 작성
                당신은 {{char}}이(가) 등장하는 연재 소설을 쓰는 작가입니다.
                - 3인칭 소설체로 씁니다. 대사는 큰따옴표로, 묘사와 서술은 문어체로 씁니다.
                - 등장인물은 아래 설정의 성격·말투·관계를 끝까지 지킵니다. 무난하게 다정해지지 않고, 각자의 욕망과 고집으로 움직입니다.
                - 행동·표정·감각·배경으로 감정을 보여 주고, 설명으로 요약하지 않습니다.
                - 앞 장면과 자연스럽게 이어 쓰고, 이미 쓴 문장과 표현을 되풀이하지 않습니다.
                - 사용자의 지시는 다음 장면의 방향으로 반영합니다. 지시가 없으면 이야기를 스스로 한 걸음 진전시킵니다.
                - 장면의 끝에 요약이나 다음 내용 예고를 붙이지 않습니다.
                """),
            Mark("charDescription"), Mark("personaDescription"), Mark("scenario"),
            new() { Identifier = "dialogueExamples", Name = MarkerName("dialogueExamples"), Marker = true, Enabled = false },
            Mark("chatHistory"),
            Text("length", "분량", PromptMessage.User, "[이번에는 1500~3000자 정도의 한 장면을 씁니다. 본문만 쓰고 제목이나 머리말은 붙이지 않습니다.]")
        }
    };
    public static NovelPromptSet Normalize(NovelPromptSet? set)
    {
        if (set == null || set.Parts == null || set.Parts.Count == 0) return Standard();
        set.Name = SafeFiles.OneLine(set.Name, 60) is { Length: > 0 } name ? name : "프롬프트"; set.Prefill = Cut(set.Prefill);
        set.Parts = set.Parts.Where(p => p != null).Take(MaxParts).ToList();
        foreach (var part in set.Parts)
        {
            part.Identifier = SafeFiles.OneLine(part.Identifier, 80) is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N"); part.Name = SafeFiles.OneLine(part.Name, 60) is { Length: > 0 } title ? title : part.Marker ? MarkerName(part.Identifier) : "프롬프트";
            part.Role = part.Role is PromptMessage.User or PromptMessage.Assistant ? part.Role : PromptMessage.System; part.Content = Cut(part.Content);
        }
        return set;
    }
    static string Cut(string? text) { text = (text ?? "").Replace("\r\n", "\n"); return text[..Math.Min(text.Length, MaxContent)]; }

    // ---- SillyTavern ----
    // A SillyTavern chat-completion preset: its prompts in the order of its prompt manager (the order of the user's own
    // list, character 100001, when there is one), each on or off as there; and its sampling settings.
    public static (NovelPromptSet Set, NovelSampling Sampling) ReadSillyTavern(string json, string name)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException) { throw new InvalidOperationException("JSON 형식이 올바르지 않습니다."); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("prompts", out var prompts) || prompts.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("SillyTavern 프롬프트 프리셋(prompts 목록이 있는 JSON)이 아닙니다.");
            var known = new Dictionary<string, NovelPromptPart>();
            foreach (var item in prompts.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || Str(item, "identifier") is not { Length: > 0 } id || known.ContainsKey(id)) continue;
                string content = Str(item, "content") ?? ""; bool marker = Bool(item, "marker") == true || (content.Length == 0 && Markers.Any(m => m.Id == id));
                known[id] = new NovelPromptPart { Identifier = id, Name = Str(item, "name") ?? id, Role = Str(item, "role") ?? PromptMessage.System, Content = content, Marker = marker, Enabled = Bool(item, "enabled") ?? true };
            }
            if (known.Count == 0) throw new InvalidOperationException("프리셋에 프롬프트가 없습니다.");
            var parts = new List<NovelPromptPart>();
            if (root.TryGetProperty("prompt_order", out var orders) && orders.ValueKind == JsonValueKind.Array)
            {
                JsonElement? chosen = null;
                foreach (var order in orders.EnumerateArray())
                    if (order.ValueKind == JsonValueKind.Object && order.TryGetProperty("order", out var list) && list.ValueKind == JsonValueKind.Array && (chosen == null || (order.TryGetProperty("character_id", out var who) && who.ValueKind == JsonValueKind.Number && who.GetInt64() == 100001))) chosen = list;
                if (chosen is { } entries)
                    foreach (var entry in entries.EnumerateArray())
                        if (Str(entry, "identifier") is { } id && known.Remove(id, out var part)) { part.Enabled = Bool(entry, "enabled") ?? true; parts.Add(part); }
            }
            // What the preset has but its order does not use comes after, switched off.
            foreach (var rest in known.Values) { if (parts.Count > 0) rest.Enabled = false; parts.Add(rest); }
            var set = Normalize(new NovelPromptSet { Name = name, Parts = parts, Prefill = Str(root, "assistant_prefill") ?? "" });
            double? Number(string key, double? off = null) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.GetDouble() is var number && number != off ? number : null;
            var parameters = new AiParameters
            {
                Temperature = Number("temperature"), TopP = Number("top_p", 1), TopK = Number("top_k", 0) is double k ? (int)k : null, FrequencyPenalty = Number("frequency_penalty", 0), PresencePenalty = Number("presence_penalty", 0),
                RepetitionPenalty = Number("repetition_penalty", 1), MinP = Number("min_p", 0), TopA = Number("top_a", 0),
                ReasoningEffort = Str(root, "reasoning_effort") switch { "min" or "minimum" => "minimal", "low" => "low", "medium" => "medium", "high" => "high", "max" or "maximum" => "max", _ => "" }
            };
            parameters.Normalize();
            return (set, new NovelSampling(parameters, Number("openai_max_context") is double context ? (int)context : null, Number("openai_max_tokens") is double tokens ? (int)tokens : null, Bool(root, "stream_openai"), Bool(root, "show_thoughts")));
        }
    }
    static string? Str(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    static bool? Bool(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    // ---- making a request ----
    static readonly Regex macro = new(@"\{\{\s*([a-zA-Z_]+)\s*\}\}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    public static string Fill(string text, NovelScene scene)
    {
        string user = scene.UserName.Trim().Length > 0 ? scene.UserName.Trim() : "사용자", all = string.Join(", ", scene.Cast.Select(c => c.Name));
        return macro.Replace(text, m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "char" => scene.Cast.Count > 0 ? scene.Cast[0].Name : "", "user" => user, "group" or "chars" or "charifnotgroup" => all,
            "lastmessage" or "lastchatmessage" or "input" or "lastusermessage" => scene.Direction, "scenario" => scene.Note, "persona" => scene.UserNotes,
            "description" => scene.Cast.Count > 0 ? scene.Cast[0].Identity : "", "personality" => scene.Cast.Count > 0 ? scene.Cast[0].Personality : "",
            "trim" or "noop" => "", _ => m.Value
        });
    }
    // The messages as the API settings want them: system messages sent as the user's (systemPrompt off), runs of system
    // messages joined (squash), runs of any one role joined (merge) - SillyTavern's prompt post-processing.
    public static List<PromptMessage> Shape(IEnumerable<PromptMessage> messages, bool systemPrompt, bool squash, bool merge)
    {
        var shaped = new List<PromptMessage>();
        foreach (var message in messages)
        {
            var m = systemPrompt || message.Role != PromptMessage.System ? message : message with { Role = PromptMessage.User };
            if (shaped.Count > 0 && shaped[^1].Role == m.Role && (merge || (squash && m.Role == PromptMessage.System))) shaped[^1] = shaped[^1] with { Text = shaped[^1].Text + "\n\n" + m.Text };
            else shaped.Add(m);
        }
        return shaped;
    }
    // What the user asked for now, as the last message of the story so far.
    public static string Asked(string direction) => direction.Trim().Length > 0 ? direction.Trim() : "(앞 장면에서 자연스럽게 이어서, 이야기를 한 걸음 진전시키는 다음 장면을 쓰세요.)";
    // The messages of one request: the parts that are on, in order, with the markers filled in. The story so far is cut
    // from its oldest end until the whole fits maxContext with room for maxOutput (0 = no limit).
    public static List<PromptMessage> Build(NovelPromptSet set, NovelScene scene, int maxContext = 0, int maxOutput = 0) => Fit(set, scene, maxContext, maxOutput).Messages;
    // How many of the story's newest parts go in a request (the rest do not fit).
    public static int Kept(NovelPromptSet set, NovelScene scene, int maxContext = 0, int maxOutput = 0) => Fit(set, scene, maxContext, maxOutput).Kept;
    static (List<PromptMessage> Messages, int Kept) Fit(NovelPromptSet set, NovelScene scene, int maxContext, int maxOutput)
    {
        var parts = set.Parts.Where(p => p.Enabled).ToList(); bool storyHere = parts.Any(p => p.Marker && p.Identifier == "chatHistory");
        var story = scene.Story.ToList();
        List<PromptMessage> Make()
        {
            var list = new List<PromptMessage>();
            void Story()
            {
                if (scene.Summary.Trim().Length > 0) list.Add(new(PromptMessage.System, "[지금까지의 줄거리]\n" + scene.Summary.Trim()));
                if (scene.Digest.Trim().Length > 0) list.Add(new(PromptMessage.System, scene.Digest.Trim()));
                foreach (var turn in story) { list.Add(new(PromptMessage.User, Asked(turn.User))); list.Add(new(PromptMessage.Assistant, turn.Assistant)); }
                list.Add(new(PromptMessage.User, Asked(scene.Direction)));
            }
            foreach (var part in parts)
            {
                if (!part.Marker) { string text = Fill(part.Content, scene).Trim(); if (text.Length > 0) list.Add(new(part.Role, text)); continue; }
                switch (part.Identifier)
                {
                    case "charDescription": foreach (var who in scene.Cast) list.Add(new(PromptMessage.System, $"[{who.Name}의 설정]\n" + CharacterPrompt.Build(who, false))); break;
                    case "personaDescription":
                        string user = scene.UserName.Trim(); if (user.Length > 0 || scene.UserNotes.Trim().Length > 0) list.Add(new(PromptMessage.System, "[사용자({{user}})]\n".Replace("{{user}}", user.Length > 0 ? user : "사용자") + (scene.UserNotes.Trim().Length > 0 ? scene.UserNotes.Trim() : "이름: " + user))); break;
                    case "scenario": if (scene.Note.Trim().Length > 0) list.Add(new(PromptMessage.System, "[상황 · 작가 노트]\n" + Fill(scene.Note.Trim(), scene))); break;
                    case "dialogueExamples": foreach (var who in scene.Cast.Where(c => c.Examples.Trim().Length > 0)) list.Add(new(PromptMessage.System, $"[{who.Name}의 대사 예시]\n" + who.Examples.Trim())); break;
                    case "chatHistory": Story(); break;
                }
            }
            if (!storyHere) Story();
            if (Fill(set.Prefill, scene).Trim() is { Length: > 0 } prefill) list.Add(new(PromptMessage.Assistant, prefill));
            return list;
        }
        var messages = Make(); if (maxContext <= 0 || story.Count == 0) return (messages, story.Count);
        // Measured once: everything but the story is fixed, so the story keeps as many of its newest parts as the rest leaves room for.
        static int Cost(string text) => AiRequestOptions.Estimate(text) + 4; int Part(ChatTurn turn) => Cost(Asked(turn.User)) + Cost(turn.Assistant);
        int room = maxContext - maxOutput - (messages.Sum(m => Cost(m.Text)) - story.Sum(Part)), used = 0, keep = 0;
        for (int i = story.Count - 1; i >= 0; i--) { used += Part(story[i]); if (used > room) break; keep++; }
        if (keep < story.Count) { story = story.Skip(story.Count - keep).ToList(); messages = Make(); }
        return (messages, keep);
    }
}
