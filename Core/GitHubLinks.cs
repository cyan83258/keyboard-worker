using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeyboardWorker;

// A place on GitHub that a user pasted: one file, a folder, or a whole repository.
// Branch is empty for "the repository's default branch".
public sealed record GitHubLink(string Owner, string Repo, string Branch, string Path, bool IsFile)
{
    static readonly Regex name = new(@"^[A-Za-z0-9_.-]{1,100}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    static bool Safe(string part) => name.IsMatch(part) && part is not ("." or "..");
    // Understands the addresses people copy from the browser:
    //   github.com/owner/repo                      the repository
    //   github.com/owner/repo/tree/branch/folder   a folder
    //   github.com/owner/repo/blob/branch/a.json   a file      (also /raw/)
    //   raw.githubusercontent.com/owner/repo/branch/a.json
    public static GitHubLink? Parse(string? url)
    {
        url = (url ?? "").Trim(); if (url.Length is 0 or > 500) return null;
        if (!url.Contains("://")) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0) return null;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray(); string host = uri.Host.ToLowerInvariant();
        if (parts.Any(p => p is "." or ".." || p.Contains('\\'))) return null;
        if (host == "raw.githubusercontent.com")
        {
            // refs/heads/<branch>/… is the long way of writing <branch>/…
            if (parts.Length > 4 && parts[2] == "refs" && parts[3] == "heads") parts = parts.Take(2).Concat(parts.Skip(4)).ToArray();
            return parts.Length >= 4 && Safe(parts[0]) && Safe(parts[1]) ? new(parts[0], parts[1], parts[2], string.Join('/', parts.Skip(3)), true) : null;
        }
        if (host is not ("github.com" or "www.github.com") || parts.Length < 2 || !Safe(parts[0])) return null;
        string repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1]; if (!Safe(repo)) return null;
        if (parts.Length == 2) return new(parts[0], repo, "", "", false);
        if (parts.Length >= 4 && parts[2] is "blob" or "raw" or "tree") return parts.Length == 4 && parts[2] != "tree" ? null : new(parts[0], repo, parts[3], string.Join('/', parts.Skip(4)), parts[2] != "tree");
        return null;
    }
    static string Escaped(string path) => string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
    public string Raw => $"https://raw.githubusercontent.com/{Owner}/{Repo}/{Uri.EscapeDataString(Branch)}/{Escaped(Path)}";
    // The folder's listing through GitHub's API (also tells the default branch's files when no branch was given).
    public string Listing => $"https://api.github.com/repos/{Owner}/{Repo}/contents/{Escaped(Path)}" + (Branch.Length > 0 ? "?ref=" + Uri.EscapeDataString(Branch) : "");
    public string Page => $"https://github.com/{Owner}/{Repo}";
    // The JSON files of a listing, as (name, address to download from). Files meant as mods ("….kwmod.json") win over other JSON files.
    public static (string Name, string Url)[] JsonFiles(string listing, int limit = 20)
    {
        using var document = JsonDocument.Parse(listing); if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<(string, string)>();
        var files = new List<(string Name, string Url)>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Text(item, "type") != "file") continue; string file = Text(item, "name"), url = Text(item, "download_url");
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(url, UriKind.Absolute, out var address) || !Downloader.Allowed(address)) continue;
            if (item.TryGetProperty("size", out var size) && size.TryGetInt64(out long bytes) && bytes > ModFile.MaxBytes) continue;
            files.Add((file, url));
        }
        var marked = files.Where(f => f.Name.EndsWith(".kwmod.json", StringComparison.OrdinalIgnoreCase)).ToArray();
        return (marked.Length > 0 ? marked : files.ToArray()).Take(limit).ToArray();
    }
    internal static string Text(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
