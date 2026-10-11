using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

// A published version on GitHub: its number, what changed, and the package to download.
public sealed record ReleaseInfo(string Tag, Version Version, string Title, string Notes, string AssetName, string AssetUrl, long Size, string Sha256, string Page);

// Updating by hand: the user asks, the newest release of the program's own repository is looked up, and if they agree its
// package replaces the program files. Nothing here runs on its own - there is no background check and no automatic install.
// Settings are not part of the program folder and are never touched.
public static class Updater
{
    const long MaxPackage = 600L * 1024 * 1024; const string Old = ".kw-old";
    public static string Repository => AppInfo.UpdateRepository;
    public static bool Configured => GitHubLink.Parse("https://github.com/" + Repository) is { Path: "", IsFile: false };
    // "v2.1.0", "2.1", "V2.1.0-beta" -> 2.1.0
    public static Version? ParseVersion(string? tag)
    {
        var match = Regex.Match((tag ?? "").Trim(), @"^[vV]?(\d{1,5}(?:\.\d{1,5}){1,3})", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version)) return null;
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }
    public static bool Newer(ReleaseInfo release, string current) => ParseVersion(current) is not { } mine || release.Version > mine;
    // Reads GitHub's "latest release" answer. The package is the first ZIP for 64-bit Windows, or else the only ZIP.
    public static ReleaseInfo ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement; if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("업데이트 정보를 읽지 못했습니다.");
        string tag = GitHubLink.Text(root, "tag_name"); var version = ParseVersion(tag) ?? throw new InvalidOperationException("최신 버전의 번호를 알 수 없습니다 (" + tag + "). 릴리스 태그는 v2.1.0 같은 형식이어야 합니다.");
        var assets = new List<(string Name, string Url, long Size, string Hash)>();
        if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray())
            {
                string file = GitHubLink.Text(item, "name"), url = GitHubLink.Text(item, "browser_download_url"), digest = GitHubLink.Text(item, "digest");
                if (!file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(url, UriKind.Absolute, out var address) || !Downloader.Allowed(address)) continue;
                assets.Add((file, url, item.TryGetProperty("size", out var size) && size.TryGetInt64(out long bytes) ? bytes : 0, digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : ""));
            }
        if (assets.Count == 0) throw new InvalidOperationException("최신 버전(" + tag + ")에 내려받을 ZIP 파일이 없습니다.");
        var chosen = assets.FirstOrDefault(a => a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase)); if (chosen.Name == null) chosen = assets[0];
        if (chosen.Size > MaxPackage) throw new InvalidOperationException("업데이트 파일이 너무 큽니다.");
        string notes = GitHubLink.Text(root, "body").Replace("\r\n", "\n").Trim(); string title = SafeFiles.OneLine(GitHubLink.Text(root, "name"), 100);
        return new(SafeFiles.OneLine(tag, 40), version, title.Length > 0 ? title : tag, notes[..Math.Min(notes.Length, 3000)], chosen.Name, chosen.Url, chosen.Size, chosen.Hash, GitHubLink.Text(root, "html_url"));
    }
    public static async Task<ReleaseInfo> Check(CancellationToken cancellation)
    {
        if (!Configured) throw new InvalidOperationException("이 프로그램에는 업데이트를 받아 올 주소가 아직 정해져 있지 않습니다.");
        try { return ParseRelease(await Downloader.Text($"https://api.github.com/repos/{Repository}/releases/latest", 1024 * 1024, cancellation)); }
        catch (JsonException) { throw new InvalidOperationException("업데이트 정보를 읽지 못했습니다."); }
        // GitHub answers 404 while the repository has no published release.
        catch (InvalidOperationException e) when (e.Message.Contains("(404)")) { throw new InvalidOperationException("아직 공개된 버전이 없습니다."); }
    }
    // Downloads the package into the settings folder's updates folder and checks it against what GitHub says it should be.
    public static async Task<string> Download(ReleaseInfo release, string root, Action<long, long?>? progress, CancellationToken cancellation)
    {
        string folder = Path.Combine(root, "updates"); if (Directory.Exists(folder)) Directory.Delete(folder, true); Directory.CreateDirectory(folder);
        string package = Path.Combine(folder, "update-" + release.Version + ".zip");
        await Downloader.File(release.AssetUrl, package, MaxPackage, progress, cancellation);
        if (release.Size > 0 && new FileInfo(package).Length != release.Size) throw new InvalidOperationException("내려받은 파일의 크기가 다릅니다. 다시 시도해 주세요.");
        if (release.Sha256.Length > 0)
        {
            string hash; await using (var stream = File.OpenRead(package)) hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation)).ToLowerInvariant();
            if (hash != release.Sha256) throw new InvalidOperationException("내려받은 파일이 GitHub에 올라간 파일과 다릅니다. 업데이트하지 않았습니다.");
        }
        return package;
    }
    // Unpacks the package next to it and returns the folder that holds the program (the package may wrap it in one folder).
    public static string Stage(string package)
    {
        string staging = Path.Combine(Path.GetDirectoryName(package)!, "staged"); if (Directory.Exists(staging)) Directory.Delete(staging, true); Directory.CreateDirectory(staging);
        string prefix = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; long total = 0;
        try
        {
            using var zip = ZipFile.OpenRead(package); if (zip.Entries.Count > 5000) throw new InvalidOperationException("업데이트 파일에 항목이 너무 많습니다.");
            foreach (var entry in zip.Entries)
            {
                if (entry.Name.Length == 0) continue; string target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("업데이트 파일에 허용되지 않는 경로가 있습니다.");
                total += entry.Length; if (total > 2L * 1024 * 1024 * 1024) throw new InvalidOperationException("업데이트 파일을 풀면 너무 큽니다.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); SafeFiles.Extract(entry, target, 500L * 1024 * 1024);
            }
        }
        catch (InvalidDataException) { throw new InvalidOperationException("업데이트 파일이 ZIP 형식이 아닙니다."); }
        bool Program(string folder) => File.Exists(Path.Combine(folder, "KeyboardWorker.exe")) && File.Exists(Path.Combine(folder, "KeyboardWorker.dll"));
        if (Program(staging)) return staging;
        var inner = Directory.GetDirectories(staging); if (inner.Length == 1 && Directory.GetFiles(staging).Length == 0 && Program(inner[0])) return inner[0];
        throw new InvalidOperationException("업데이트 파일 안에 프로그램(KeyboardWorker.exe)이 없습니다.");
    }
    // Puts the staged files over the program's. A file in use cannot be overwritten but can be renamed, so each old file is set
    // aside first ("….kw-old", removed at the next start). If anything fails, every file is put back as it was.
    public static int Apply(string staged, string program)
    {
        staged = Path.GetFullPath(staged); program = Path.GetFullPath(program); var done = new List<(string Target, string? Aside)>();
        try
        {
            foreach (string source in Directory.GetFiles(staged, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(program, Path.GetRelativePath(staged, source)), aside = target + Old; Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                bool existed = File.Exists(target); if (existed) { if (File.Exists(aside)) File.Delete(aside); File.Move(target, aside); }
                done.Add((target, existed ? aside : null)); File.Copy(source, target, true);
            }
            return done.Count;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            foreach (var (target, aside) in Enumerable.Reverse(done))
            {
                try { if (File.Exists(target)) File.Delete(target); if (aside != null && File.Exists(aside)) File.Move(aside, target); } catch (Exception again) when (again is IOException or UnauthorizedAccessException) { }
            }
            throw new InvalidOperationException("프로그램 폴더에 쓸 수 없어 업데이트하지 못했습니다 (원래대로 되돌렸어요). 프로그램 폴더가 쓰기 금지 위치에 있다면 다른 곳으로 옮겨 주세요. " + e.Message);
        }
    }
    // After an update: the files set aside and the downloaded package are removed. Harmless when there is nothing to remove.
    public static int CleanUp(string program, string root)
    {
        int removed = 0;
        try
        {
            if (Directory.Exists(program)) foreach (string old in Directory.GetFiles(program, "*" + Old, SearchOption.AllDirectories)) { try { File.Delete(old); removed++; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
            string updates = Path.Combine(root, "updates"); if (Directory.Exists(updates)) { Directory.Delete(updates, true); removed++; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return removed;
    }
}
