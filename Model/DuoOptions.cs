using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace KeyboardWorker;

public sealed record DuoOptions
{
    public bool Enabled { get; init; }
    public string PresetId { get; init; } = "";
    public double Left { get; init; } = -9999;
    public double Top { get; init; } = -9999;
    public bool Interactions { get; init; }
    // With two pets out, nudges and random lines come from either of them instead of always the main one.
    public bool ShareLines { get; init; } = true;
    // How many lines the two exchange in one go (2 to 4).
    public int Turns { get; init; } = 3;
    public int MinMinutes { get; init; } = 10;
    public int MaxMinutes { get; init; } = 20;
    public int CooldownSeconds { get; init; } = 180;
    public int HourlyLimit { get; init; } = 4;
    public string Prompt
    {
        get;
        init;
    } = "두 캐릭터 {first_name}, {second_name}가 책상에서 함께 지내는 짧은 상호작용을 만드세요. 서로의 성격·말투에 맞는 1문장씩, 두 사람의 관계에 어울리는 것(잡담·티격태격·장난·무심한 한마디·은근한 챙김 등)을 주제로 해 주세요. 두 사람 사이가 원래 다정하지 않다면 억지로 다정하게 만들지 마세요. 사용자의 답장을 강요하거나 작업 내용을 봤다고 하지 마세요. 직전 대화와 다르게: {last_line}\nJSON만 출력: {\"first\":\"첫 캐릭터의 대사\",\"second\":\"둘째 캐릭터의 대사\"}";
    public DuoOptions Normalize()
    {
        int min = Math.Clamp(MinMinutes, 1, 1440);
        // The earlier default asked for cheering whatever the two were like; a saved copy of it is brought up to date.
        string prompt = (Prompt ?? new DuoOptions().Prompt).Replace("서로의 성격·말투에 맞는 1문장씩, 장난·응원·소소한 잡담을 주제로 해 주세요.", "서로의 성격·말투에 맞는 1문장씩, 두 사람의 관계에 어울리는 것(잡담·티격태격·장난·무심한 한마디·은근한 챙김 등)을 주제로 해 주세요. 두 사람 사이가 원래 다정하지 않다면 억지로 다정하게 만들지 마세요.");
        return this with
        {
            PresetId = PresetId ?? "",
            Left = double.IsFinite(Left) ? Left : -9999,
            Top = double.IsFinite(Top) ? Top : -9999,
            MinMinutes = min,
            MaxMinutes = Math.Clamp(MaxMinutes, min, 1440),
            CooldownSeconds = Math.Clamp(CooldownSeconds, 60, 3600),
            HourlyLimit = Math.Clamp(HourlyLimit, 1, 60),
            Prompt = prompt[..Math.Min(prompt.Length, 10000)]
        };
    }
    public AutomaticOptions Schedule => new() { RandomLines = Interactions, RandomMinMinutes = MinMinutes, RandomMaxMinutes = MaxMinutes, CooldownSeconds = CooldownSeconds, HourlyLimit = HourlyLimit };
}
