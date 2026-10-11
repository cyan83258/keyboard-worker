using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace KeyboardWorker;

public static class AppInfo
{
    // The version from the project file, e.g. "2.0.0".
    // "owner/repository" on GitHub where new versions are published (set in the project file); empty = no update address.
    public static string UpdateRepository { get; } = typeof(AppInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "UpdateRepository")?.Value?.Trim() ?? "";
    public static string Version { get; } = (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+')[0] is { Length: > 0 } version ? version : "개발판";
}

// Starting with Windows: one value under the current user's Run key. Nothing is installed and no administrator right is needed.
public static class Autostart
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "KeyboardWorker";
    static string Command(string exe) => "\"" + exe + "\"";
    // The program path registered to start with Windows, or null when it is off.
    public static string? Registered(string key = RunKey, string name = Name)
    {
        try { using var run = Registry.CurrentUser.OpenSubKey(key); return (run?.GetValue(name) as string)?.Trim().Trim('"'); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }
    public static bool IsOn(string key = RunKey, string name = Name) => Registered(key, name) is { Length: > 0 };
    // True when the registered program is not the one running now (the folder was moved, or another copy registered itself).
    public static bool Elsewhere(string? exe = null, string key = RunKey, string name = Name) =>
        Registered(key, name) is { Length: > 0 } saved && !string.Equals(Path.GetFullPath(saved), Path.GetFullPath(exe ?? Environment.ProcessPath ?? ""), StringComparison.OrdinalIgnoreCase);
    public static void Set(bool on, string? exe = null, string key = RunKey, string name = Name)
    {
        using var run = Registry.CurrentUser.CreateSubKey(key, true) ?? throw new InvalidOperationException("시작 프로그램 목록을 열 수 없습니다.");
        if (!on) { run.DeleteValue(name, false); return; }
        exe ??= Environment.ProcessPath ?? throw new InvalidOperationException("프로그램 위치를 알 수 없습니다.");
        run.SetValue(name, Command(Path.GetFullPath(exe)), RegistryValueKind.String);
    }
}
