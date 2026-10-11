using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

public static class AdventureAi
{
    public static JsonDocument Json(string text) { text = text.Trim(); if (text.StartsWith("```")) { text = text[(text.IndexOf('\n') + 1)..]; if (text.EndsWith("```")) text = text[..^3]; } return JsonDocument.Parse(text); }
    public static async Task<StoryQuest> Quest(ChatPreferences preferences, string id, AdventureOptions options, CancellationToken token, AiClient? client = null, bool automatic = true)
    {
        var p = preferences.Snapshot();
        p.UsageKind = automatic ? "AutoQuest" : "Quest";
        p.AutoContinue = false;
        p.Streaming = false;
        p.MaxOutputTokens = Math.Min(options.OutputTokens, 2048);
        p.RequestTimeoutSeconds = 60;
        string reply = await (client ?? new()).Send(p, Array.Empty<ChatTurn>(), options.Prompt + "\n지금은 이야기의 도입 퀘스트를 만듭니다. 실제 키보드 활동 5~30분을 목표로, JSON만 반환: {\"title\":\"퀘스트 이름\",\"premise\":\"허구 모험의 도입 2~4문장\",\"minutes\":15}", token);
        using var json = Json(reply); var r = json.RootElement;
        return new() { CharacterId = id, Title = r.GetProperty("title").GetString() ?? "작은 모험", Premise = r.GetProperty("premise").GetString() ?? "", Minutes = Math.Clamp(r.GetProperty("minutes").GetInt32(), 5, 30) };
    }
    public static async Task<(string Title, string Body)> Story(ChatPreferences preferences, StoryQuest quest, AdventureOptions options, CancellationToken token, AiClient? client = null, bool automatic = true)
    {
        var p = preferences.Snapshot();
        p.UsageKind = automatic ? "AutoStory" : "Story";
        p.Profile.Length = "이번에는 캐릭터 모험의 완결 이야기를 600~1500자로 작성합니다. 대화용 짧은 답변 길이와 구분합니다.";
        p.AutoContinue = false;
        p.Streaming = false;
        p.MaxOutputTokens = options.OutputTokens;
        p.RequestTimeoutSeconds = 90;
        string prompt = options.Prompt + "\n완료한 허구 퀘스트: " + quest.Title + "\n도입: " + quest.Premise + "\n캐릭터가 겪은 모험의 완결 이야기 600~1500자를 써 주세요. 물건 보상은 앱이 별도로 결정합니다. JSON만 반환: {\"title\":\"제목\",\"body\":\"줄바꿈을 포함한 이야기 본문\"}";
        string reply = await (client ?? new()).Send(p, Array.Empty<ChatTurn>(), prompt, token); using var json = Json(reply);
        string title = json.RootElement.GetProperty("title").GetString() ?? "작은 모험", body = json.RootElement.GetProperty("body").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(body)) throw new InvalidOperationException("이야기 본문이 없습니다."); return (title, body);
    }
}
