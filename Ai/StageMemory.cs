using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

// The memory of a chat room: the AI folds the talk that is not yet in the summary into it, so that what happened long ago
// is still known when the exchanges themselves are no longer shown to the model.
public static class StageMemory
{
    public const int MaxTurns = 40, MaxTurnLength = 1200;
    public const string Instruction = """
        당신은 롤플레이 대화의 기록 담당입니다. '기존 요약'과 '새 대화'를 합쳐 하나의 새 요약을 한국어로 씁니다.
        - 앞으로 대화를 이어 가는 데 필요한 것만 남깁니다: 일어난 사건, 정해진 사실·약속·호칭, 인물들의 관계와 감정의 변화, 지금 있는 장소와 상황.
        - 기존 요약의 중요한 내용은 지키고 새 대화의 내용으로 고쳐 씁니다. 바뀐 사실은 새것으로 바꾸고, 사소한 잡담과 말투·문장 표현은 버립니다.
        - 대화에 없는 일을 지어내지 않습니다.
        - '- '로 시작하는 짧은 평서문 15개 이하, 전체 1200자 이내로 씁니다. 요약만 출력하고 인사나 설명은 붙이지 않습니다.
        """;
    // The exchanges of a room that are not in its summary yet.
    public static ChatTurn[] Pending(IReadOnlyList<ChatTurn> turns, StageRoom room) => turns.Where(t => t.At > room.SummarizedAt).ToArray();
    public static bool Due(IReadOnlyList<ChatTurn> turns, StageRoom room, int every) => room.AutoSummary && Pending(turns, room).Length >= every;
    public static string Request(string summary, IReadOnlyList<ChatTurn> turns)
    {
        static string Cut(string text) { text = text.Trim(); return text.Length > MaxTurnLength ? text[..MaxTurnLength] + "…" : text; }
        var text = new StringBuilder("[기존 요약]\n").Append(summary.Trim().Length > 0 ? summary.Trim() : "(아직 없음)").Append("\n\n[새 대화]\n");
        foreach (var turn in turns.TakeLast(MaxTurns))
        {
            if (turn.User.Trim().Length > 0) text.Append("사용자: ").Append(Cut(turn.User)).Append('\n');
            // A scene for two is already written as "이름: 대사" lines.
            text.Append(turn.CharacterName.Length > 0 && !turn.CharacterName.Contains(" · ") ? turn.CharacterName + ": " : "").Append(Cut(turn.Assistant)).Append("\n\n");
        }
        return text.ToString().TrimEnd();
    }
    // The settings a summary is asked with: the named connection when there is one of that name, else the one in use.
    public static ChatPreferences Settings(ChatPreferences preferences, string connection)
    {
        var settings = ChatRepository.Clone(preferences); int named = connection.Length == 0 ? -1 : settings.Connections.FindIndex(c => string.Equals(c.Name, connection, StringComparison.OrdinalIgnoreCase));
        if (named >= 0) settings.Selected = named;
        settings.UsageKind = "ChatStageMemory"; settings.Streaming = false; settings.MaxOutputTokens = Math.Max(settings.MaxOutputTokens, 2048);
        settings.Profile = new Persona { Prompts = new PromptTemplates { Profile = "{extra}" }, Extra = Instruction };
        return settings;
    }
    public static async Task<string> Summarize(ChatPreferences preferences, string connection, string summary, IReadOnlyList<ChatTurn> turns, CancellationToken cancellation, AiClient? client = null)
    {
        string answer = (await (client ?? new AiClient()).Send(Settings(preferences, connection), Array.Empty<ChatTurn>(), Request(summary, turns), cancellation)).Trim();
        if (answer.StartsWith("```")) answer = answer.Trim('`').Trim();
        if (answer.Length == 0) throw new InvalidOperationException("요약이 비어 있습니다. 다시 시도해 주세요.");
        return answer[..Math.Min(answer.Length, StageRoom.MaxSummary)];
    }
}
