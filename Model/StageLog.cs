using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KeyboardWorker;

// How the characters talk on their pages: length, room for the answer, and the prompt written for these talks.
public sealed class StageTalkOptions
{
    public static readonly (string Name, string Guide)[] Lengths =
    {
        ("매우 짧게", "한 문장. 꼭 필요한 말만 합니다."), ("짧게", "1~2문장."), ("중간", "2~4문장. 필요하면 짧은 지문을 곁들입니다."),
        ("길게", "한두 문단(5~8문장). 생각과 감정, 주변 묘사를 충분히 담습니다."), ("매우 길게", "여러 문단. 장면을 그리듯 대사와 지문, 속마음을 풍부하게 씁니다.")
    };
    public const string StandardPrompt = """
        지금은 {name}와(과) 사용자({user_name})가 단둘이 마주 보고 이야기하는 장면입니다. 데스크톱 펫의 짧은 한마디가 아니라, 이 캐릭터와 실제로 함께 있는 것 같은 깊이 있는 대화를 합니다.
        - 말하는 사람은 {name} 한 명뿐입니다. 다른 캐릭터의 대사나 사용자의 대사·행동을 대신 쓰지 않고, 대사 앞에 이름을 붙이지 않습니다.
        - 프로필의 성격·말투·가치관·세계관을 끝까지 지킵니다. 사용자에게 맞춰 주려고 성격을 무르게 만들지 말고, 이 캐릭터가 실제로 가질 법한 의견·취향·고집·농담·침묵을 보여 줍니다. 동의하지 않을 때는 동의하지 않습니다.
        - 사용자의 말을 되풀이하거나 무난한 맞장구로 끝내지 않습니다. 자기 생각·기억·경험·세계관 속 이야기를 꺼내 대화를 한 걸음 더 나아가게 하고, 가끔은 먼저 묻고, 화제를 바꾸고, 놀리고, 속내를 내비칩니다.
        - 표정·몸짓·시선·속마음은 (괄호) 안에 짧게 쓰고, 대사는 괄호 밖에 씁니다. 괄호는 한 답변에 한두 번이면 충분합니다.
        - 지난 대화의 흐름과 감정을 이어 가고, 매번 인사하거나 자기소개하지 않습니다.
        - 화면·파일·실제 작업은 볼 수 없으며 보았다고 꾸미지 않습니다.
        - 답변 길이: {length}
        """;
    public string Length { get; set; } = "중간";
    public int MaxTokens { get; set; } = 2048;      // room for the answer (models that think first use part of it)
    public int ContextTurns { get; set; } = 12;     // how many earlier exchanges the character is shown
    public string Prompt { get; set; } = StandardPrompt;
    public static string Guide(string length) => Lengths.FirstOrDefault(l => l.Name == length).Guide ?? Lengths[2].Guide;
    public StageTalkOptions Snapshot() => (StageTalkOptions)MemberwiseClone();
    public void Normalize()
    {
        if (Lengths.All(l => l.Name != Length)) Length = "중간";
        MaxTokens = Math.Clamp(MaxTokens, 256, 16000); ContextTurns = Math.Clamp(ContextTurns, 0, 60);
        Prompt = string.IsNullOrWhiteSpace(Prompt) ? StandardPrompt : Prompt[..Math.Min(Prompt.Length, 10000)];
    }
}

// The talks held on the character pages (stage-chat.json), by character and page mode. They are kept apart from the
// ordinary chat with the desktop pets: neither is shown to the model in the other.
public sealed class StageLog
{
    public const int Keep = 300, MaxAnswer = 8000;
    readonly string path; readonly Dictionary<string, List<ChatTurn>> threads;
    public event Action? Changed;
    public StageLog(string? root = null)
    {
        path = Path.Combine(root ?? Storage.Root, "stage-chat.json");
        threads = Storage.ReadJson<Dictionary<string, List<ChatTurn>>>(path, 128L * 1024 * 1024) ?? new();
        foreach (var key in threads.Keys.ToArray()) threads[key] = (threads[key] ?? new()).Where(t => t != null && t.User != null && t.Assistant != null).TakeLast(Keep).ToList();
    }
    public IReadOnlyList<ChatTurn> Turns(string key) => threads.TryGetValue(key, out var turns) ? turns : Array.Empty<ChatTurn>();
    public void Add(string key, ChatTurn turn)
    {
        if (!threads.TryGetValue(key, out var turns)) threads[key] = turns = new();
        turns.Add(turn with { User = turn.User[..Math.Min(turn.User.Length, 6000)], Assistant = turn.Assistant[..Math.Min(turn.Assistant.Length, MaxAnswer)] });
        if (turns.Count > Keep) turns.RemoveRange(0, turns.Count - Keep);
        Save();
    }
    public void Clear(string key) { if (threads.Remove(key)) Save(); }
    public int Count => threads.Values.Sum(t => t.Count);
    void Save() { OrderedStore.SaveJson(path, threads.ToDictionary(p => p.Key, p => p.Value.ToArray())); Changed?.Invoke(); }
}

// A way of talking on a character page: the layout it uses and what it adds to the prompt. Two are built in; mod files add more.
public sealed record StageMode(string Id, string Name, string Layout, string Prompt, string Length = "", string Source = "")
{
    public const string Stage = "stage", Messenger = "messenger";
    public static readonly StageMode[] BuiltIn =
    {
        new("stage", "마주 보기", Stage, ""),
        new("sms", "문자 메시지", Messenger, "\n[문자 메시지 모드]\n지금은 마주 보고 있지 않고, 휴대폰 메신저로 문자를 주고받고 있습니다. 실제 문자처럼 짧은 메시지 1~3개로 답하고, 메시지 사이는 줄바꿈으로 나눕니다. 행동 지문이나 (괄호)는 쓰지 않습니다. 이모티콘·말줄임표·오타·답장 속도 같은 문자 습관은 이 캐릭터가 실제로 그럴 법한 만큼만 드러냅니다. 위의 '답변 길이'보다 이 규칙이 우선합니다.")
    };
    public static IReadOnlyList<StageMode> All => BuiltIn.Concat(ModStore.Current.PageModes).ToArray();
    public static StageMode Find(string? id) => All.FirstOrDefault(m => m.Id == id) ?? BuiltIn[0];
    // The key of this mode's own thread of talk with a character.
    public string Thread(string characterId) => Id == BuiltIn[0].Id ? characterId : characterId + "|" + Id;
}
