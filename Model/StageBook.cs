using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KeyboardWorker;

// A standing picture of a character for one feeling or situation. The tag is what the user (or the file name) calls it.
public sealed class StageSprite
{
    public string Tag { get; set; } = "";
    public string Path { get; set; } = "";
    public StageSprite Snapshot() => (StageSprite)MemberwiseClone();
}
// A background. Every character page shares the same backgrounds; Group is the folder the user sorts it into.
public sealed class StageBackdrop
{
    public string Title { get; set; } = "";
    public string Path { get; set; } = "";
    public string Group { get; set; } = "";
    public StageBackdrop Snapshot() => (StageBackdrop)MemberwiseClone();
}
// One character's standing pictures.
public sealed class StageSet
{
    public List<StageSprite> Sprites { get; set; } = new();
    // Only read from files written before backgrounds were shared; moved into StageState.Backdrops on loading.
    public List<string>? Backgrounds { get; set; }
    public string? Background { get; set; }
    public StageSet Snapshot() => new() { Sprites = Sprites.Select(s => s.Snapshot()).ToList() };
}
public sealed class StageState
{
    public Dictionary<string, StageSet>? Sets { get; set; }      // by character id
    public List<StageBackdrop> Backdrops { get; set; } = new();
    public string Backdrop { get; set; } = "";                    // path of the one on show; empty = the plain themed backdrop
    public bool AutoBackdrop { get; set; } = true;                // let the AI change the background when the scene changes
    public bool FoldSprites { get; set; }                         // the lists in the picture window are folded away
    public bool FoldBackdrops { get; set; }
    public StageTalkOptions Talk { get; set; } = new();          // length, room and prompt of the talks on the pages
    public string Mode { get; set; } = "";                       // the page mode in use (see StageMode)
    public StageState Snapshot() => new() { Talk = Talk.Snapshot(), Mode = Mode, Sets = (Sets ?? new()).ToDictionary(p => p.Key, p => p.Value.Snapshot()), Backdrops = Backdrops.Select(b => b.Snapshot()).ToList(), Backdrop = Backdrop, AutoBackdrop = AutoBackdrop, FoldSprites = FoldSprites, FoldBackdrops = FoldBackdrops };
}

// The pictures of the character pages (stage.json). The files themselves are copies in the images folder.
public sealed class StageBook
{
    public const int MaxSprites = 40, MaxBackdrops = 60;
    public const string BuiltInGroup = "기본 제공";
    // Backgrounds that come with the program: the pictures in the "backgrounds" folder next to it. A picture's file name is
    // its title ("street_night.jpg" -> "street night") and a subfolder is its group. They are listed after the user's own
    // and cannot be renamed or removed from inside the program.
    public static IReadOnlyList<StageBackdrop> BuiltIn { get; private set; } = Array.Empty<StageBackdrop>();
    static bool builtInRead;
    public static StageBackdrop[] ReadBuiltIn(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return Array.Empty<StageBackdrop>(); string home = Path.GetFullPath(folder);
            return Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories).Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(200)
                .Select(f => new StageBackdrop { Path = f, Title = StageTags.BackdropTitle(f), Group = Path.GetDirectoryName(f)!.Length > home.Length ? SafeFiles.OneLine(Path.GetRelativePath(home, Path.GetDirectoryName(f)!).Replace('\\', ' ').Replace('/', ' '), 20) : BuiltInGroup }).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<StageBackdrop>(); }
    }
    // Every background there is: the user's own, then the ones that came with the program.
    public IReadOnlyList<StageBackdrop> Backdrops => BuiltIn.Count == 0 ? State.Backdrops : State.Backdrops.Concat(BuiltIn).ToArray();
    public static bool IsBuiltIn(StageBackdrop backdrop) => BuiltIn.Any(b => ReferenceEquals(b, backdrop));
    readonly string path;
    public StageState State { get; }
    public Dictionary<string, StageSet> Sets => State.Sets!;
    public event Action? Changed;
    public StageBook(string? root = null, string? builtIn = null)
    {
        // Read once per run (or again when a test names a folder), before the saved choice of background is checked against them.
        if (builtIn != null || !builtInRead) { BuiltIn = ReadBuiltIn(builtIn ?? Path.Combine(AppContext.BaseDirectory, "backgrounds")); builtInRead = true; }
        root ??= Storage.Root; path = Path.Combine(root, "stage.json"); State = Load(root);
    }
    // Reads the file without keeping it open for changes (also used when checking which pictures are in use).
    public static StageState Load(string root)
    {
        string file = Path.Combine(root, "stage.json"); var state = Storage.ReadJson<StageState>(file, 4 * 1024 * 1024) ?? new();
        // The first version of the file was just the sets, each with its own backgrounds.
        if (state.Sets == null && File.Exists(file)) state.Sets = Storage.ReadJson<Dictionary<string, StageSet>>(file, 4 * 1024 * 1024);
        state.Sets ??= new(); state.Backdrops ??= new(); state.Talk ??= new(); state.Talk.Normalize(); state.Mode ??= "";
        foreach (var key in state.Sets.Keys.ToArray()) if (state.Sets[key] == null) state.Sets.Remove(key);
        foreach (var set in state.Sets.Values)
        {
            foreach (string old in set.Backgrounds ?? new()) if (!string.IsNullOrWhiteSpace(old) && state.Backdrops.All(b => !string.Equals(b.Path, old, StringComparison.OrdinalIgnoreCase))) state.Backdrops.Add(new StageBackdrop { Title = "배경 " + (state.Backdrops.Count + 1), Path = old });
            if (state.Backdrop.Length == 0 && !string.IsNullOrWhiteSpace(set.Background)) state.Backdrop = set.Background!;
            set.Backgrounds = null; set.Background = null;
        }
        Fit(state); return state;
    }
    static void Fit(StageState state)
    {
        foreach (var set in state.Sets!.Values)
        {
            set.Sprites = (set.Sprites ?? new()).Where(s => s != null && !string.IsNullOrWhiteSpace(s.Path) && !SafeFiles.IsNetworkPath(s.Path)).Take(MaxSprites).ToList();
            foreach (var sprite in set.Sprites) sprite.Tag = StageTags.Clean(sprite.Tag);
        }
        state.Backdrops = state.Backdrops.Where(b => b != null && !string.IsNullOrWhiteSpace(b.Path) && !SafeFiles.IsNetworkPath(b.Path)).DistinctBy(b => b.Path, StringComparer.OrdinalIgnoreCase).Take(MaxBackdrops).ToList();
        foreach (var backdrop in state.Backdrops) { backdrop.Title = StageTags.Clean(backdrop.Title, "배경"); backdrop.Group = SafeFiles.OneLine(backdrop.Group, 20); }
        if (state.Backdrops.Concat(BuiltIn).All(b => !string.Equals(b.Path, state.Backdrop ?? "", StringComparison.OrdinalIgnoreCase))) state.Backdrop = "";
        state.Talk.Normalize();
    }
    public StageSet Set(string id) { if (!Sets.TryGetValue(id, out var set)) Sets[id] = set = new(); return set; }
    public void AddSprite(string id, string tag, string file)
    {
        var set = Set(id); if (set.Sprites.Count >= MaxSprites) throw new InvalidOperationException("스탠딩 그림은 캐릭터마다 " + MaxSprites + "장까지 등록할 수 있어요.");
        set.Sprites.Add(new StageSprite { Tag = StageTags.Clean(tag), Path = file }); Save();
    }
    public StageBackdrop AddBackdrop(string title, string group, string file)
    {
        if (State.Backdrops.Count >= MaxBackdrops) throw new InvalidOperationException("배경은 " + MaxBackdrops + "장까지 등록할 수 있어요.");
        var backdrop = new StageBackdrop { Title = title, Group = group == BuiltInGroup ? "" : group, Path = file }; State.Backdrops.Add(backdrop); if (State.Backdrop.Length == 0) State.Backdrop = file; Save(); return backdrop;
    }
    // The background with this title, for when the AI names one.
    public StageBackdrop? Titled(string title) => title.Trim().Length == 0 ? null : Backdrops.FirstOrDefault(b => string.Equals(b.Title, title.Trim(), StringComparison.OrdinalIgnoreCase));
    public string CurrentTitle => Backdrops.FirstOrDefault(b => string.Equals(b.Path, State.Backdrop, StringComparison.OrdinalIgnoreCase))?.Title ?? "";
    public string[] Groups => Backdrops.Select(b => b.Group).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Length == 0).ThenBy(g => g, StringComparer.CurrentCulture).ToArray();
    public void Save() { Fit(State); OrderedStore.SaveJson(path, State.Snapshot()); Changed?.Invoke(); }
    // Every file a page uses, so that tidying up never takes one away.
    public static IEnumerable<string> Files(StageState? state) =>
        state == null ? Array.Empty<string>() : (state.Sets ?? new()).Values.Where(s => s != null).SelectMany(s => (s.Sprites ?? new()).Where(p => p != null).Select(p => p.Path)).Concat((state.Backdrops ?? new()).Where(b => b != null).Select(b => b.Path));
}
