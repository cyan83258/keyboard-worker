using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace KeyboardWorker;

// Fonts the user chose for the pets' lines (TTF / OTF files), copied into the settings folder's fonts folder.
public static class FontAssets
{
    public const int MinSize = 8, MaxSize = 48;
    public static readonly FontFamily Standard = new("Malgun Gothic");
    static readonly Dictionary<string, FontFamily?> loaded = new(StringComparer.OrdinalIgnoreCase);
    static string Folder(string root) => Path.Combine(root, "fonts");
    static bool FontFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ttf" or ".otf";
    // The font in a file, or null when there is none (empty path, missing file, not a font). Each file is opened once.
    public static FontFamily? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || SafeFiles.IsNetworkPath(path)) return null;
        if (loaded.TryGetValue(path, out var known)) return known;
        FontFamily? family = null;
        try
        {
            if (File.Exists(path) && FontFile(path))
            {
                var face = new GlyphTypeface(new Uri(Path.GetFullPath(path)));   // throws when the file is not a font
                string name = face.FamilyNames.Values.FirstOrDefault() ?? ""; string folder = Path.GetDirectoryName(Path.GetFullPath(path))!.Replace('\\', '/');
                if (name.Length > 0) family = new FontFamily(new Uri("file:///" + folder + "/"), "./#" + name);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or UriFormatException or FileFormatException or System.Runtime.InteropServices.COMException) { }
        return loaded[path] = family;
    }
    public static FontFamily Get(string? path) => Load(path) ?? Standard;
    // The name to show for a chosen font.
    public static string Name(string? path) => Load(path) is { } family ? family.Source[(family.Source.IndexOf('#') + 1)..] : "기본 글꼴 (맑은 고딕)";
    // Checks the file and copies it into the settings folder. Returns the copy's path.
    public static string Import(string source, string root)
    {
        if (!FontFile(source)) throw new InvalidDataException("TTF 또는 OTF 글꼴 파일을 선택해 주세요.");
        var info = new FileInfo(source); if (!info.Exists) throw new FileNotFoundException("글꼴 파일을 찾지 못했습니다.");
        if (info.Length > 40 * 1024 * 1024 || info.Length < 100) throw new InvalidDataException("글꼴 파일은 40MB 이하의 정상 파일을 선택해 주세요.");
        try { _ = new GlyphTypeface(new Uri(Path.GetFullPath(source))); } catch (Exception e) when (e is FileFormatException or NotSupportedException or ArgumentException or IOException or System.Runtime.InteropServices.COMException) { throw new InvalidDataException("글꼴 파일을 읽을 수 없습니다. 다른 TTF / OTF 파일을 골라 주세요."); }
        string folder = Folder(root); Directory.CreateDirectory(folder); string full = Path.GetFullPath(source);
        if (string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)) return full;
        string target = Path.Combine(folder, Guid.NewGuid().ToString("N") + info.Extension.ToLowerInvariant()); File.Copy(source, target); File.SetLastWriteTimeUtc(target, DateTime.UtcNow); return target;   // the copy's own age decides when an unused one may be removed
    }
    // Removes copied fonts that no setting uses any more (files added in the last hour are left, in case a choice is not saved yet).
    public static int Prune(string root, IEnumerable<string> used, DateTime now)
    {
        var folder = new DirectoryInfo(Folder(root)); if (!folder.Exists) return 0; int removed = 0;
        var keep = used.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in folder.GetFiles())
        {
            if (keep.Contains(file.FullName) || !FontFile(file.Name) || now.ToUniversalTime() - file.LastWriteTimeUtc < TimeSpan.FromHours(1)) continue;
            try { file.Delete(); removed++; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return removed;
    }
}
