using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyboardWorker;

// A mod file: one JSON document that adds content of the kinds the program knows. It is data only - nothing in it is ever
// run as code. A file may hold any mix of the lists below; a kind added later is one more list here and one more reader in ModStore.
public sealed class ModFile
{
    public const int Format = 1;
    [JsonPropertyName("format")] public int FormatVersion { get; set; } = Format;
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    // Where the mod was downloaded from (a GitHub address), so it can be fetched again; empty for a file added by hand.
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("themes")] public List<ModTheme>? Themes { get; set; }
    [JsonPropertyName("linePacks")] public List<ModLinePack>? LinePacks { get; set; }
    [JsonPropertyName("profiles")] public List<ModProfile>? Profiles { get; set; }
    [JsonPropertyName("pageModes")] public List<ModPageMode>? PageModes { get; set; }

    static readonly JsonSerializerOptions reading = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    public static readonly JsonSerializerOptions Writing = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public const int MaxBytes = 2 * 1024 * 1024;
    // Reads and checks a mod. Everything is cut to a sane size; what the program does not understand is left out, not guessed at.
    public static ModFile Parse(string json)
    {
        if (json.Length > MaxBytes) throw new InvalidOperationException("모드 파일이 너무 큽니다 (2MB 이하).");
        ModFile? mod; try { mod = JsonSerializer.Deserialize<ModFile>(json, reading); } catch (JsonException e) { throw new InvalidOperationException("모드 파일의 JSON 형식이 올바르지 않습니다. " + e.Message); }
        if (mod == null) throw new InvalidOperationException("모드 파일이 비어 있습니다.");
        if (mod.FormatVersion != Format) throw new InvalidOperationException("이 프로그램이 모르는 모드 형식입니다 (format " + mod.FormatVersion + ").");
        mod.Id = Slug(mod.Id); if (mod.Id.Length == 0) throw new InvalidOperationException("모드에 id가 없습니다 (영문·숫자·-·_ 로 된 이름).");
        mod.Name = Line(mod.Name, 60); if (mod.Name.Length == 0) mod.Name = mod.Id; mod.Author = Line(mod.Author, 60); mod.Version = Line(mod.Version, 20); mod.Description = Line(mod.Description, 400);
        mod.Source = Uri.TryCreate(mod.Source, UriKind.Absolute, out var from) && Downloader.Allowed(from) && mod.Source.Length <= 600 ? mod.Source : "";
        mod.Themes = (mod.Themes ?? new()).Where(t => t != null).Take(10).Select(t => t.Checked()).Where(t => t.Id.Length > 0).DistinctBy(t => t.Id).ToList();
        mod.LinePacks = (mod.LinePacks ?? new()).Where(p => p != null).Take(30).Select(p => p.Checked()).Where(p => p.Id.Length > 0 && p.Lines!.Count > 0).DistinctBy(p => p.Id).ToList();
        mod.Profiles = (mod.Profiles ?? new()).Where(p => p != null).Take(20).Select(p => p.Checked()).Where(p => p.Name.Length > 0 && p.Values!.Count > 0).ToList();
        mod.PageModes = (mod.PageModes ?? new()).Where(m => m != null).Take(20).Select(m => m.Checked()).Where(m => m.Id.Length > 0).DistinctBy(m => m.Id).ToList();
        if (mod.Themes.Count + mod.LinePacks.Count + mod.Profiles.Count + mod.PageModes.Count == 0) throw new InvalidOperationException("모드에 이 프로그램이 쓸 수 있는 내용(themes, linePacks, profiles, pageModes)이 없습니다.");
        return mod;
    }
    [JsonIgnore] public string Summary => string.Join(" · ", new[] { (Themes?.Count ?? 0, "테마"), (LinePacks?.Count ?? 0, "대사 팩"), (Profiles?.Count ?? 0, "상황 프로필"), (PageModes?.Count ?? 0, "페이지 모드") }.Where(p => p.Item1 > 0).Select(p => p.Item2 + " " + p.Item1));
    internal static string Slug(string? value) => new string((value ?? "").Trim().ToLowerInvariant().Where(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_').Take(40).ToArray());
    internal static string Line(string? value, int length) => SafeFiles.OneLine(value, length);
    internal static string Text(string? value, int length) { value = (value ?? "").Replace("\r\n", "\n").Trim(); return value[..Math.Min(value.Length, length)]; }

    // A file to learn the format from: one of each kind.
    public static ModFile Sample() => new()
    {
        Id = "my-first-mod", Name = "나의 첫 모드", Author = "만든 사람", Version = "1.0", Description = "모드 파일 형식을 보여 주는 예시입니다. 필요한 부분만 남기고 고쳐 쓰세요.",
        Themes = new() { new() { Id = "night", Name = "밤하늘팝", AccentHue = 235, SecondHue = 280, Saturation = .85 } },
        LinePacks = new() { new() { Id = "greetings", Name = "인사 모음", Description = "아침과 밤 인사 몇 줄", Price = 0, Lines = new() { ["greet-morning"] = "일어났군.\n오늘은 일찍 왔네.", ["greet-night"] = "아직 안 잤나.\n[4+] 늦었어. 그만 자." } } },
        Profiles = new() { new() { Name = "방송 중", Values = new() { ["typing"] = false, ["voice"] = false, ["badge"] = false, ["todo"] = false, ["ai-random"] = false } } },
        PageModes = new() { new() { Id = "letter", Name = "손편지", Layout = StageMode.Stage, Length = "길게", Prompt = "지금은 마주 보고 말하는 대신, 사용자에게 손편지를 쓰고 있습니다. 편지글의 말투로 쓰고 (괄호) 지문은 쓰지 않습니다." } }
    };
}
// A colour theme derived from the pink original like the built-in ones: the pinks take one hue, the sky blues another.
public sealed class ModTheme
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("accentHue")] public double AccentHue { get; set; }      // 0-360: buttons, headings, outlines
    [JsonPropertyName("secondHue")] public double SecondHue { get; set; } = 200; // 0-360: the second button colour and its text
    [JsonPropertyName("saturation")] public double Saturation { get; set; } = 1; // 0 (grey) to 1.3 (vivid)
    internal ModTheme Checked()
    {
        static double Hue(double value) => double.IsFinite(value) ? (value % 360 + 360) % 360 : 0;
        return new() { Id = ModFile.Slug(Id), Name = ModFile.Line(Name, 20) is { Length: > 0 } name ? name : ModFile.Slug(Id), AccentHue = Hue(AccentHue), SecondHue = Hue(SecondHue), Saturation = double.IsFinite(Saturation) ? Math.Clamp(Saturation, 0, 1.3) : 1 };
    }
}
// Lines for the line book, by situation key (see LineBook.Situations). price 0 = free.
public sealed class ModLinePack
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("price")] public int Price { get; set; }
    [JsonPropertyName("lines")] public Dictionary<string, string>? Lines { get; set; }
    internal ModLinePack Checked() => new()
    {
        Id = ModFile.Slug(Id), Name = ModFile.Line(Name, 40) is { Length: > 0 } name ? name : ModFile.Slug(Id), Description = ModFile.Line(Description, 200), Price = Math.Clamp(Price, 0, 100000),
        Lines = (Lines ?? new()).Where(l => LineBook.Find(l.Key ?? "") != null && !string.IsNullOrWhiteSpace(l.Value)).ToDictionary(l => l.Key, l => ModFile.Text(l.Value, 5000))
    };
}
// A set of on/off choices (see QuickToggles for the keys).
public sealed class ModProfile
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("values")] public Dictionary<string, bool>? Values { get; set; }
    internal ModProfile Checked() => new() { Name = ModFile.Line(Name, 30), Values = (Values ?? new()).Where(v => QuickToggles.Find(v.Key ?? "") != null).ToDictionary(v => v.Key, v => v.Value) };
}
// A way of talking on the character pages: "stage" (standing picture and dialogue box) or "messenger" (text messages), with its own instruction.
public sealed class ModPageMode
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("layout")] public string Layout { get; set; } = StageMode.Stage;
    [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
    [JsonPropertyName("length")] public string Length { get; set; } = "";
    internal ModPageMode Checked() => new()
    {
        Id = ModFile.Slug(Id), Name = ModFile.Line(Name, 30) is { Length: > 0 } name ? name : ModFile.Slug(Id), Layout = Layout == StageMode.Messenger ? StageMode.Messenger : StageMode.Stage,
        Prompt = ModFile.Text(Prompt, 4000), Length = StageTalkOptions.Lengths.Any(l => l.Name == Length) ? Length : ""
    };
}
