using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KeyboardWorker;

public sealed record InstalledMod(ModFile File, string Path, bool Enabled);

// The mods in the settings folder's mods folder, and what the enabled ones add. Read once at start-up (before the theme is
// chosen) and again whenever a mod is added, removed or switched.
public sealed class ModStore
{
    static ModStore? current;
    public static ModStore Current => current ??= new ModStore(Storage.Root);
    // For tests and after the settings folder changed.
    public static ModStore Reload(string? root = null) => current = new ModStore(root ?? Storage.Root);

    readonly string folder, statePath; HashSet<string> disabled;
    public IReadOnlyList<InstalledMod> Mods { get; private set; } = Array.Empty<InstalledMod>();
    public IReadOnlyList<LinePack> LinePacks { get; private set; } = Array.Empty<LinePack>();
    public IReadOnlyList<ModeProfile> Profiles { get; private set; } = Array.Empty<ModeProfile>();
    public IReadOnlyList<StageMode> PageModes { get; private set; } = Array.Empty<StageMode>();
    public IReadOnlyList<string> Problems { get; private set; } = Array.Empty<string>();
    public event Action? Changed;
    ModStore(string root)
    {
        folder = Path.Combine(root, "mods"); statePath = Path.Combine(folder, "disabled.json");
        try { disabled = File.Exists(statePath) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(statePath)) ?? new() : new(); } catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { disabled = new(); }
        Read();
    }
    void Read()
    {
        var mods = new List<InstalledMod>(); var problems = new List<string>();
        if (Directory.Exists(folder))
            foreach (string path in Directory.GetFiles(folder, "*.kwmod.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Take(100))
            {
                try
                {
                    if (new FileInfo(path).Length > ModFile.MaxBytes) throw new InvalidOperationException("파일이 너무 큽니다.");
                    var mod = ModFile.Parse(File.ReadAllText(path)); if (mods.Any(m => m.File.Id == mod.Id)) throw new InvalidOperationException("같은 id의 모드가 이미 있습니다.");
                    mods.Add(new(mod, path, !disabled.Contains(mod.Id)));
                }
                catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException) { problems.Add(Path.GetFileName(path) + ": " + e.Message); }
            }
        Mods = mods; Problems = problems; var enabled = mods.Where(m => m.Enabled).Select(m => m.File).ToArray();
        // Ids are prefixed with the mod's id, so two mods (or a mod and a built-in) can never collide.
        LinePacks = enabled.SelectMany(m => m.LinePacks!.Select(p => new LinePack("mod-" + m.Id + "-" + p.Id, p.Name, p.Description.Length > 0 ? p.Description : m.Name + " 모드의 대사 팩", p.Price, p.Lines!))).ToArray();
        Profiles = enabled.SelectMany(m => m.Profiles!.Select((p, i) => new ModeProfile { Id = "mod-" + m.Id + "-" + i, Name = p.Name, Values = new(p.Values!) })).ToArray();
        PageModes = enabled.SelectMany(m => m.PageModes!.Select(p => new StageMode("mod-" + m.Id + "-" + p.Id, p.Name, p.Layout, p.Prompt.Length > 0 ? "\n[" + p.Name + "]\n" + p.Prompt : "", p.Length, m.Name))).ToArray();
        Pastel.SetExtraThemes(enabled.SelectMany(m => m.Themes!.Select(t => ("mod-" + m.Id + "-" + t.Id, t.Name, t.AccentHue, t.SecondHue, t.Saturation))));
    }
    void Refresh() { Read(); Changed?.Invoke(); }
    // Checks a file and copies it into the mods folder. A mod with the same id is replaced (an update).
    public ModFile Install(string source)
    {
        var info = new FileInfo(source); if (!info.Exists) throw new FileNotFoundException("모드 파일을 찾지 못했습니다.");
        if (info.Length > ModFile.MaxBytes) throw new InvalidOperationException("모드 파일이 너무 큽니다 (2MB 이하).");
        return InstallText(File.ReadAllText(source));
    }
    // source: the GitHub address the text was downloaded from (kept in the copy, so the mod can be updated from there).
    public ModFile InstallText(string json, string source = "")
    {
        var mod = ModFile.Parse(json); if (source.Length > 0) mod.Source = Uri.TryCreate(source, UriKind.Absolute, out var from) && Downloader.Allowed(from) ? source : "";
        Directory.CreateDirectory(folder);
        foreach (var old in Mods.Where(m => m.File.Id == mod.Id)) try { File.Delete(old.Path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        // The copy is written from what was understood, under a name of our choosing.
        File.WriteAllText(Path.Combine(folder, mod.Id + ".kwmod.json"), JsonSerializer.Serialize(mod, ModFile.Writing)); Refresh(); return mod;
    }
    public void Enable(string id, bool on)
    {
        if (on ? !disabled.Remove(id) : !disabled.Add(id)) return;
        Directory.CreateDirectory(folder); File.WriteAllText(statePath, JsonSerializer.Serialize(disabled)); Refresh();
    }
    public void Remove(string id)
    {
        foreach (var mod in Mods.Where(m => m.File.Id == id)) File.Delete(mod.Path);
        if (disabled.Remove(id)) File.WriteAllText(statePath, JsonSerializer.Serialize(disabled));
        Refresh();
    }
    public string Folder { get { Directory.CreateDirectory(folder); return folder; } }
    public static string Text(InstalledMod mod) { try { return File.ReadAllText(mod.Path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "파일을 읽을 수 없습니다. " + e.Message; } }
}
