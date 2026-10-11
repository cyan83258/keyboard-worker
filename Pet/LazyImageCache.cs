using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KeyboardWorker;

public sealed class LazyImageCache : IDisposable
{
    readonly Dispatcher dispatcher;
    readonly Action ready;
    Dictionary<string, string> slots = new();
    Dictionary<string, string> keys = new();
    HashSet<string> activeKeys = new();
    readonly Dictionary<string, BitmapSource> cache = new();
    readonly Dictionary<string, Task<BitmapSource>> pending = new();
    readonly HashSet<string> failed = new();
    int width;
    bool disposed;
    int revision;
    public int CachedCount => cache.Count;
    public int DecodeRequests { get; private set; }
    public LazyImageCache(Dispatcher dispatcher, Action ready) { this.dispatcher = dispatcher; this.ready = ready; }
    string Id(string path)
    {
        try { var info = new FileInfo(path); return Path.GetFullPath(path) + "|" + width + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|" + SharedSprites.Revision(path); }
        catch { return path + "|" + width + "|missing|" + revision; }
    }
    // eager: also decode the typing pictures now. A pet that is not typed at yet can leave them for later.
    public void Configure(Preferences preferences, int pixels, bool forceReload = false, bool eager = true)
    {
        if (forceReload) { revision++; SharedSprites.Invalidate(preferences.ImagePaths.Values); }
        width = pixels;
        slots = preferences.ImagePaths.Where(p => preferences.AdvancedImages || !ImageAssets.IsAdvanced(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        keys = slots.ToDictionary(p => p.Key, p => Id(p.Value)); activeKeys = keys.Values.ToHashSet(); var valid = activeKeys;
        foreach (var key in cache.Keys.Where(k => !valid.Contains(k)).ToArray()) cache.Remove(key);
        failed.RemoveWhere(k => !valid.Contains(k));
        Get("idle"); if (eager) { Get("typing-left"); Get("typing-right"); }
    }
    public BitmapSource? Get(string slot)
    {
        if (disposed || !slots.TryGetValue(slot, out var path)) return null;
        string id = keys[slot]; if (cache.TryGetValue(id, out var value)) return value;
        if (failed.Contains(id) || pending.ContainsKey(id)) return null;
        var task = SharedSprites.Read(id, path, width, out bool decoding); if (decoding) DecodeRequests++; pending[id] = task;
        _ = task.ContinueWith(t =>
        {
            _ = t.Exception;
            if (disposed || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(() =>
            {
                if (pending.TryGetValue(id, out var current) && ReferenceEquals(current, t)) pending.Remove(id);
                if (disposed || !activeKeys.Contains(id)) return;
                if (t.IsCompletedSuccessfully) cache[id] = t.Result; else failed.Add(id);
                ready();
            });
        }, TaskScheduler.Default);
        return null;
    }
    public void Dispose() { disposed = true; slots.Clear(); keys.Clear(); activeKeys.Clear(); cache.Clear(); failed.Clear(); pending.Clear(); }
}
