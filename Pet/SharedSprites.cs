using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

public static class SharedSprites
{
    sealed class Entry { public Task<BitmapSource>? Pending; public WeakReference<BitmapSource>? Image; }
    static readonly object gate = new();
    static readonly Dictionary<string, Entry> entries = new();
    static readonly Dictionary<string, int> revisions = new(StringComparer.OrdinalIgnoreCase);
    public static int Revision(string path) { lock (gate) return revisions.GetValueOrDefault(path); }
    public static void Invalidate(IEnumerable<string> paths) { lock (gate) foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase)) revisions[path] = revisions.GetValueOrDefault(path) + 1; }
    public static Task<BitmapSource> Read(string id, string path, int width, out bool decoding)
    {
        lock (gate)
        {
            if (entries.TryGetValue(id, out var found))
            {
                if (found.Image?.TryGetTarget(out var bitmap) == true) { decoding = false; return Task.FromResult(bitmap); }
                if (found.Pending != null) { decoding = false; return found.Pending; }
            }
            if (entries.Count >= 128) foreach (string key in entries.Where(p => p.Value.Pending == null && p.Value.Image?.TryGetTarget(out _) != true).Select(p => p.Key).ToArray()) entries.Remove(key);
            var entry = new Entry(); decoding = true; var task = Task.Run(() => ImageAssets.Read(path, width)); entry.Pending = task; entries[id] = entry;
            _ = task.ContinueWith(t => { lock (gate) { if (t.IsCompletedSuccessfully) entry.Image = new(t.Result); entry.Pending = null; _ = t.Exception; } }, TaskScheduler.Default);
            return task;
        }
    }
}
