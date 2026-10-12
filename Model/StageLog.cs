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
    // Text messages are another thing altogether: their own prompt, length and room, set in their own window.
    public const string StandardTextPrompt = """
        지금 {name}은(는) 사용자({user_name})와 휴대폰 메신저로 문자를 주고받고 있습니다. 마주 보고 있지 않습니다.
        - 답하는 사람은 {name} 한 명뿐입니다. 사용자의 메시지를 대신 쓰지 않고, 메시지 앞에 이름을 붙이지 않습니다.
        - 실제 문자처럼 짧은 메시지 1~3개로 답하고, 메시지 사이는 줄바꿈으로 나눕니다. 행동 지문이나 (괄호)는 쓰지 않습니다.
        - 프로필의 성격·말투·가치관을 끝까지 지킵니다. 이모티콘·말줄임표·오타·답장 속도 같은 문자 습관은 이 캐릭터가 실제로 그럴 법한 만큼만 드러냅니다.
        - 사용자의 말을 되풀이하거나 무난한 맞장구로 끝내지 않습니다. 가끔은 먼저 묻고, 화제를 바꾸고, 놀리고, 답을 미룹니다.
        - 지난 문자의 흐름을 이어 가고, 매번 인사하지 않습니다. 화면·파일·실제 작업은 볼 수 없으며 보았다고 꾸미지 않습니다.
        - 한 번에 보내는 분량: {length}
        """;
    public static StageTalkOptions Texting() => new() { Length = "짧게", MaxTokens = 1024, ContextTurns = 20, Prompt = StandardTextPrompt };
    public string Length { get; set; } = "중간";
    public int MaxTokens { get; set; } = 2048;      // room for the answer (models that think first use part of it)
    public int ContextTurns { get; set; } = 12;     // how many earlier exchanges the character is shown
    public bool Slang { get; set; } = true;        // text messages only: ㅋㅋ, ^^ and the like may be used
    public int KeepTurns { get; set; } = StageLog.Keep;   // how many exchanges a thread keeps before the oldest are dropped
    public string Prompt { get; set; } = StandardPrompt;
    // The page for two: the two characters talk to each other and the user is not there.
    public const string StandardPairPrompt = """
        지금은 {first_name}와(과) {second_name} 두 사람만 있는 장면입니다. 사용자는 이 자리에 없고, 등장하지도 말을 걸지도 않습니다. 두 사람은 서로에게 말하고 서로에게 반응합니다.
        - 각자의 프로필에 적힌 성격·말투·가치관을 끝까지 지킵니다. 두 사람의 말투가 서로 닮아 가지 않게 하고, 사이가 원래 다정하지 않다면 억지로 다정하게 만들지 않습니다.
        - 두 사람의 관계 설정(호칭, 함께 겪은 일, 지킬 것)을 따릅니다. 서로를 대하는 태도에는 그 관계의 역사가 묻어나야 합니다.
        - 무난한 맞장구를 주고받지 말고, 각자 원하는 것·숨기는 것·신경 쓰이는 것을 가지고 대화를 앞으로 끌고 갑니다. 침묵, 말 돌리기, 장난, 다툼, 화해도 좋습니다.
        - 표정·몸짓·시선·속마음은 (괄호) 안에 짧게 쓰고, 대사는 괄호 밖에 씁니다.
        - 지난 장면의 흐름을 이어 가고, 같은 말을 되풀이하지 않습니다.
        - 이번에는 두 사람이 모두 합쳐 {turns}번 안팎으로 말합니다 (한 사람이 연달아 말해도 됩니다). 한 번의 길이: {length}
        """;
    public string PairPrompt { get; set; } = StandardPairPrompt;
    public static string Guide(string length) => Lengths.FirstOrDefault(l => l.Name == length).Guide ?? Lengths[2].Guide;
    public StageTalkOptions Snapshot() => (StageTalkOptions)MemberwiseClone();
    // standard: the prompt an emptied one goes back to (the page's, or the text messages').
    public void Normalize(string standard = StandardPrompt)
    {
        if (Lengths.All(l => l.Name != Length)) Length = "중간";
        MaxTokens = Math.Clamp(MaxTokens, 256, 16000); ContextTurns = Math.Clamp(ContextTurns, 0, 60); KeepTurns = Math.Clamp(KeepTurns, 30, StageLog.MaxKeep);
        Prompt = string.IsNullOrWhiteSpace(Prompt) ? standard : Prompt[..Math.Min(Prompt.Length, 10000)];
        PairPrompt = string.IsNullOrWhiteSpace(PairPrompt) ? StandardPairPrompt : PairPrompt[..Math.Min(PairPrompt.Length, 10000)];
    }
}

// The talks held on the character pages (stage-chat.json), by character and page mode. They are kept apart from the
// ordinary chat with the desktop pets: neither is shown to the model in the other.
public sealed class StageLog
{
    public const int Keep = 300, MaxKeep = 2000, MaxAnswer = 8000;
    public static readonly int[] Limits = { 30, 50, 100, 200, 300, 500, 1000 };
    // How many exchanges a thread keeps; the oldest go first when one more is added.
    public int Limit { get; set; } = Keep;
    readonly string path; readonly Dictionary<string, List<ChatTurn>> threads;
    public event Action? Changed;
    public StageLog(string? root = null)
    {
        path = Path.Combine(root ?? Storage.Root, "stage-chat.json");
        threads = Storage.ReadJson<Dictionary<string, List<ChatTurn>>>(path, 128L * 1024 * 1024) ?? new();
        foreach (var key in threads.Keys.ToArray()) threads[key] = (threads[key] ?? new()).Where(t => t != null && t.User != null && t.Assistant != null).TakeLast(MaxKeep).ToList();
    }
    public IReadOnlyList<ChatTurn> Turns(string key) => threads.TryGetValue(key, out var turns) ? turns : Array.Empty<ChatTurn>();
    public void Add(string key, ChatTurn turn)
    {
        if (!threads.TryGetValue(key, out var turns)) threads[key] = turns = new();
        turns.Add(Cut(turn));
        if (turns.Count > Limit) turns.RemoveRange(0, turns.Count - Limit);
        Save();
    }
    static ChatTurn Cut(ChatTurn turn) => turn with { User = turn.User[..Math.Min(turn.User.Length, 6000)], Assistant = turn.Assistant[..Math.Min(turn.Assistant.Length, MaxAnswer)] };
    public IReadOnlyList<string> Threads => threads.Keys.ToArray();
    // ---- looking after a thread by hand (the history window) ----
    public bool Replace(string key, ChatTurn old, ChatTurn now)
    {
        if (!threads.TryGetValue(key, out var turns)) return false; int at = turns.IndexOf(old); if (at < 0) return false;
        turns[at] = Cut(now); Save(); return true;
    }
    public bool Remove(string key, ChatTurn turn)
    {
        if (!threads.TryGetValue(key, out var turns) || !turns.Remove(turn)) return false;
        if (turns.Count == 0) threads.Remove(key); Save(); return true;
    }
    // Keeps only the newest exchanges of a thread. Returns how many were dropped.
    public int Trim(string key, int keep)
    {
        if (!threads.TryGetValue(key, out var turns) || turns.Count <= keep) return 0; int dropped = turns.Count - Math.Max(0, keep);
        turns.RemoveRange(0, dropped); if (turns.Count == 0) threads.Remove(key); Save(); return dropped;
    }
    // Adds exchanges read from a file, in order of time; ones already there are left out. Returns how many came in.
    public int Import(string key, IEnumerable<ChatTurn> incoming)
    {
        if (!threads.TryGetValue(key, out var turns)) turns = new(); var known = turns.Select(t => (t.At, t.User)).ToHashSet(); int added = 0;
        foreach (var turn in incoming.Where(t => t != null && t.User != null && t.Assistant != null).Select(Cut)) if (known.Add((turn.At, turn.User))) { turns.Add(turn); added++; }
        if (added == 0) return 0;
        turns = turns.OrderBy(t => t.At).ToList(); if (turns.Count > MaxKeep) turns.RemoveRange(0, turns.Count - MaxKeep);
        threads[key] = turns; Save(); return added;
    }
    public void Clear(string key) { if (threads.Remove(key)) Save(); }
    public int Count => threads.Values.Sum(t => t.Count);
    void Save() { OrderedStore.SaveJson(path, threads.ToDictionary(p => p.Key, p => p.Value.ToArray())); Changed?.Invoke(); }
}

// A way of talking with a character. Layout says which of the two things it is: the character page (Stage: standing picture
// and dialogue box) or text messages (Messenger). The two share nothing - each has its own window, settings, rooms and
// threads. One of each is built in; mod files add more.
public sealed record StageMode(string Id, string Name, string Layout, string Prompt, string Length = "", string Source = "")
{
    public const string Stage = "stage", Messenger = "messenger", Pair = "pair";
    public static readonly StageMode[] BuiltIn = { new("stage", "마주 보기", Stage, ""), new("sms", "문자 메시지", Messenger, "") };
    public static IReadOnlyList<StageMode> All => BuiltIn.Concat(ModStore.Current.PageModes).ToArray();
    public static StageMode[] Of(string layout) => All.Where(m => m.Layout == layout).ToArray();
    // The mode with this id among those of a layout; the built-in one when there is none.
    public static StageMode Find(string? id, string layout = Stage) { var modes = Of(layout); return modes.FirstOrDefault(m => m.Id == id) ?? modes[0]; }
    // The key of this mode's page with a character (its rooms and threads hang on it).
    public string Thread(string characterId) => Id == BuiltIn[0].Id ? characterId : characterId + "|" + Id;
    // Which of the three kinds of page a key or thread belongs to: Stage, Messenger or Pair.
    public static string Kind(string thread)
    {
        string key = StageRooms.Split(thread).Key; if (key.StartsWith("pair:", StringComparison.Ordinal)) return Pair;
        int bar = key.IndexOf('|'); return bar >= 0 && All.FirstOrDefault(m => m.Id == key[(bar + 1)..])?.Layout == Messenger ? Messenger : Stage;
    }
}
