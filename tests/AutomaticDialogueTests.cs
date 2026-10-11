using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

public static class AutomaticDialogueTests
{
    sealed class Handler : HttpMessageHandler
    {
        public string Request = "";
        public int Calls;
        public string Finish = "STOP";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); Calls++; Request = await request.Content!.ReadAsStringAsync(cancellation);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = "옆에서 응원할게!" } } }, finishReason = Finish } } }), Encoding.UTF8, "application/json") };
        }
    }
    public static int Run(string root)
    {
        int count = 0; void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        var options = new AutomaticOptions { AiNudge = true, RandomLines = true, RandomMinMinutes = 1, RandomMaxMinutes = 2, CooldownSeconds = 60, HourlyLimit = 2 };
        var clock = new AutomaticClock((min, max) => min); clock.Observe(options, 0, true);
        Check(!clock.RandomReady(59999) && clock.RandomReady(60000), "Random line starts after configured interval without startup burst");
        Check(clock.Reserve(false, options, 60000) && !clock.Reserve(true, options, 61000), "Nudge and random share minimum call interval");
        Check(clock.Reserve(true, options, 120000) && !clock.Reserve(false, options, 180000), "Shared rolling hourly call limit enforced");
        Check(clock.Count(3660000) == 1 && clock.Reserve(false, options, 3660000), "Hourly quota frees individual attempts after sixty minutes");
        clock.Failed(3660000); Check(!clock.Reserve(true, options, 3720000) && clock.Reserve(true, options, 3960000), "Failure backs off automatic requests for five minutes");
        var pauseClock = new AutomaticClock((min, max) => min); pauseClock.Observe(options, 0, true); pauseClock.Observe(options, 30000, false);
        Check(!pauseClock.RandomReady(120000) && !pauseClock.Reserve(true, options, 120000), "Paused state prevents both automatic request kinds");
        pauseClock.Observe(options, 150000, true); Check(!pauseClock.RandomReady(209999) && pauseClock.RandomReady(210000), "Resume gives a fresh random interval");
        var fairClock = new AutomaticClock((min, max) => min); fairClock.Observe(options, 0, true); fairClock.Reserve(true, options, 0);
        Check(fairClock.RandomReady(60000) && fairClock.Reserve(false, options, 60000), "Repeated nudge does not postpone scheduled random dialogue");
        var off = new AutomaticOptions(); var offClock = new AutomaticClock(); offClock.Observe(off, 0, true);
        Check(!offClock.Reserve(true, off, 9999999) && !offClock.RandomReady(9999999), "Both automatic AI features default off");
        var invalid = new AutomaticOptions { RandomMinMinutes = 0, RandomMaxMinutes = -1, CooldownSeconds = 0, HourlyLimit = 0 }.Normalize();
        Check(invalid.RandomMinMinutes == 1 && invalid.RandomMaxMinutes == 1 && invalid.CooldownSeconds == 60 && invalid.HourlyLimit == 1, "Invalid schedule cannot create an unbounded API loop");
        var persona = new Persona { Name = "별이", Speech = "{name} 그대로", Prompts = new() { Profile = "{name} / {speech} / {chat_rules}", Chat = "수정한 대화 규칙", Nudge = "입력 쉼 {idle_seconds}초 / 지난 대사 {last_line}", Random = "랜덤 프롬프트 {last_line}" } };
        Check(CharacterPrompt.Build(persona, false) == "별이 / {name} 그대로 / 수정한 대화 규칙" && CharacterPrompt.Build(persona) == "별이 / {name} 그대로 / 수정한 대화 규칙" + CharacterPrompt.OwnVoice, "Edited profile and chat prompts expanded without recursively interpreting profile content; the own-voice rule follows them");
        var store = new PresetStore(Path.Combine(root, "automatic-presets")); var settings = new Preferences(); var preset = store.Add("별이", settings, persona);
        store.SavePromptsAsync(preset.Id, persona.Prompts with { Nudge = "캐릭터 전용 재촉" }).GetAwaiter().GetResult();
        var restored = new PresetStore(Path.Combine(root, "automatic-presets")).Items[0].Prepare(settings, new ChatPreferences());
        Check(restored.Chat.Profile.Prompts.Nudge == "캐릭터 전용 재촉" && restored.Chat.Profile.Prompts.Chat == "수정한 대화 규칙", "Edited automatic and conversation prompts follow character presets and reload");
        var chat = new ChatPreferences { Profile = persona, MaxOutputTokens = 4000, Automatic = options }; chat.Normalize();
        var history = new[] { new ChatTurn("이전 메시지", "이전 답변", DateTimeOffset.Now) };
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var reply = AutomaticGenerator.Generate(chat, history, true, 77, "지난 한마디", CancellationToken.None, new AiClient(http)).GetAwaiter().GetResult();
        using (var body = JsonDocument.Parse(handler.Request))
        {
            Check(reply.Text == "옆에서 응원할게!" && body.RootElement.GetProperty("contents").GetArrayLength() == 1, "Automatic generation uses no chat history unless enabled");
            Check(body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString() == "입력 쉼 77초 / 지난 대사 지난 한마디", "Edited nudge prompt receives idle time and previous line");
            Check(body.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString()!.Contains("수정한 대화 규칙") && body.RootElement.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32() == AutomaticGenerator.OutputTokens, "Automatic requests use character profile and bounded token budget");
        }
        chat.Automatic = options with { UseConversation = true }; AutomaticGenerator.Generate(chat, history, false, 0, "지난 한마디", CancellationToken.None, new AiClient(http)).GetAwaiter().GetResult();
        using (var body = JsonDocument.Parse(handler.Request)) Check(body.RootElement.GetProperty("contents").GetArrayLength() == 3 && body.RootElement.GetProperty("contents")[2].GetProperty("parts")[0].GetProperty("text").GetString() == "랜덤 프롬프트 지난 한마디", "Optional recent context and edited random prompt passed together");
        Check(chat.MaxOutputTokens == 4000 && chat.AutoContinue && chat.Streaming, "Automatic request limits do not modify manual chat settings");
        handler.Finish = "MAX_TOKENS"; int before = handler.Calls; bool failed = false;
        try { AutomaticGenerator.Generate(chat, history, true, 77, "", CancellationToken.None, new AiClient(http)).GetAwaiter().GetResult(); } catch (ReplyLimitException) { failed = true; }
        Check(failed && handler.Calls == before + 1, "Failed automatic dialogue never creates continuation calls");
        var repo = new ChatRepository(Path.Combine(root, "automatic-history")); repo.ApplyAsync(chat).GetAwaiter().GetResult(); int turns = repo.Turns.Count; handler.Finish = "STOP";
        var reloadedChat = new ChatRepository(Path.Combine(root, "automatic-history"));
        Check(reloadedChat.Preferences.Automatic.AiNudge && reloadedChat.Preferences.Automatic.RandomLines && reloadedChat.Preferences.Profile.Prompts.Chat == "수정한 대화 규칙", "Automatic options and editable prompts survive committed preferences reload");
        AutomaticGenerator.Generate(repo.Preferences, repo.Context(), false, 0, "", CancellationToken.None, new AiClient(http)).GetAwaiter().GetResult();
        Check(repo.Turns.Count == turns, "Automatic lines never populate user chat history");
        return count;
    }
}
