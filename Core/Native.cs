using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.IO;
using System.Text;

namespace KeyboardWorker;

public static class Native
{
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    public static bool IsTopmost(IntPtr hwnd) => (GetStyle(hwnd, -20).ToInt64() & 8) != 0;
    public static void RaisePet(IntPtr hwnd) { if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13); }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attribute, ref uint value, int size);
    public static bool RemoveSystemBorder(IntPtr hwnd)
    {
        uint none = 0xFFFFFFFE; int result = DwmSetWindowAttribute(hwnd, 34, ref none, 4);
        uint square = 1; DwmSetWindowAttribute(hwnd, 33, ref square, 4);
        return result == 0;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetStyle(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetStyle(IntPtr h, int index, IntPtr value);
    public static bool NoActivate(IntPtr hwnd) => (GetStyle(hwnd, -20).ToInt64() & 0x08000000L) != 0;
    public static void Style(IntPtr hwnd, bool through)
    {
        long style = GetStyle(hwnd, -20).ToInt64() | 0x08000000L | 0x80L;
        style = through ? style | 0x20L : style & ~0x20L;
        SetStyle(hwnd, -20, new IntPtr(style));
    }
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    // The pets never take focus, so this app is usually in the background and Windows would open its windows behind the active one.
    // Ask for the foreground; if Windows refuses, at least move above the other normal windows. This never touches another
    // application's input queue (attaching to it can freeze both programs when that application is busy).
    public static void Front(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || GetForegroundWindow() == hwnd) return;
        SetForegroundWindow(hwnd);
        if (GetForegroundWindow() != hwnd) { SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13); SetWindowPos(hwnd, new IntPtr(-2), 0, 0, 0, 0, 0x13); }
    }
    [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint Size, Time; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput info);
    // Milliseconds since the last keyboard or mouse input anywhere in Windows.
    public static long IdleMs() { var info = new LastInput { Size = 8 }; return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) : 0; }
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    public static bool CtrlDown => (GetAsyncKeyState(0x11) & 0x8000) != 0;
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    public static IntPtr ForegroundHandle => GetForegroundWindow();
    delegate bool WindowCallback(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);
    public static bool HasYouTubeMusicWindow(string? song = null) => HasWindowTitled("YouTube Music", song);
    // Is there a window whose title contains the marker (and the media title, when given)?
    public static bool HasWindowTitled(string marker, string? song = null)
    {
        bool found = false; var text = new StringBuilder(512);
        EnumWindows((hwnd, _) =>
        {
            text.Clear();
            GetWindowText(hwnd, text, 512);
            string title = text.ToString();
            if (title.Contains(marker, StringComparison.OrdinalIgnoreCase) && (string.IsNullOrEmpty(song) || title.Contains(song, StringComparison.OrdinalIgnoreCase))) { found = true; return false; } return true;
        }, IntPtr.Zero);
        return found;
    }
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);
    // Hand memory that is not being touched back to Windows (it is paged in again on use).
    public static void TrimMemory() { try
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        SetProcessWorkingSetSize(self.Handle, new IntPtr(-1), new IntPtr(-1));
    } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { } }
    public static void Yield(bool lower) { try
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var wanted = lower ? System.Diagnostics.ProcessPriorityClass.BelowNormal : System.Diagnostics.ProcessPriorityClass.Normal;
        if (self.PriorityClass != wanted) self.PriorityClass = wanted;
    } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { } }
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint id);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static IntPtr lastForeground;
    static string foregroundName = "";
    // Full path of the program whose window is in front ("" when it cannot be read or it is this app).
    public static string ForegroundPath { get; private set; } = "";
    public static string ForegroundProcess()
    {
        var h = GetForegroundWindow(); if (h == lastForeground) return foregroundName; lastForeground = h;
        ForegroundPath = ""; GetWindowThreadProcessId(h, out uint pid); if (pid == Environment.ProcessId) return foregroundName = "KeyboardWorker";
        var process = OpenProcess(0x1000, false, pid); if (process == IntPtr.Zero) return foregroundName = "";
        try
        {
            uint size = 1024;
            var path = new StringBuilder((int)size);
            if (!QueryFullProcessImageName(process, 0, path, ref size)) return foregroundName = "";
            ForegroundPath = path.ToString();
            return foregroundName = Path.GetFileNameWithoutExtension(ForegroundPath);
        }
        finally { CloseHandle(process); }
    }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    public static bool Fullscreen(IntPtr own)
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero || h == own || h == GetShellWindow() || IsZoomed(h) || !GetWindowRect(h, out var r)) return false;
        var s = System.Windows.Forms.Screen.FromHandle(h).Bounds;
        return r.Left <= s.Left && r.Top <= s.Top && r.Right >= s.Right && r.Bottom >= s.Bottom;
    }
}
