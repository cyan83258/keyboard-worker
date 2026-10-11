using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KeyboardWorker;

public static class ImageReplacementTests
{
    public static int Run(string root)
    {
        int count = 0; void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        string folder = Path.Combine(root, "image-replacement"); Directory.CreateDirectory(folder); string source = Path.Combine(folder, "same-name.png"); Solid(source, 255, 0);
        var selected = new Dictionary<string, string> { ["idle"] = source };
        var first = ImageAssets.ImportSelectionAsync(selected, new(), new() { "idle" }, folder).GetAwaiter().GetResult(); string oldFile = first["idle"];
        Check(Red(ImageAssets.Read(oldFile, 16)), "First selected image imported");
        Solid(source, 0, 255); var second = ImageAssets.ImportSelectionAsync(selected, first, new() { "idle" }, folder).GetAwaiter().GetResult();
        Check(second["idle"] != oldFile && !Red(ImageAssets.Read(second["idle"], 16)), "Same source filename replaced by fresh managed asset");
        var sameManaged = new Dictionary<string, string> { ["idle"] = oldFile }; Solid(oldFile, 0, 255);
        var reselect = ImageAssets.ImportSelectionAsync(sameManaged, sameManaged, new() { "idle" }, folder).GetAwaiter().GetResult();
        Check(reselect["idle"] != oldFile && !Red(ImageAssets.Read(reselect["idle"], 16)), "Explicitly reselected managed file never reuses old image identity");
        var untouched = ImageAssets.ImportSelectionAsync(second, second, new(), folder).GetAwaiter().GetResult();
        Check(untouched["idle"] == second["idle"], "Unchanged slot does not make redundant copy");
        var settings = new Preferences { ImagePaths = second, SkinPath = "" };
        string jsonPath = Path.Combine(folder, "settings.json");
        OrderedStore.SaveJsonAsync(jsonPath, settings.Snapshot()).GetAwaiter().GetResult();
        var loaded = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(jsonPath))!;
        loaded.Normalize();
        Check(loaded.ImagePaths["idle"] == second["idle"] && !Red(ImageAssets.Read(loaded.ImagePaths["idle"], 16)), "Restart loads replacement asset after committed save");
        using var cache = new LazyImageCache(Dispatcher.CurrentDispatcher, () => { }); string fixedFile = Path.Combine(folder, "fixed-cache.png"); Solid(fixedFile, 255, 0);
        var p = new Preferences { ImagePaths = new() { ["idle"] = fixedFile } }; cache.Configure(p, 16); Pump(() => cache.Get("idle") != null); Check(Red(cache.Get("idle")!), "Original image cached");
        long bytes = new FileInfo(fixedFile).Length; DateTime stamp = File.GetLastWriteTimeUtc(fixedFile); Solid(fixedFile, 0, 255); File.SetLastWriteTimeUtc(fixedFile, stamp);
        cache.Configure(p, 16, true); Pump(() => cache.Get("idle") != null); Check(!Red(cache.Get("idle")!), "Forced save refreshes same path even with preserved timestamp");
        Solid(fixedFile, 255, 0);
        File.SetLastWriteTimeUtc(fixedFile, stamp.AddSeconds(10));
        cache.Configure(p, 16);
        Pump(() => cache.Get("idle") != null);
        Check(Red(cache.Get("idle")!), "Changed file metadata invalidates bitmap cache");
        var changed = new Preferences { ImagePaths = new() { ["idle"] = second["idle"] } };
        cache.Configure(changed, 16);
        Pump(() => cache.Get("idle") != null);
        Check(!Red(cache.Get("idle")!), "Switching to different image path updates visible pixels");
        var cleared = ImageAssets.ImportSelectionAsync(new(), second, new(), folder).GetAwaiter().GetResult(); Check(cleared.Count == 0, "Clearing slot stays cleared");
        return count;
    }
    static bool Red(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        byte[] pixel = new byte[4];
        converted.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        return pixel[2] > pixel[0];
    }
    static void Solid(string path, byte r, byte b)
    {
        byte[] pixels = new byte[16 * 16 * 4]; for (int n = 0; n < pixels.Length; n += 4) { pixels[n] = b; pixels[n + 2] = r; pixels[n + 3] = 255; }
        var source = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 64);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        png.Save(stream);
    }
    static void Pump(Func<bool> done)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) }; long until = Environment.TickCount64 + 5000;
        timer.Tick += (_, _) => { if (done() || Environment.TickCount64 > until) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        if (!done()) throw new Exception("Replacement cache load timeout");
    }
}
