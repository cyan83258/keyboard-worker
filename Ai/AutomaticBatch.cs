using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

public sealed class AutomaticBatch
{
    // One queue of prepared lines per kind of line and per speaking pet, so two pets taking turns do not throw each other's lines away.
    readonly Dictionary<(bool Nudge, string Speaker), (string Key, Queue<string> Lines)> batches = new();
    public async Task<PetReply> Get(ChatPreferences preferences, ChatTurn[] history, bool nudge, long idle, string last, CancellationToken token, AiClient? client = null, bool expressions = false, string speaker = "")
    {
        if (!preferences.Automatic.Batch) return await AutomaticGenerator.Generate(preferences, history, nudge, idle, last, token, client, expressions);
        string key = JsonSerializer.Serialize(preferences.Profile) + preferences.Current.Format + preferences.Current.Endpoint + preferences.Current.Model + preferences.Automatic.BatchSize + (preferences.Automatic.UseConversation ? JsonSerializer.Serialize(history) : "");
        if (batches.TryGetValue((nudge, speaker), out var cached) && cached.Key == key && cached.Lines.Count > 0) return PetMood.Parse(cached.Lines.Dequeue());
        var p = preferences.Snapshot();
        p.UsageKind = nudge ? "AutoNudgeBatch" : "AutoRandomBatch";
        p.AutoContinue = false;
        p.Streaming = false;
        p.RequestTimeoutSeconds = Math.Min(p.RequestTimeoutSeconds, 45);
        p.MaxOutputTokens = AutomaticGenerator.OutputTokens * 2;
        string prompt = PromptTemplates.Expand(nudge ? p.Profile.Prompts.Nudge : p.Profile.Prompts.Random, new Dictionary<string, string> { ["idle_seconds"] = idle.ToString(), ["last_line"] = last.Length == 0 ? "없음" : last });
        if (expressions) prompt += PetMood.LineInstruction;
        prompt += $"\n서로 다른 짧은 대사 {p.Automatic.BatchSize}개를 미리 생성합니다. 나중에 나눠 표시하므로 구체적인 현재 시간 숫자는 말하지 마세요. JSON만 출력: {{\"lines\":[\"대사1\",\"대사2\"]}}";
        string reply = await (client ?? new()).Send(p, p.Automatic.UseConversation ? history : Array.Empty<ChatTurn>(), prompt, token);
        using var doc = AdventureAi.Json(reply);
        var lines = doc.RootElement.GetProperty("lines").EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!.Trim()).Where(v => v.Length > 0).Take(Math.Clamp(p.Automatic.BatchSize, 2, 8)).Select(v => v.Length > 600 ? v[..597] + "…" : v).ToArray();
        if (lines.Length < 2) throw new InvalidOperationException("대사 묶음 형식이 올바르지 않습니다. 자동 대사 묶음을 끄거나 프롬프트를 확인해 주세요.");
        var queue = new Queue<string>(lines); batches[(nudge, speaker)] = (key, queue); token.ThrowIfCancellationRequested(); return PetMood.Parse(queue.Dequeue());
    }
    public void Clear() => batches.Clear();
}
