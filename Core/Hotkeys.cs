using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace KeyboardWorker;

public sealed class GlobalHotkeys : IDisposable
{
    readonly HwndSource source;
    readonly Action<string> execute;
    Dictionary<string, string> current = new();
    readonly Dictionary<int, string> ids = new();
    public GlobalHotkeys(HwndSource source, Action<string> execute) { this.source = source; this.execute = execute; source.AddHook(Hook); }
    public bool IsRegistered(string action) => ids.Values.Contains(action);
    public void ConfigureIfChanged(Dictionary<string, string> previous, Dictionary<string, string> next)
    {
        if (previous.Count == next.Count && previous.All(p => next.TryGetValue(p.Key, out var value) && value == p.Value)) return;
        Configure(next);
    }
    public static (uint Mods, uint Key) Parse(string value)
    {
        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); uint mods = 0;
        if (parts.Length < 2) throw new InvalidOperationException("단축키는 Ctrl/Alt/Win과 키를 조합해 주세요. 예: Ctrl+Alt+Q");
        foreach (var part in parts[..^1]) mods |= part.ToLowerInvariant() switch { "ctrl" or "control" => 2u, "alt" => 1u, "shift" => 4u, "win" => 8u, _ => throw new InvalidOperationException("단축키 조합을 확인해 주세요: " + value) };
        if ((mods & 11) == 0 || !Enum.TryParse<Key>(parts[^1], true, out var key) || key == Key.None || key == Key.F12) throw new InvalidOperationException("Ctrl/Alt/Win + 문자 또는 F1~F11을 사용해 주세요.");
        int vk = KeyInterop.VirtualKeyFromKey(key); if (vk <= 0) throw new InvalidOperationException("지원하지 않는 단축키입니다."); return (mods, (uint)vk);
    }
    public void Configure(Dictionary<string, string> values)
    {
        if (values.Count == current.Count && values.All(p => current.TryGetValue(p.Key, out var value) && value == p.Value)) return;
        var parsed = values.Where(v => !string.IsNullOrWhiteSpace(v.Value)).ToDictionary(v => v.Key, v => Parse(v.Value));
        if (parsed.Values.Distinct().Count() != parsed.Count) throw new InvalidOperationException("같은 단축키를 두 기능에 지정할 수 없습니다.");
        var old = current; Clear();
        try { Register(values); current = new(values); }
        catch { Clear(); try { Register(old); current = old; } catch { Clear(); } throw; }
    }
    void Register(Dictionary<string, string> values)
    {
        int id = 5100;
        foreach (var pair in values.Where(v => !string.IsNullOrWhiteSpace(v.Value)))
        {
            var key = Parse(pair.Value);
            if (!RegisterHotKey(source.Handle, id, key.Mods | 0x4000, key.Key)) throw new InvalidOperationException("단축키가 다른 프로그램에서 사용 중입니다: " + pair.Value);
            ids[id++] = pair.Key;
        }
    }
    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x312 && ids.TryGetValue(wp.ToInt32(), out var name)) { handled = true; execute(name); } return IntPtr.Zero;
    }
    void Clear() { foreach (var id in ids.Keys) UnregisterHotKey(source.Handle, id); ids.Clear(); }
    public void Dispose() { Clear(); source.RemoveHook(Hook); }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
