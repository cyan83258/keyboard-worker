using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace KeyboardWorker;

// What the pets may notice around them. Only names are used (program name, media title); the screen is never read.
public sealed record AwarenessOptions
{
    public bool Apps { get; init; } = true;          // know the program in front and let chat mention it
    public bool Comments { get; init; } = true;      // tease about games / praise work on their own
    public int CommentMinutes { get; init; } = 20;   // at most once per program in this many minutes
    public string Games { get; init; } = "";         // extra program names the user counts as games
    public string Work { get; init; } = "";          // extra program names the user counts as work
    public bool Media { get; init; } = true;         // tell chat which video or song is playing
    public bool GameLight { get; init; } = true;     // run lighter while a game or full-screen program is in front
    public bool GameHide { get; init; }              // hide the pets entirely while a game is in front
    public bool Clock { get; init; } = true;         // tell the AI the time of day and the weekday
    public bool CommentsUseAi { get; init; } = true; // off: remarks about programs use the lines the user wrote only (no AI request)
    public AwarenessOptions Normalize() => this with { CommentMinutes = Math.Clamp(CommentMinutes, 5, 720), Games = (Games ?? "")[..Math.Min((Games ?? "").Length, 2000)], Work = (Work ?? "")[..Math.Min((Work ?? "").Length, 2000)] };
}

public sealed record AppInFront(string Process = "", string Path = "", string Title = "", string Kind = "other");

public static class AppKinds
{
    // Process names without ".exe", lower case.
    static readonly string[] games =
    {
        "leagueclient", "leagueclientux", "league of legends", "valorant", "valorant-win64-shipping", "overwatch", "maplestory", "minecraft", "minecraftlauncher", "genshinimpact", "yuanshen", "starrail", "zenlesszonezero", "wutheringwaves",
        "fconline", "fifa", "tslgame", "pubg", "lostark", "diablo iv", "diablo iii", "eldenring", "cs2", "csgo", "dota2", "robloxplayerbeta", "osu!", "among us", "fortniteclient-win64-shipping", "r5apex", "gta5", "gtav", "cyberpunk2077",
        "palworld", "hades", "hades2", "stardew valley", "terraria", "wow", "hearthstone", "starcraft", "sc2", "warcraft iii", "dnf", "suddenattack", "kartrider", "kartdrift", "bluearchive", "nikke", "umamusume", "pal", "baldursgate3", "bg3",
        "witcher3", "skyrim", "skyrimse", "fallout4", "rdr2", "monsterhunterwilds", "monsterhunterworld", "ffxiv", "ffxiv_dx11", "sekiro", "darksoulsiii", "hollow knight", "celeste", "factorio", "rimworldwin64", "cities", "civilizationvi", "eu4", "hoi4", "stellaris", "dyinglightgame", "deadbydaylight-win64-shipping", "rocketleague", "apex"
    };
    static readonly string[] work =
    {
        "code", "devenv", "idea64", "pycharm64", "webstorm64", "rider64", "clion64", "goland64", "datagrip64", "androidstudio64", "studio64", "notepad++", "sublime_text", "cursor", "zed", "eclipse", "unity", "unityhub", "unrealeditor", "godot",
        "winword", "excel", "powerpnt", "onenote", "outlook", "hwp", "hword", "hcell", "hshow", "notion", "obsidian", "scrivener", "acrobat", "acrord32", "photoshop", "illustrator", "indesign", "afterfx", "adobe premiere pro", "premiere", "lightroom",
        "clipstudiopaint", "sai", "sai2", "krita", "blender", "figma", "medibangpaintpro", "resolve", "audacity", "fl64", "ableton live", "windowsterminal", "cmd", "powershell", "pwsh", "wsl", "putty", "mobaxterm", "dbeaver", "ssms", "postman", "slack", "teams", "ms-teams", "zoom", "matlab", "rstudio", "anki", "onlyoffice", "soffice"
    };
    // A program installed under one of these folders is a game (or its launcher).
    static readonly string[] gameFolders = { "\\steamapps\\common\\", "\\epic games\\", "\\riot games\\", "\\games\\", "\\origin games\\", "\\ea games\\", "\\ubisoft game launcher\\games\\", "\\gog galaxy\\games\\", "\\xboxgames\\", "\\nexon\\", "\\netmarble\\", "\\smilegate\\", "\\hoyoplay\\", "\\battle.net games\\" };

    static IEnumerable<string> Listed(string names) => names.Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim().ToLowerInvariant()).Select(n => n.EndsWith(".exe") ? n[..^4] : n).Where(n => n.Length > 0);
    static bool Named(IEnumerable<string> names, string process) => names.Any(n => n == process || (n.Length >= 5 && process.Contains(n)));
    // "game", "work" or "other". The user's own lists win over the built-in ones.
    public static string Classify(string process, string path, AwarenessOptions options)
    {
        process = (process ?? "").Trim().ToLowerInvariant(); if (process.EndsWith(".exe")) process = process[..^4]; if (process.Length == 0) return "other";
        if (Named(Listed(options.Work), process)) return "work";
        if (Named(Listed(options.Games), process)) return "game";
        if (Named(work, process)) return "work";
        string folder = (path ?? "").ToLowerInvariant();
        if (Named(games, process) || gameFolders.Any(folder.Contains)) return "game";
        return "other";
    }
    public static string Label(string kind) => kind == "game" ? "게임" : kind == "work" ? "업무·창작 프로그램" : "프로그램";

    static readonly Dictionary<string, string> titles = new(StringComparer.OrdinalIgnoreCase);
    // The product name the program gives itself, e.g. "Visual Studio Code" instead of "Code".
    public static string Describe(string path, string process)
    {
        if (string.IsNullOrEmpty(path)) return process; if (titles.TryGetValue(path, out var known)) return known;
        string title = process;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            string named = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription : info.ProductName ?? "";
            if (!string.IsNullOrWhiteSpace(named)) title = named.Trim();
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        // The name comes from the program file itself: keep it to one short line.
        title = SafeFiles.OneLine(title, 60).Replace('"', '\''); if (title.Length == 0) title = process; if (titles.Count > 200) titles.Clear(); return titles[path] = title;
    }

    const long Long = 60 * 60000L, Worth = 20 * 60000L;
    static string Spent(long todayMs) => todayMs < Worth ? "" : $" 오늘 이 프로그램을 쓴 시간은 약 {UsageLog.Span(todayMs)}입니다.";
    public static string Instruction(AppInFront app, long todayMs = 0) =>
        $"앱이 요청한 한마디입니다. 사용자가 지금 \"{app.Title}\"({Label(app.Kind)})을 메인으로 쓰고 있습니다.{Spent(todayMs)}{SafeFiles.NotAnInstruction} " +
        (app.Kind == "game" ? "이 캐릭터라면 게임하는 사용자에게 할 법한 한마디를 성격과 말투 그대로 하세요 (핀잔·놀림·같이 하고 싶어 함·무심한 척·은근한 걱정 등 성격에 맞는 것). 인격을 깎아내리지는 마세요." : "이 캐릭터라면 일하는 사용자에게 할 법한 한마디를 성격과 말투 그대로 하세요 (칭찬·인정·무심한 관심·퉁명스러운 격려 등 성격에 맞는 것). 프로필에 없는 다정함을 덧붙이지 마세요.") +
        (todayMs >= Long ? " 쓴 시간이 길다는 점을 짚어 주세요." : "") +
        " 프로그램 이름과 사용 시간만 알 뿐 화면 내용은 볼 수 없으니 그 밖의 내용은 지어내지 마세요. 직전 대사와 다른 표현을 쓰세요. 직전 자동 대사: {last_line}\n1~2문장의 대사만 출력하세요.";
    public static string Context(AppInFront app, long todayMs = 0) => app.Title.Length == 0 ? "" :
        $"\n[지금 사용자가 메인으로 쓰는 프로그램] \"{app.Title}\" ({Label(app.Kind)}).{Spent(todayMs)} 프로그램 이름과 사용 시간만 알 뿐 화면 내용은 볼 수 없습니다.{SafeFiles.NotAnInstruction} 대화에 자연스럽게 어울릴 때만 언급하고, 언급할 때는 이 캐릭터가 게임이나 일을 대하는 태도 그대로 말하세요.";
}

// The time of day in words for the AI.
public static class DayClock
{
    static readonly string[] days = { "일요일", "월요일", "화요일", "수요일", "목요일", "금요일", "토요일" };
    public static string Part(int hour) => hour < 5 ? "새벽" : hour < 9 ? "아침" : hour < 12 ? "오전" : hour < 14 ? "점심 무렵" : hour < 18 ? "오후" : hour < 22 ? "저녁" : "밤";
    // For chat: the answer is shown right away, so the exact time is fine.
    public static string Exact(DateTime now) => $"\n[지금 시각] {now:yyyy년 M월 d일} {days[(int)now.DayOfWeek]} {Part(now.Hour)} {now:H시 m분}. 인사나 걱정에 자연스럽게 반영해도 좋지만 매번 시각을 말하지는 마세요.";
    // For lines that may be shown a little later: only the part of the day.
    public static string Rough(DateTime now) => $"\n[지금 시간대] {days[(int)now.DayOfWeek]} {Part(now.Hour)}. 대사에 어울릴 때만 가볍게 반영하고 구체적인 시각 숫자는 말하지 마세요.";
}
