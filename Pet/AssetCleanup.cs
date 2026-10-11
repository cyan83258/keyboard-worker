using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace KeyboardWorker;

public sealed record UnusedAsset(string Path, long Bytes, DateTime Modified);
public sealed record CleanupResult(int Removed, int Skipped, int Failed);
public static class AssetCleanup
{
    // Everything a setting, preset, custom item or pair refers to: pictures, typing sounds, voice clips, pictures of the two together.
    public static string[] References(Preferences settings, IEnumerable<CharacterPreset> presets, GameState game, AdventureState? book = null) =>
        settings.ImagePaths.Values.Concat(new[] { settings.SoundPath }).Concat(settings.SoundPresets.Select(p => p.Path)).Concat(settings.VoicePaths.Values)
            .Concat(presets.SelectMany(p => p.Images.Values.Concat((p.VoicePaths ?? new()).Values))).Concat(game.CustomItems.Select(c => c.ImagePath))
            .Concat(book == null ? Array.Empty<string>() : book.Pairs.Values.SelectMany(p => (p.Scenes ?? new()).Values)).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
    static T? Read<T>(string root, string filename)
    {
        string path = System.IO.Path.Combine(root, filename); if (!File.Exists(path)) return default;
        long maximum = filename == "character-presets.json" ? 32 * 1024 * 1024 : 4 * 1024 * 1024;
        if (new FileInfo(path).Length > maximum) throw new InvalidDataException(filename + " 파일이 너무 커서 안전하게 확인할 수 없습니다.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new InvalidDataException(filename + " 내용을 확인할 수 없습니다.");
    }
    public static UnusedAsset[] Scan(string root, IEnumerable<string> references, DateTime now)
    {
        var protectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Protect(IEnumerable<string> paths) { foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p))) protectedFiles.Add(System.IO.Path.GetFullPath(path)); }
        Protect(references);
        var settings = Read<Preferences>(root, "settings.json");
        if (settings != null) { settings.Normalize(); Protect(settings.ImagePaths.Values); Protect(new[] { settings.SoundPath }); Protect(settings.SoundPresets.Select(p => p.Path)); Protect(settings.VoicePaths.Values); }
        var presets = Read<List<CharacterPreset>>(root, "character-presets.json");
        if (presets != null) foreach (var preset in presets) { Protect(preset.Images.Values); Protect((preset.VoicePaths ?? new()).Values); }
        var book = Read<AdventureState>(root, "adventure-book.json");
        if (book?.Pairs != null) foreach (var pair in book.Pairs.Values) if (pair?.Scenes != null) Protect(pair.Scenes.Values);
        Protect(StageBook.Files(StageBook.Load(root)));
        var game = Read<GameState>(root, "pet-game.json");
        if (game != null) Protect(game.CustomItems.Select(c => c.ImagePath));
        var result = new List<UnusedAsset>(); root = System.IO.Path.GetFullPath(root);
        if (!Directory.Exists(root)) return result.ToArray();
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("연결된 저장 폴더는 정리하지 않습니다.");
        foreach (string category in new[] { "images", "sounds" })
        {
            string folder = System.IO.Path.Combine(root, category); if (!Directory.Exists(folder)) continue;
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("연결된 이미지·소리 폴더는 정리하지 않습니다.");
            foreach (string path in Directory.EnumerateFiles(folder, "*", System.IO.SearchOption.TopDirectoryOnly))
            {
                var file = new FileInfo(path); string ext = file.Extension.ToLowerInvariant();
                if (!Guid.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(path), "N", out _) || (category == "sounds" ? ext is not ".mp3" and not ".wav" : ext is not ".png" and not ".jpg" and not ".jpeg")) continue;
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || protectedFiles.Contains(file.FullName) || now - file.LastWriteTimeUtc < TimeSpan.FromHours(24)) continue;
                result.Add(new(file.FullName, file.Length, file.LastWriteTimeUtc));
            }
        }
        return result.ToArray();
    }
    public static CleanupResult Clean(string root, IEnumerable<UnusedAsset> reviewed, IEnumerable<string> references, DateTime now, Action<string>? remove = null)
    {
        var eligible = Scan(root, references, now).ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        int removed = 0, skipped = 0, failed = 0;
        foreach (var candidate in reviewed.DistinctBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (!eligible.TryGetValue(candidate.Path, out var current) || current.Bytes != candidate.Bytes || current.Modified != candidate.Modified) { skipped++; continue; }
            try
            {
                // Recheck the file immediately before a non-recursive recycle operation.
                var file = new FileInfo(current.Path);
                if (!file.Exists || file.Length != current.Bytes || file.LastWriteTimeUtc != current.Modified || (file.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                if (remove != null) remove(current.Path);
                else FileSystem.DeleteFile(current.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
                removed++;
            }
            catch { failed++; }
        }
        return new(removed, skipped, failed);
    }
}
