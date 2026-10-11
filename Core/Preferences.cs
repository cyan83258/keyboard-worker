using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace KeyboardWorker;

public sealed class Preferences
{
    public double Left { get; set; } = -9999;
    public double Top { get; set; } = -9999;
    public int Size { get; set; } = 180;
    public bool Topmost { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool Locked { get; set; }
    public bool MouseActivity { get; set; }
    public bool HideFullscreen { get; set; }
    public bool VisibilityPolicyUpdated { get; set; }
    public int IdleSeconds { get; set; } = 60;
    public int RepeatSeconds { get; set; } = 60;
    public int BubbleSeconds { get; set; } = 5;
    public bool Repeat { get; set; } = true;
    public string Lines { get; set; } = "잠깐, 손이 멈췄는데?\n우리 한 줄만 더 해볼까?\n생각 중이면 천천히 해도 돼.";
    public string PatLines { get; set; } = "헤헤, 고마워 ♡\n손이 따뜻하네. 잠깐 같이 쉬자!\n오늘도 옆에 있을게 ☆";
    public bool Sound { get; set; } = true;
    public int Volume { get; set; } = 40;
    public string SoundPath { get; set; } = "";
    public List<SoundPreset> SoundPresets { get; set; } = new();
    public string SkinPath { get; set; } = "";
    public Dictionary<string, string> ImagePaths { get; set; } = new();
    public Dictionary<string, ImageAdjustment> ImageAdjustments { get; set; } = new();
    public bool AdvancedImages { get; set; }
    public bool FlipImages { get; set; }
    public string Theme { get; set; } = "strawberry";
    public AwarenessOptions Awareness { get; set; } = new();
    // Where the user dragged each speech bubble, relative to its pet. Null means automatic placement.
    public BubbleSpot? BubbleSpot { get; set; }
    public BubbleSpot? CompanionBubbleSpot { get; set; }
    public DesktopOptions Desktop { get; set; } = new();
    public string ActivePresetId { get; set; } = "";
    public DuoOptions Duo { get; set; } = new();
    public bool MousePetting { get; set; } = true;
    public bool PettingHearts { get; set; } = true;
    public bool LowPower { get; set; }
    public MusicOptions Music { get; set; } = new();
    // Lines by situation for the current pet (see LineBook), its voice clips, and when to say a line without AI.
    public Dictionary<string, string> LineBook { get; set; } = new();
    public Dictionary<string, string> VoicePaths { get; set; } = new();
    public VoiceOptions Voice { get; set; } = new();
    public bool LocalRandom { get; set; } = true;
    public int LocalRandomMinMinutes { get; set; } = 15;
    public int LocalRandomMaxMinutes { get; set; } = 30;
    public bool Greetings { get; set; } = true;
    // The welcome guide has been shown (or this was already an existing installation).
    public bool WelcomeSeen { get; set; }
    // Fonts for the pets' lines: a TTF / OTF file copied into the settings folder (empty = the standard font). The bubble's size is Desktop.BubbleFont.
    public string BubbleFontPath { get; set; } = "";
    public string StageFontPath { get; set; } = "";
    public int StageFontSize { get; set; } = 16;
    // One line the user wants to keep in sight, shown under the pet.
    public string Todo { get; set; } = "";
    public bool ShowTodo { get; set; } = true;
    public bool TodoToAi { get; set; }
    // Upkeep of the settings folder and automatic backups (see Housekeeping).
    public TidyOptions Tidy { get; set; } = new();
    // Sets of on/off choices to switch between; null until the first start gives the starter set.
    public List<ModeProfile>? SavedModes { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public List<ModeProfile> Modes => SavedModes ??= ModeProfile.Starters();
    public string ActiveMode { get; set; } = "";
    public Preferences Snapshot()
    {
        var copy = (Preferences)MemberwiseClone();
        copy.SoundPresets = new(SoundPresets);
        copy.ImagePaths = new(ImagePaths);
        copy.ImageAdjustments = new(ImageAdjustments);
        copy.LineBook = new(LineBook);
        copy.VoicePaths = new(VoicePaths);
        copy.Desktop = Desktop.Snapshot();
        copy.SavedModes = SavedModes?.Select(m => m.Snapshot()).ToList();
        return copy;
    }
    public void Normalize()
    {
        Topmost = true; Duo = (Duo ?? new()).Normalize();
        BubbleFontPath = SafeFiles.LocalOnly(BubbleFontPath); StageFontPath = SafeFiles.LocalOnly(StageFontPath); StageFontSize = Math.Clamp(StageFontSize, FontAssets.MinSize, FontAssets.MaxSize);
        Todo = SafeFiles.OneLine(Todo, 120); Tidy = (Tidy ?? new()).Normalize(); ActiveMode ??= "";
        SavedModes = (SavedModes ?? ModeProfile.Starters()).Where(m => m != null && !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Name)).Take(12).ToList();
        foreach (var mode in SavedModes) { mode.Name = mode.Name.Trim(); mode.Name = mode.Name[..Math.Min(mode.Name.Length, 30)]; mode.Values ??= new(); }
        LineBook = KeyboardWorker.LineBook.Normalize(LineBook); VoicePaths = VoiceSlots.Normalize(VoicePaths); Voice = (Voice ?? new()).Normalize();
        LocalRandomMinMinutes = Math.Clamp(LocalRandomMinMinutes, 1, 1440); LocalRandomMaxMinutes = Math.Clamp(LocalRandomMaxMinutes, LocalRandomMinMinutes, 1440);
        Music = (Music ?? new()).Normalize();
        if (!VisibilityPolicyUpdated) { HideFullscreen = false; VisibilityPolicyUpdated = true; }
        Size = Math.Clamp(Size, 100, 400);
        IdleSeconds = Math.Clamp(IdleSeconds, 5, 86400);
        RepeatSeconds = Math.Clamp(RepeatSeconds, 5, 86400);
        BubbleSeconds = Math.Clamp(BubbleSeconds, 1, 60);
        Volume = Math.Clamp(Volume, 0, 100);
        if (BubbleSpot?.Valid == false) BubbleSpot = null; if (CompanionBubbleSpot?.Valid == false) CompanionBubbleSpot = null;
        if (!Pastel.Themes.Any(t => t.Id == Theme)) Theme = "strawberry";
        Awareness = (Awareness ?? new()).Normalize();
        if (!double.IsFinite(Left)) Left = -9999;
        if (!double.IsFinite(Top)) Top = -9999;
        Lines ??= "";
        PatLines ??= "";
        // Pictures and sounds are copied into the settings folder when registered, so a saved path is always a local file.
        SoundPath = SafeFiles.LocalOnly(SoundPath); SkinPath = SafeFiles.LocalOnly(SkinPath);
        if (ImagePaths != null) foreach (var key in ImagePaths.Where(p => SafeFiles.IsNetworkPath(p.Value)).Select(p => p.Key).ToArray()) ImagePaths.Remove(key);
        SoundPresets?.RemoveAll(p => p != null && SafeFiles.IsNetworkPath(p.Path));
        SoundPresets ??= new();
        SoundPresets = SoundPresets.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id) && !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Path)).DistinctBy(p => p.Id).Take(50).Select(p => p with { Name = p.Name[..Math.Min(p.Name.Length, 80)] }).ToList();
        SkinPath ??= "";
        ImagePaths ??= new();
        ImageAdjustments ??= new();
        foreach (var key in new List<string>(ImageAdjustments.Keys)) ImageAdjustments[key] = (ImageAdjustments[key] ?? new()).Normalize();
        Desktop ??= new(); Desktop.Normalize();
        if (ImagePaths.Count == 0 && SkinPath.Length > 0)
            foreach (var key in ImageAssets.BasicKeys) { string file = Path.Combine(SkinPath, key + ".png"); if (File.Exists(file)) ImagePaths[key] = file; }
    }
}

public static class Storage
{
    public static string Root { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyboardWorker");
    internal static void UseTestRoot(string root) => Root = Path.GetFullPath(root);
    static string FilePath => Path.Combine(Root, "settings.json");
    // The very first start on this PC begins with sample lines, so the pet has something to say without AI. Existing settings are left as they are.
    public static Preferences Load() { _ = ModStore.Current;   // mods may add themes, which must be known before the saved theme is checked
        var p = ReadJson<Preferences>(FilePath) ?? new() { LineBook = new(KeyboardWorker.LineBook.Samples) }; p.Normalize(); return p; }
    // Reads one of the settings files. A missing file is normal (first run) and gives null. A file that exists but cannot
    // be read also gives null so the app can start with defaults - but it is first copied to "<name>.broken-<date>" and the
    // problem is written to the error log, because the defaults will be saved over the original.
    public static T? ReadJson<T>(string path, long maxBytes = 64L * 1024 * 1024) where T : class
    {
        FileInfo info;
        try { info = new FileInfo(path); if (!info.Exists) return null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
        try
        {
            if (info.Length > maxBytes) { ErrorLog.Write("설정 파일을 읽지 않음: " + info.Name, $"파일이 너무 큽니다 ({info.Length / 1048576.0:0.0}MB). 기본값으로 시작합니다."); return null; }
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            string kept = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd");
            try { File.Copy(path, kept, true); } catch (Exception copy) when (copy is IOException or UnauthorizedAccessException) { kept = "(보관하지 못함)"; }
            ErrorLog.Write("설정 파일을 읽지 못함: " + info.Name, e.Message + "\r\n기본값으로 시작합니다. 원본 보관: " + kept);
            return null;
        }
    }
    public static void Save(Preferences p)
    {
        OrderedStore.SaveJson(FilePath, p.Snapshot());
    }
    public static Task SaveAsync(Preferences p) => OrderedStore.SaveJsonAsync(FilePath, p.Snapshot());
}
