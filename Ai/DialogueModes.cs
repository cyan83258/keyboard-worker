using System.Collections.Generic;

namespace KeyboardWorker;

public static class DialogueModes
{
    public static readonly IReadOnlyDictionary<string, string> Rules = new Dictionary<string, string>
    {
        ["잡담"] = "캐릭터와 편하게 수다 떠는 모드. 자연스럽게 현재 대화를 이어가세요.",
        ["응원"] = "응원 모드. 사용자가 힘을 얻도록 돕되 표현은 캐릭터의 성격과 말투를 그대로 따르세요 (다정한 캐릭터는 다정하게, 퉁명스러운 캐릭터는 퉁명스럽게 등을 떠미는 식으로). 평가하거나 압박하지는 마세요.",
        ["아이디어 회의"] = "아이디어 회의 모드. 캐릭터 말투로 구체적인 아이디어와 선택지를 제안하고 함께 다듬으세요.",
        ["역할극"] = "역할극 모드. 캐릭터 세계관 속 작은 장면을 이어가되 사용자의 행동이나 대사를 대신 결정하지 마세요.",
        ["작업 회고"] = "작업 회고 모드. 사용자가 직접 알려준 작업을 바탕으로 잘된 점·다음 할 일·쉬어갈 점을 함께 돌아보세요. 화면을 보았다고 꾸미지 마세요."
    };
}
