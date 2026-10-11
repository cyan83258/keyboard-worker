using System;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

public sealed class PetConversation
{
    readonly ChatRepository repository;
    readonly AiClient client;
    public bool Busy { get; private set; }
    public PetConversation(ChatRepository repository, AiClient? client = null) { this.repository = repository; this.client = client ?? new(); }
    public async Task<(ChatTurn Turn, string Mood)> Send(string text, bool quick, bool expressions, CancellationToken cancellation, Action<string>? progress = null, string characterState = "", Persona? target = null, string targetKey = "primary")
    {
        if (Busy) throw new InvalidOperationException("이전 답변을 기다리거나 먼저 생성을 중단해 주세요.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 6000) throw new InvalidOperationException("메시지는 1~6000자로 입력해 주세요.");
        Busy = true;
        try
        {
            var settings = ChatRepository.Clone(repository.Preferences);
            if (target != null) { settings.Profile = target.Snapshot(); settings.UsageKind = "ChatSecondary"; }
            settings.Profile.Extra += "\n[대화 모드]\n" + DialogueModes.Rules[settings.Mode];
            settings.Profile.Extra += characterState;
            if (expressions) settings.Profile.Extra += PetMood.Instruction;
            if (quick) settings.Profile.Extra += "\n" + settings.Profile.Prompts.Quick;
            string answer = await client.Send(settings, repository.Context(targetKey), text.Trim(), cancellation, progress);
            cancellation.ThrowIfCancellationRequested(); var reply = PetMood.Parse(answer);
            if (reply.Text.Length == 0) throw new InvalidOperationException("대사 없이 표정만 도착했습니다. 다시 시도해 주세요.");
            var turn = new ChatTurn(text.Trim(), reply.Text, DateTimeOffset.Now, settings.Profile.Name, targetKey, settings.Mode); repository.Add(turn); return (turn, reply.Mood);
        }
        finally { Busy = false; }
    }
}
