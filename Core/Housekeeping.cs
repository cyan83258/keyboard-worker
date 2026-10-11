using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KeyboardWorker;

public sealed record TidyOptions
{
    public bool Auto { get; init; } = true;          // clear leftovers by itself, once a day
    public bool AutoUnused { get; init; }            // also send pictures and sounds nothing has used for 30 days to the recycle bin
    public string LastRun { get; init; } = "";       // yyyy-MM-dd
    public int AutoBackupDays { get; init; } = 7;    // 0 = no automatic backup
    public bool AutoBackupMedia { get; init; }       // pictures and sounds too (large)
    public int AutoBackupKeep { get; init; } = 5;
    public string LastBackup { get; init; } = "";    // yyyy-MM-dd
    public TidyOptions Normalize() => this with { AutoBackupDays = Math.Clamp(AutoBackupDays, 0, 90), AutoBackupKeep = Math.Clamp(AutoBackupKeep, 1, 30), LastRun = LastRun ?? "", LastBackup = LastBackup ?? "" };
}
public sealed record Leftover(string Kind, string Path, long Bytes, bool Folder = false);

// Keeps the settings folder from filling up with things nobody needs: unfinished temporary files, working folders of an
// interrupted import, old copies of broken settings files, old safety and automatic backups. Settings, pictures in use,
// chat history and anything outside the settings folder (except this program's own temporary folders) are never touched.
public static class Housekeeping
{
    public const string AutoFolder = "auto", AutoPrefix = "auto-backup-";
    static long Size(DirectoryInfo folder) { try { return folder.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; } }
    public static Leftover[] Scan(string root, DateTime now, int keepAuto = 5, string? temp = null)
    {
        var found = new List<Leftover>(); var home = new DirectoryInfo(root); if (!home.Exists || (home.Attributes & FileAttributes.ReparsePoint) != 0) return found.ToArray();
        bool Old(FileSystemInfo item, TimeSpan age) => now.ToUniversalTime() - item.LastWriteTimeUtc > age && (item.Attributes & FileAttributes.ReparsePoint) == 0;
        try
        {
            // Temporary files of a save or an import that never finished (a running save is seconds old).
            foreach (var folder in new[] { home }.Concat(new[] { "images", "sounds", "backups" }.Select(n => new DirectoryInfo(Path.Combine(root, n))).Where(d => d.Exists)))
                foreach (var file in folder.EnumerateFiles("*.tmp")) if (Old(file, TimeSpan.FromHours(1))) found.Add(new("끝나지 않은 임시 파일", file.FullName, file.Length));
            foreach (var folder in home.EnumerateDirectories()) if ((folder.Name.StartsWith("preset-import-") || folder.Name.StartsWith("couple-import-")) && Old(folder, TimeSpan.FromHours(1))) found.Add(new("가져오다 만 작업 폴더", folder.FullName, Size(folder), true));
            foreach (var file in home.EnumerateFiles("*.broken-*")) if (Old(file, TimeSpan.FromDays(30))) found.Add(new("오래된 손상 파일 사본", file.FullName, file.Length));
            var updates = new DirectoryInfo(Path.Combine(root, "updates")); if (updates.Exists && Old(updates, TimeSpan.FromDays(1))) found.Add(new("쓰고 남은 업데이트 파일", updates.FullName, Size(updates), true));
            var pending = new FileInfo(Path.Combine(root, "pending-import.zip")); if (pending.Exists && Old(pending, TimeSpan.FromDays(1))) found.Add(new("적용되지 않은 가져오기 파일", pending.FullName, pending.Length));
            var backups = new DirectoryInfo(Path.Combine(root, "backups"));
            if (backups.Exists)
            {
                foreach (var file in backups.EnumerateFiles("before-import-*.zip").OrderByDescending(f => f.Name).Skip(5)) found.Add(new("오래된 안전 백업", file.FullName, file.Length));
                foreach (var file in backups.EnumerateFiles("*.zip").Where(f => !f.Name.StartsWith("before-import-")).Where(f => Old(f, TimeSpan.FromDays(90)))) found.Add(new("오래된 정리 전 백업", file.FullName, file.Length));
                var auto = new DirectoryInfo(Path.Combine(backups.FullName, AutoFolder));
                if (auto.Exists) foreach (var file in auto.EnumerateFiles(AutoPrefix + "*.zip").OrderByDescending(f => f.Name).Skip(Math.Max(1, keepAuto))) found.Add(new("오래된 자동 백업", file.FullName, file.Length));
            }
            // This program's own working folders in the temporary folder.
            var scratch = new DirectoryInfo(temp ?? Path.GetTempPath());
            if (scratch.Exists) foreach (var folder in scratch.EnumerateDirectories("kw-couple-*")) if (Old(folder, TimeSpan.FromHours(1))) found.Add(new("임시 폴더의 작업 폴더", folder.FullName, Size(folder), true));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return found.ToArray();
    }
    // Deletes what Scan found. Returns the bytes freed.
    public static long Clean(IEnumerable<Leftover> leftovers)
    {
        long freed = 0;
        foreach (var item in leftovers)
        {
            try { if (item.Folder) { if (!Directory.Exists(item.Path)) continue; Directory.Delete(item.Path, true); } else { if (!File.Exists(item.Path)) continue; File.Delete(item.Path); } freed += item.Bytes; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return freed;
    }
    // How much each part of the settings folder takes.
    public static (string Name, long Bytes, int Files)[] Usage(string root)
    {
        var rows = new List<(string, long, int)>(); var home = new DirectoryInfo(root); if (!home.Exists) return rows.ToArray();
        try
        {
            foreach (var (name, title) in new[] { ("images", "이미지"), ("sounds", "소리·음성"), ("fonts", "글꼴"), ("backups", "백업") })
            {
                var folder = new DirectoryInfo(Path.Combine(root, name)); if (!folder.Exists) continue;
                var files = folder.EnumerateFiles("*", SearchOption.AllDirectories).ToArray(); rows.Add((title, files.Sum(f => f.Length), files.Length));
            }
            var top = home.EnumerateFiles().ToArray(); rows.Add(("설정·기록 파일", top.Sum(f => f.Length), top.Length));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return rows.ToArray();
    }

    public static bool BackupDue(TidyOptions options, DateTime now) =>
        options.AutoBackupDays > 0 && (!DateTime.TryParse(options.LastBackup, out var last) || (now.Date - last.Date).Days >= options.AutoBackupDays || last.Date > now.Date);
    // Writes backups\auto\auto-backup-<time>.zip (settings and chat; pictures and sounds only when asked) and keeps the newest few.
    public static string AutoBackup(string root, TidyOptions options, DateTime now)
    {
        string folder = Path.Combine(root, "backups", AutoFolder); Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, AutoPrefix + now.ToString("yyyyMMdd-HHmmss") + ".zip");
        SettingsBackup.Export(root, target, options.AutoBackupMedia, true);
        foreach (var old in new DirectoryInfo(folder).GetFiles(AutoPrefix + "*.zip").OrderByDescending(f => f.Name).Skip(options.AutoBackupKeep)) try { old.Delete(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return target;
    }
    public static FileInfo[] AutoBackups(string root)
    {
        var folder = new DirectoryInfo(Path.Combine(root, "backups", AutoFolder));
        return folder.Exists ? folder.GetFiles(AutoPrefix + "*.zip").OrderByDescending(f => f.Name).ToArray() : Array.Empty<FileInfo>();
    }
}

// A named set of on/off choices (see QuickToggles) that is switched to in one go: "업무 집중", "발표" and so on.
public sealed class ModeProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public Dictionary<string, bool> Values { get; set; } = new();
    public ModeProfile Snapshot() => new() { Id = Id, Name = Name, Values = new(Values ?? new()) };
    // What a new settings file starts with; all three can be changed or deleted.
    public static List<ModeProfile> Starters() => new()
    {
        new() { Name = "편하게", Values = new() { ["quick"] = true, ["timer"] = true, ["badge"] = true, ["todo"] = true, ["player"] = true, ["typing"] = true, ["voice"] = true, ["local"] = true, ["greetings"] = true, ["bounce"] = true, ["hearts"] = true, ["through"] = false } },
        new() { Name = "업무 집중", Values = new() { ["quick"] = false, ["timer"] = true, ["badge"] = false, ["todo"] = true, ["player"] = false, ["typing"] = false, ["voice"] = false, ["local"] = false, ["greetings"] = false, ["ai-random"] = false, ["duo-talk"] = false, ["bounce"] = false } },
        new() { Name = "발표·화면 공유", Values = new() { ["quick"] = false, ["timer"] = false, ["badge"] = false, ["todo"] = false, ["player"] = false, ["typing"] = false, ["voice"] = false, ["local"] = false, ["greetings"] = false, ["ai-random"] = false, ["ai-nudge"] = false, ["duo-talk"] = false, ["through"] = true } }
    };
}
