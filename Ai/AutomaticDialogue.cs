using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KeyboardWorker;

public sealed record PromptTemplates
{
    public PromptTemplates Normalize()
    {
        var defaults = new PromptTemplates();
        string Safe(string? value, string fallback) => (value ?? fallback)[..Math.Min((value ?? fallback).Length, 10000)];
        // Prompts saved before the wording put the character's own voice first are brought up to date sentence by sentence,
        // so whatever else the user changed in them stays.
        string Renew(string text, string fallback) { text = Safe(text, fallback); foreach (var (old, now) in Renewed) text = text.Replace(old, now); return text; }
        return this with { Profile = Renew(Profile, defaults.Profile), Chat = Renew(Chat, defaults.Chat), Nudge = Renew(Nudge, defaults.Nudge), Random = Renew(Random, defaults.Random), Quick = Safe(Quick, defaults.Quick) };
    }
    static readonly (string Old, string Now)[] Renewed =
    {
        ("당신은 사용자의 데스크톱 펫 '{name}'으로 대화한다. 아래 프로필을 일관되게 반영하되, 사용자의 현재 요청과 감정을 먼저 살핀다.", "당신은 사용자의 데스크톱 펫 '{name}'이다. 아래 프로필의 성격과 말투가 가장 우선이며, 어떤 상황에서도 이 캐릭터답게 말한다. 사용자의 말에는 이 캐릭터가 할 법한 방식으로 반응한다."),
        ("- 사용자가 힘들어하면 생산성 재촉보다 공감과 편안한 휴식을 우선한다. 평소에는 가벼운 응원을 한다.", "- 사용자가 힘들어하면 재촉하지 않는다. 걱정이나 위로도 이 캐릭터의 성격과 말투로 표현한다 (무뚝뚝한 캐릭터는 무뚝뚝하게, 오만한 캐릭터는 오만하게). 프로필에 없는 다정함이나 응원을 덧붙이지 않는다."),
        ("캐릭터의 성격·말투·관계에 맞게 부담 없는 재촉이나 응원 대사 1~2문장만 말하세요. 생각하거나 쉬는 중일 수 있으니 꾸짖거나 죄책감을 주지 마세요.", "캐릭터의 성격·말투·관계에 맞는 재촉 대사 1~2문장만 말하세요. 말의 온도(다정함·퉁명스러움·오만함·장난기)는 프로필을 그대로 따르고, 프로필에 없는 응원이나 애교를 덧붙이지 마세요. 사용자는 생각하거나 쉬는 중일 수 있으니 인격을 깎아내리거나 죄책감을 주는 말은 피하세요."),
        ("답장을 요구하지 않는 짧은 혼잣말·소소한 이야기·다정한 응원 중 하나를 캐릭터 설정과 말투에 맞춰 1~2문장으로 표현하세요.", "답장을 요구하지 않는 짧은 혼잣말·소소한 이야기·이 캐릭터다운 한마디 중 하나를 캐릭터 설정과 말투 그대로 1~2문장으로 표현하세요. 프로필에 없는 다정함이나 응원을 덧붙이지 마세요.")
    };
    public string Profile { get; init; } = """
    당신은 사용자의 데스크톱 펫 '{name}'이다. 아래 프로필의 성격과 말투가 가장 우선이며, 어떤 상황에서도 이 캐릭터답게 말한다. 사용자의 말에는 이 캐릭터가 할 법한 방식으로 반응한다.
    [정체성] {identity}
    [배경과 취향] {background}
    [성격] {personality}
    [말투와 표현] {speech}
    [사용자 호칭] {user_name}
    [관계] {relationship}
    [답변 길이] {length}
    [말투 예시 — 현재 대화로 오인하지 말 것] {examples}
    [피할 표현] {boundaries}
    [사용자가 직접 작성한 기억 메모] {user_notes}
    [추가 설정] {extra}
    대화 진행 규칙:
    {chat_rules}
    """;
    public string Chat { get; init; } = """
    - 제공된 최근 대화의 주제와 감정을 이어서 답하고, 매번 처음 만난 듯 인사하거나 자기소개하지 않는다.
    - 앞서 말한 내용을 불필요하게 반복하지 말고 지금 말한 내용에 구체적으로 반응한다.
    - 대화를 이어갈 질문은 도움이 될 때 한 개만 한다. 모든 답변을 질문으로 끝내지 않는다.
    - 기억 범위는 제공된 대화와 기억 메모뿐이다. 삭제된 과거 대화를 기억한다고 꾸미지 않는다. 부족하면 자연스럽게 물어본다.
    - 화면, 파일, 입력 문자열, 실제 작업 상태를 볼 수 없으며, 직접 수행하지 않은 행동을 수행했다고 말하지 않는다.
    - 놀이와 역할극은 캐릭터에 맞게 이어간다. 현실의 능력이나 사실을 질문받으면 정직하게 답한다.
    - 사용자가 힘들어하면 재촉하지 않는다. 걱정이나 위로도 이 캐릭터의 성격과 말투로 표현한다 (무뚝뚝한 캐릭터는 무뚝뚝하게, 오만한 캐릭터는 오만하게). 프로필에 없는 다정함이나 응원을 덧붙이지 않는다.
    - 평문으로 답한다. 요청하지 않은 긴 목록이나 형식적인 보고서를 피한다.
    """;
    public string Nudge { get; init; } = "키보드 입력이 약 {idle_seconds}초 동안 없어서 앱이 요청한 자동 한마디입니다. 캐릭터의 성격·말투·관계에 맞는 재촉 대사 1~2문장만 말하세요. 말의 온도(다정함·퉁명스러움·오만함·장난기)는 프로필을 그대로 따르고, 프로필에 없는 응원이나 애교를 덧붙이지 마세요. 사용자는 생각하거나 쉬는 중일 수 있으니 인격을 깎아내리거나 죄책감을 주는 말은 피하세요. 실제 화면이나 작업 내용을 본 것처럼 말하지 마세요. 직전 대사와 다른 표현을 쓰세요. 직전 자동 대사: {last_line}\n대사만 짧고 자연스럽게 끝까지 출력하세요.";
    public string Random { get; init; } = "앱이 요청한 랜덤 한마디입니다. 답장을 요구하지 않는 짧은 혼잣말·소소한 이야기·이 캐릭터다운 한마디 중 하나를 캐릭터 설정과 말투 그대로 1~2문장으로 표현하세요. 프로필에 없는 다정함이나 응원을 덧붙이지 마세요. 매번 작업을 재촉하지 말고 실제 화면이나 입력 내용을 본 것처럼 말하지 마세요. 직전 대사와 소재를 반복하지 마세요. 직전 자동 대사: {last_line}\n대사만 짧게 출력하세요.";
    public string Quick { get; init; } = "지금은 사용자가 작업 중 보내는 짧은 한마디에 답하는 상황입니다. 캐릭터의 말투를 유지하면서 1~3개의 짧은 문장으로 자연스럽게 반응하세요. 자세히 요청할 때만 길게 답하세요. 문장을 끝까지 마무리하세요.";
    public static string Expand(string template, IReadOnlyDictionary<string, string> values) => Regex.Replace(template, @"\{([a-z_]+)\}", m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
public sealed record AutomaticOptions
{
    public bool AiNudge { get; init; }
    public bool RandomLines { get; init; }
    public int RandomMinMinutes { get; init; } = 10;
    public int RandomMaxMinutes { get; init; } = 20;
    public int CooldownSeconds { get; init; } = 120;
    public int HourlyLimit { get; init; } = 6;
    public bool UseConversation { get; init; }
    public bool FallbackNudge { get; init; } = true;
    public bool Batch { get; init; } = true;
    public int BatchSize { get; init; } = 4;
    public AutomaticOptions Normalize()
    {
        int min = Math.Clamp(RandomMinMinutes, 1, 1440);
        return this with { RandomMinMinutes = min, RandomMaxMinutes = Math.Clamp(RandomMaxMinutes, min, 1440), CooldownSeconds = Math.Clamp(CooldownSeconds, 60, 3600), HourlyLimit = Math.Clamp(HourlyLimit, 1, 60), BatchSize = Math.Clamp(BatchSize, 2, 8) };
    }
}
public sealed class AutomaticClock
{
    readonly Queue<long> requests = new();
    readonly Func<int, int, int> random;
    long nextRandom, lastRequest = long.MinValue / 2, retryAfter;
    AutomaticOptions? observed;
    bool allowed;
    public AutomaticClock(Func<int, int, int>? random = null) { this.random = random ?? ((min, max) => Random.Shared.Next(min, max)); }
    public void Observe(AutomaticOptions options, long now, bool eligible)
    {
        if (observed != options || allowed != eligible) { observed = options; allowed = eligible; nextRandom = eligible && options.RandomLines ? Next(options, now) : 0; }
    }
    long Next(AutomaticOptions o, long now) => now + random(o.RandomMinMinutes * 60, o.RandomMaxMinutes * 60 + 1) * 1000L;
    public int Count(long now) { while (requests.Count > 0 && now - requests.Peek() >= 3600000) requests.Dequeue(); return requests.Count; }
    public bool RandomReady(long now) => allowed && nextRandom > 0 && now >= nextRandom;
    public bool CanReserve(AutomaticOptions options, long now) => allowed && now >= retryAfter && now - lastRequest >= options.CooldownSeconds * 1000L && Count(now) < options.HourlyLimit;
    public bool Reserve(bool nudge, AutomaticOptions options, long now)
    {
        if ((nudge ? !options.AiNudge : !options.RandomLines || !RandomReady(now)) || !CanReserve(options, now)) return false;
        requests.Enqueue(now); lastRequest = now; if (!nudge) nextRandom = options.RandomLines ? Next(options, now) : 0; return true;
    }
    public void Failed(long now) => retryAfter = now + 300000;
    // When the next random line is due, in words.
    public string Outlook(AutomaticOptions options, long now)
    {
        if (!allowed || nextRandom == 0) return "대기 중";
        if (Count(now) >= options.HourlyLimit) return "최근 1시간 호출 한도에 도달해 쉬는 중";
        long wait = Math.Max(nextRandom, Math.Max(retryAfter, lastRequest + options.CooldownSeconds * 1000L)) - now;
        return wait <= 0 ? "곧" : wait < 90000 ? $"약 {wait / 1000 + 1}초 후" : $"약 {wait / 60000 + 1}분 후";
    }
}
