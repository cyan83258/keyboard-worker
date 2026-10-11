using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

public static class AutomaticGenerator
{
    // Room for a short line even when the model spends part of its output on reasoning. Only tokens actually used are counted.
    public const int OutputTokens = 2048;
    public static async Task<PetReply> Generate(ChatPreferences preferences, ChatTurn[] history, bool nudge, long idleSeconds, string lastLine, CancellationToken cancellation, AiClient? client = null, bool expressions = false)
    {
        var settings = preferences.Snapshot();
        settings.UsageKind = nudge ? "AutoNudge" : "AutoRandom";
        settings.Streaming = false;
        settings.AutoContinue = false;
        settings.RequestTimeoutSeconds = Math.Min(settings.RequestTimeoutSeconds, 45);
        settings.MaxOutputTokens = OutputTokens;
        string instruction = PromptTemplates.Expand(nudge ? settings.Profile.Prompts.Nudge : settings.Profile.Prompts.Random, new Dictionary<string, string> { ["idle_seconds"] = Math.Max(0, idleSeconds).ToString(), ["last_line"] = lastLine.Length == 0 ? "없음" : lastLine });
        if (expressions) instruction += PetMood.LineInstruction;
        var answer = await (client ?? new AiClient()).Send(settings, settings.Automatic.UseConversation ? history : Array.Empty<ChatTurn>(), instruction, cancellation);
        cancellation.ThrowIfCancellationRequested(); var reply = PetMood.Parse(answer);
        if (string.IsNullOrWhiteSpace(reply.Text)) throw new InvalidOperationException("자동 대사 내용이 없습니다.");
        return reply with { Text = reply.Text.Length > 600 ? reply.Text[..597] + "…" : reply.Text };
    }
}
