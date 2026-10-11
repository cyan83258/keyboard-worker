using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyboardWorker;

// Lines the user writes for a pet, by situation, so the pet has things to say without any AI.
// The nudge and petting lines keep their old homes (Preferences.Lines / PatLines); every other situation lives in the book.
public static class LineBook
{
    public sealed record Situation(string Key, string Group, string Title, string Help);
    public const string Nudge = "nudge", Pat = "pat";
    public static readonly Situation[] Situations =
    {
        new(Nudge, "기본", "재촉", "입력이 한동안 없을 때 건네는 말."),
        new(Pat, "기본", "쓰다듬기", "마우스로 쓰다듬어 줄 때."),
        new("random", "기본", "랜덤 한마디", "가끔 먼저 건네는 혼잣말·응원. AI 랜덤 한마디가 꺼져 있거나 쉴 때 쓰입니다."),
        new("greet-morning", "인사", "아침 인사", "아침(5~12시)에 처음 만났을 때."),
        new("greet-day", "인사", "낮 인사", "낮(12~18시)에 처음 만났을 때."),
        new("greet-evening", "인사", "저녁 인사", "저녁(18~22시)에 처음 만났을 때."),
        new("greet-night", "인사", "밤·새벽 인사", "밤늦게(22~5시) 만났을 때. 그만 자라는 말도 좋아요."),
        new("work", "프로그램", "업무 프로그램을 쓸 때", "{app} = 프로그램 이름."),
        new("game", "프로그램", "게임을 할 때", "{app} = 게임 이름."),
        new("long-work", "프로그램", "업무가 1시간 넘었을 때", "{app}, {time} = 오늘 쓴 시간."),
        new("long-game", "프로그램", "게임이 1시간 넘었을 때", "{app}, {time} = 오늘 쓴 시간."),
        new("rest-start", "휴식·음악", "휴식을 시작할 때", "휴식 버튼이나 단축키로 쉬기 시작할 때."),
        new("rest-end", "휴식·음악", "휴식이 끝났을 때", "다시 작업으로 돌아올 때."),
        new("music", "휴식·음악", "새 노래·영상이 시작될 때", "{title} = 제목. 음악 감지가 켜져 있어야 해요."),
        new("anniversary", "추억", "기념일", "{event} = 기념일 이름."),
        new("stage-up", "추억", "친밀도 단계가 올랐을 때", "{stage} = 새 단계 이름."),
        new("pair", "두 친구", "둘이 주고받는 대화", "한 줄에 대화 하나. 대사를 / 로 나누면 대표 → 둘째 → 대표 순서로 주고받습니다. 예: 오늘 뭐 해? / 너 구경할 건데. / …저리 가."),
        new("sulk", "두 친구", "토라졌을 때", "다른 친구하고만 이야기해서 서운할 때. {partner} = 다른 친구 이름."),
        new("makeup", "두 친구", "화해했을 때", "서운함이 풀렸을 때.")
    };
    public static Situation? Find(string key) => Situations.FirstOrDefault(s => s.Key == key);
    // Plain sample lines: what a new install starts with, and what "fill the empty ones" in the line book writes. Meant to be rewritten in the pet's own voice.
    public static readonly IReadOnlyDictionary<string, string> Samples = new Dictionary<string, string>
    {
        ["random"] = "오늘도 옆에 있을게.\n물 한 잔 마시고 할까?\n어깨 한 번 쭉 펴 보자.\n[3+] {user}랑 같이 있으니까 시간이 금방 가네.\n[5+] 가끔 이렇게 조용히 같이 있는 게 제일 좋아.",
        ["greet-morning"] = "좋은 아침, {user}! 오늘도 잘 부탁해.\n일찍 왔네. 천천히 시작하자.\n[4+] 아침부터 얼굴 보니까 기분 좋다.",
        ["greet-day"] = "안녕, {user}! 점심은 챙겨 먹었어?\n왔구나. 오후도 같이 힘내자.",
        ["greet-evening"] = "저녁이네. 오늘 하루 어땠어?\n수고했어, {user}. 조금만 더 하고 쉬자.",
        ["greet-night"] = "이 시간까지 깨어 있었어? 너무 무리하지 마.\n밤이 깊었네. 오늘은 여기까지만 할까?\n[4+] {user}, 늦었어. 걱정되니까 얼른 자.",
        ["work"] = "{app} 열었구나. 집중하는 모습 멋져!\n좋아, 그 기세로 가 보자.",
        ["game"] = "{app}…? 일은 다 한 거 맞지?\n한 판만 하고 돌아오는 거야, 약속!",
        ["long-work"] = "{app}에서 벌써 {time}째야. 잠깐 쉬었다 해.\n정말 열심히네. 눈 좀 쉬게 해 주자.",
        ["long-game"] = "{app}만 {time}째인데… 슬슬 그만할 때 아니야?\n재밌는 건 알겠는데 {time}은 좀 길다?",
        ["rest-start"] = "그래, 쉬는 것도 일이야. 푹 쉬어.\n기지개 한 번 켜고 오자!",
        ["rest-end"] = "다 쉬었어? 다시 천천히 시작해 보자.\n어서 와. 기다리고 있었어.",
        ["music"] = "오, 「{title}」! 이 노래 좋다.\n「{title}」 들으면서 하면 일이 잘될 것 같아.",
        ["anniversary"] = "오늘은 {event}이야! 축하해, {user} ♡\n{event}… 같이 맞을 수 있어서 기뻐.",
        ["stage-up"] = "우리 이제 '{stage}'래. 앞으로도 잘 부탁해 ♡\n{user}랑 조금 더 가까워진 것 같아서 기뻐.",
        ["pair"] = "오늘 일 많아 보이네. / 그러게, 우리가 응원해 주자. / 좋아, 힘내라 힘!\n뭐 보고 있어? / {user} 일하는 거. / …나도 같이 볼래.\n졸리다… / 벌써? 정신 차려. / 5분만….",
        ["sulk"] = "…{partner}하고만 얘기하네. 흥.\n나도 여기 있는데.",
        ["makeup"] = "…이번만 봐주는 거야.\n헤헤, 이제 괜찮아. 고마워."
    };
    public const string CommonHelp = "한 줄에 대사 하나씩 적으면 그중에서 무작위로 고릅니다. 비워 두면 그 상황에서는 말하지 않아요 (프로그램 한마디는 기본 대사를 씁니다).\n넣을 수 있는 말: {user} = 내 호칭, {name} = 캐릭터 이름.\n줄 앞에 [3+] 처럼 적으면 친밀도 3단계부터만 나옵니다.";

    // The greeting situation for an hour of the day.
    public static string Greeting(int hour) => hour < 5 || hour >= 22 ? "greet-night" : hour < 12 ? "greet-morning" : hour < 18 ? "greet-day" : "greet-evening";
    // The text for a situation: the two old fields for nudge and petting, the book for the rest.
    public static string Text(IReadOnlyDictionary<string, string>? book, string nudgeLines, string? patLines, string key) =>
        key == Nudge ? nudgeLines ?? "" : key == Pat ? patLines ?? "" : book != null && book.TryGetValue(key, out var text) ? text ?? "" : "";
    // One line for the situation at this stage, or null when none is written. "avoid" keeps the same line from coming twice in a row.
    public static string? Pick(string text, int stage, IReadOnlyDictionary<string, string> values, ref string avoid, Func<int, int>? roll = null)
    {
        var lines = Affection.Available(text.Split('\n'), stage); if (lines.Length == 0) return null;
        int index = (roll ?? Random.Shared.Next)(lines.Length); if (lines.Length > 1 && lines[index] == avoid) index = (index + 1) % lines.Length;
        avoid = lines[index]; return Fill(lines[index], values);
    }
    public static string Fill(string line, IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values) line = line.Replace("{" + pair.Key + "}", pair.Value);
        return line;
    }
    // "A / B / A" as turns; the first speaker is the main pet.
    public static (bool Second, string Text)[] Turns(string line) =>
        line.Split('/').Select(part => part.Trim()).Where(part => part.Length > 0).Select((part, index) => (index % 2 == 1, part)).ToArray();
    public static Dictionary<string, string> Normalize(Dictionary<string, string>? book)
    {
        var clean = new Dictionary<string, string>(); if (book == null) return clean;
        foreach (var pair in book) if (pair.Key != Nudge && pair.Key != Pat && Find(pair.Key) != null && !string.IsNullOrWhiteSpace(pair.Value)) clean[pair.Key] = pair.Value[..Math.Min(pair.Value.Length, 10000)];
        return clean;
    }
}

// Short sound clips a pet plays in certain situations.
public sealed record VoiceOptions
{
    public bool Enabled { get; init; } = true;
    public int Volume { get; init; } = 70;
    public bool ReadAloud { get; init; }   // no clip for the moment: let Windows read the line (text to speech)
    public VoiceOptions Normalize() => this with { Volume = Math.Clamp(Volume, 0, 100) };
}
public static class VoiceSlots
{
    public static readonly (string Key, string Title)[] All =
    {
        ("speak", "말할 때 (기본)"), ("greet", "인사"), ("nudge", "재촉"), ("pat", "쓰다듬기"), ("happy", "기쁨"), ("sad", "슬픔"), ("angry", "화남·핀잔"),
        ("surprised", "놀람"), ("shy", "부끄러움"), ("sleepy", "졸림"), ("caring", "다정함·위로"), ("celebrate", "축하·기념일")
    };
    // The clip for a moment: the one for the situation, else the one for the expression, else the general one.
    public static string Find(IReadOnlyDictionary<string, string>? clips, string situation, string mood)
    {
        if (clips == null) return "";
        foreach (string key in new[] { situation, mood, "speak" }) if (key.Length > 0 && clips.TryGetValue(key, out var path) && !string.IsNullOrWhiteSpace(path)) return path;
        return "";
    }
    public static Dictionary<string, string> Normalize(Dictionary<string, string>? clips)
    {
        var clean = new Dictionary<string, string>(); if (clips == null) return clean;
        foreach (var pair in clips) if (All.Any(s => s.Key == pair.Key) && !string.IsNullOrWhiteSpace(pair.Value) && !SafeFiles.IsNetworkPath(pair.Value)) clean[pair.Key] = pair.Value;
        return clean;
    }
}
