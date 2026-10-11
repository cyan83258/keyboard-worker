using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using Microsoft.VisualBasic.FileIO;

namespace KeyboardWorker;

public sealed record DietPlan(Dictionary<string, string> Merge, string[] Oversized, long DuplicateBytes, long OversizedBytes);
public sealed record DietResult(int Merged, int Resized, long SavedBytes, int Failed);

// Keeps the copied pictures small: the same picture is stored once, and nothing is stored larger than it is ever drawn.
public static class ImageDiet
{
    // Sprites are decoded at most this many pixels on the longer side (see the LazyImageCache.Configure callers).
    public const int MaxSide = 1024;

    static bool IsPicture(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";
    static bool Oversized(string path)
    {
        using var stream = File.OpenRead(path); var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        return Math.Max(frame.PixelWidth, frame.PixelHeight) > MaxSide;
    }
    // What to store for a picture: the file as it is, or a copy scaled to MaxSide when that is clearly smaller.
    // The scaled copy is produced by the same decoder the pet uses, so it looks the same on screen.
    public static byte[] Slim(string path)
    {
        byte[] original = File.ReadAllBytes(path);
        try
        {
            if (!Oversized(path)) return original;
            BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() == ".png" ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 92 };
            encoder.Frames.Add(BitmapFrame.Create(ImageAssets.Read(path, MaxSide))); using var scaled = new MemoryStream(); encoder.Save(scaled);
            return scaled.Length < original.Length * .9 ? scaled.ToArray() : original;
        }
        catch { return original; }
    }
    // An identical picture that is already stored in the folder, if any. The source itself never counts: a stored file that
    // was edited in place and selected again must get a new name, because the old name may still be cached with the old pixels.
    public static string? Existing(string folder, string extension, byte[] content, string source)
    {
        if (!Directory.Exists(folder)) return null; source = Path.GetFullPath(source);
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*" + extension))
            try { if (!file.FullName.Equals(source, StringComparison.OrdinalIgnoreCase) && file.Length == content.Length && File.ReadAllBytes(file.FullName).AsSpan().SequenceEqual(content)) return file.FullName; } catch (IOException) { }
        return null;
    }

    // Looks only at pictures that are in use; unused ones belong to AssetCleanup.
    public static DietPlan Scan(string root, IEnumerable<string> references)
    {
        string folder = Path.Combine(Path.GetFullPath(root), "images");
        var files = references.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => string.Equals(Path.GetDirectoryName(p), folder, StringComparison.OrdinalIgnoreCase) && IsPicture(p) && File.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        var merge = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var kept = new Dictionary<string, string>(); long duplicateBytes = 0;
        foreach (string file in files)
        {
            long length = new FileInfo(file).Length;
            string key;
            using (var stream = File.OpenRead(file)) key = Path.GetExtension(file).ToLowerInvariant() + length + ":" + Convert.ToHexString(SHA256.HashData(stream));
            if (kept.TryGetValue(key, out var first)) { merge[file] = first; duplicateBytes += length; } else kept[key] = file;
        }
        var oversized = kept.Values.Where(p => { try { return Oversized(p); } catch { return false; } }).ToArray();
        return new(merge, oversized, duplicateBytes, oversized.Sum(p => new FileInfo(p).Length));
    }
    public static string Map(IReadOnlyDictionary<string, string> merge, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        try { return merge.TryGetValue(Path.GetFullPath(path), out var kept) ? kept : path; } catch { return path; }
    }
    static void Recycle(string path) => FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);

    // Call after every reference has been moved to the kept copies and saved. Replaced and merged files go to the recycle bin.
    public static DietResult Apply(DietPlan plan, IEnumerable<string> referencesNow, Action<string>? recycle = null)
    {
        recycle ??= Recycle; int merged = 0, resized = 0, failed = 0; long saved = 0;
        var used = new HashSet<string>(referencesNow.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        foreach (string duplicate in plan.Merge.Keys)
        {
            if (used.Contains(duplicate) || !File.Exists(duplicate)) continue;
            try { long length = new FileInfo(duplicate).Length; recycle(duplicate); merged++; saved += length; } catch { failed++; }
        }
        foreach (string picture in plan.Oversized)
        {
            string temporary = picture + ".tmp";
            try
            {
                long before = new FileInfo(picture).Length; byte[] slim = Slim(picture); if (slim.Length >= before) continue;
                File.WriteAllBytes(temporary, slim); recycle(picture); File.Move(temporary, picture, true); resized++; saved += before - slim.Length;
            }
            catch { failed++; try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
        }
        return new(merged, resized, saved, failed);
    }
}
