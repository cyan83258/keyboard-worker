using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace KeyboardWorker;

// Measures how long each part of starting up takes and keeps the latest runs in startup-timing.txt.
public static class StartupTrace
{
    const string FileName = "startup-timing.txt";
    static readonly Stopwatch watch = new(); static readonly List<(string Name, long Ms)> marks = new();
    static long last; static double runtime; static bool shown, image, written;
    public static void Begin()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            runtime = Math.Max(0, (DateTime.Now - process.StartTime).TotalMilliseconds);
        } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        watch.Restart();
    }
    // Time since the previous mark is booked under this name.
    public static void Mark(string name) { if (written || !watch.IsRunning) return; long now = watch.ElapsedMilliseconds; marks.Add((name, now - last)); last = now; }
    public static T Time<T>(string name, Func<T> load) { var value = load(); Mark(name); return value; }
    public static void Shown(bool expectImage) { if (shown) return; Mark("창 표시"); shown = true; if (!expectImage || image) Write(); }
    public static void Image() { if (image) return; Mark("첫 이미지"); image = true; if (shown) Write(); }
    public static void Expire() => Write();
    static void Write()
    {
        if (written || !watch.IsRunning) return; written = true; watch.Stop();
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | 합계 {runtime + last:0}ms | 런타임 로딩 {runtime:0} · " + string.Join(" · ", marks.Select(m => m.Name + " " + m.Ms));
        try
        {
            string path = Path.Combine(Storage.Root, FileName); Directory.CreateDirectory(Storage.Root);
            var lines = File.Exists(path) ? File.ReadAllLines(path).TakeLast(29).ToList() : new List<string>(); lines.Add(line); File.WriteAllLines(path, lines);
        }
        catch { }
    }
    public static string[] Recent(int count)
    {
        try { string path = Path.Combine(Storage.Root, FileName); return File.Exists(path) ? File.ReadAllLines(path).TakeLast(count).Reverse().ToArray() : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }
}
