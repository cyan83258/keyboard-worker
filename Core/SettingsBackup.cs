using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyboardWorker;

// One-file backup of the settings folder. An import is staged as a pending archive and applied on the next start,
// before any store is loaded, so the running app can never write over the restored files.
public static class SettingsBackup
{
    const string Manifest = "keyboard-worker-backup.json", PendingName = "pending-import.zip", SafetyFolder = "backups";
    // Keys: whether the (encrypted) API keys are inside. Backups made before that choice existed always contain them.
    public sealed record Info(int Version, string Root, DateTimeOffset Created, bool Media, bool Chat, bool Keys = true);
    const string KeyFile = "chat-settings.json";
    static JsonArray? Connections(JsonNode? settings) => settings is JsonObject map && map["Connections"] is JsonArray list ? list : null;
    static string? Text(JsonNode? node, string name) => node is JsonObject map && map[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    // The chat settings without any API key.
    public static string WithoutKeys(string json)
    {
        JsonNode? root; try { root = JsonNode.Parse(json); } catch (JsonException) { return json; }
        if (Connections(root) is not JsonArray list) return json;
        foreach (var connection in list) if (connection is JsonObject map && map.ContainsKey("Secret")) map["Secret"] = "";
        return root!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    // A backup without keys must not erase the keys already saved here: keep them for connections with the same name and address.
    public static string KeepLocalKeys(string imported, string local)
    {
        JsonNode? root, current; try { root = JsonNode.Parse(imported); current = JsonNode.Parse(local); } catch (JsonException) { return imported; }
        if (Connections(root) is not JsonArray list || Connections(current) is not JsonArray saved) return imported;
        foreach (var connection in list)
        {
            if (connection is not JsonObject map || !string.IsNullOrEmpty(Text(map, "Secret"))) continue;
            var match = saved.FirstOrDefault(s => Text(s, "Name") == Text(map, "Name") && Text(s, "Endpoint") == Text(map, "Endpoint"));
            if (!string.IsNullOrEmpty(Text(match, "Secret"))) map["Secret"] = Text(match, "Secret");
        }
        return root!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    static string Pending => Path.Combine(Storage.Root, PendingName);

    static bool IsMedia(string relative) => relative.StartsWith("images/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("sounds/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("fonts/", StringComparison.OrdinalIgnoreCase);
    static bool Skipped(string relative, bool media, bool chat) =>
        relative.StartsWith(SafetyFolder + "/", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || relative.Contains(".broken-", StringComparison.OrdinalIgnoreCase)
        || relative.Equals(PendingName, StringComparison.OrdinalIgnoreCase) || relative.Equals("error-log.txt", StringComparison.OrdinalIgnoreCase) || relative.Equals("startup-timing.txt", StringComparison.OrdinalIgnoreCase) || relative.Equals(Manifest, StringComparison.OrdinalIgnoreCase)
        || (!media && IsMedia(relative)) || (!chat && (relative.Equals("chat-history.json", StringComparison.OrdinalIgnoreCase) || relative.Equals("stage-chat.json", StringComparison.OrdinalIgnoreCase) || relative.Equals("stage-clips.json", StringComparison.OrdinalIgnoreCase)));

    public static void Export(string root, string target, bool media, bool chat, bool keys = true)
    {
        string temporary = target + ".tmp"; target = Path.GetFullPath(target);
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (Skipped(relative, media, chat) || Path.GetFullPath(file).Equals(target, StringComparison.OrdinalIgnoreCase)) continue;
                    // Pictures and sounds are already compressed; storing them keeps a large backup fast.
                    if (!keys && relative.Equals(KeyFile, StringComparison.OrdinalIgnoreCase)) { using var writer = new StreamWriter(zip.CreateEntry(relative, CompressionLevel.Optimal).Open()); writer.Write(WithoutKeys(File.ReadAllText(file))); continue; }
                    zip.CreateEntryFromFile(file, relative, IsMedia(relative) ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                }
                using var manifest = zip.CreateEntry(Manifest).Open(); JsonSerializer.Serialize(manifest, new Info(1, Path.GetFullPath(root), DateTimeOffset.Now, media, chat, keys));
            }
            File.Move(temporary, target, true);
        }
        catch { try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } throw; }
    }

    static Info Read(ZipArchive zip)
    {
        var entry = zip.GetEntry(Manifest) ?? throw new InvalidOperationException("Keyboard Worker 백업 파일이 아닙니다.");
        using var stream = entry.Open(); var info = JsonSerializer.Deserialize<Info>(stream);
        if (info == null || info.Version != 1 || string.IsNullOrWhiteSpace(info.Root)) throw new InvalidOperationException("지원하지 않는 백업 형식입니다.");
        return info;
    }
    public static Info Describe(string archive)
    {
        try { using var zip = ZipFile.OpenRead(archive); return Read(zip); }
        catch (InvalidDataException) { throw new InvalidOperationException("ZIP 파일을 읽을 수 없습니다."); }
    }
    public static void Stage(string archive) { Describe(archive); Directory.CreateDirectory(Storage.Root); File.Copy(archive, Pending, true); }
    // Returns an error message when a staged import could not be applied; null when there was nothing to do or it worked.
    public static string? ApplyPending()
    {
        string pending = Pending; if (!File.Exists(pending)) return null;
        try { Import(Storage.Root, pending); return null; }
        catch (Exception e) { return e.Message; }
        finally { try { File.Delete(pending); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }

    // Files in the archive replace the same files in root; nothing else is deleted. The previous settings are kept in backups\.
    public static void Import(string root, string archive)
    {
        using var zip = ZipFile.OpenRead(archive); var info = Read(zip);
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (zip.Entries.Count > MaxEntries) throw new InvalidOperationException("백업 파일에 항목이 너무 많습니다.");
        var files = zip.Entries.Where(e => e.Name.Length > 0 && !Skipped(e.FullName.Replace('\\', '/'), true, true)).Select(e => (Entry: e, Target: Path.GetFullPath(Path.Combine(root, e.FullName)))).ToArray();
        if (files.Any(f => !f.Target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("백업 파일에 허용되지 않는 경로가 있습니다.");
        // Only what a backup is made of comes in: settings (.json at the top) and copied pictures and sounds. Anything else is left out.
        files = files.Where(f => Allowed(f.Entry.FullName.Replace('\\', '/'))).ToArray();
        string safety = Path.Combine(root, SafetyFolder); Directory.CreateDirectory(safety);
        Export(root, Path.Combine(safety, "before-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip"), false, true);
        foreach (var old in new DirectoryInfo(safety).GetFiles("before-import-*.zip").OrderByDescending(f => f.Name).Skip(5)) try { old.Delete(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (var (entry, target) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!target.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) { SafeFiles.Extract(entry, target, MaxMedia); continue; }
            string text = DropNetworkPaths(Rebase(SafeFiles.ReadText(entry, MaxJson), info.Root, root));
            if (!info.Keys && Path.GetFileName(target).Equals(KeyFile, StringComparison.OrdinalIgnoreCase) && File.Exists(target)) text = KeepLocalKeys(text, File.ReadAllText(target));
            File.WriteAllText(target + ".tmp", text); File.Move(target + ".tmp", target, true);
        }
    }

    const int MaxEntries = 20000; const long MaxMedia = 25L * 1024 * 1024, MaxJson = 64L * 1024 * 1024;
    static bool Allowed(string relative)
    {
        string extension = Path.GetExtension(relative).ToLowerInvariant(); int folders = relative.Count(c => c == '/');
        if (folders == 0) return extension == ".json";
        if (folders != 1) return false;
        if (relative.StartsWith("fonts/", StringComparison.OrdinalIgnoreCase)) return extension is ".ttf" or ".otf";
        if (relative.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)) return extension == ".json";   // mod files are data, checked again when they are loaded
        return relative.StartsWith("images/", StringComparison.OrdinalIgnoreCase) ? extension is ".png" or ".jpg" or ".jpeg" : relative.StartsWith("sounds/", StringComparison.OrdinalIgnoreCase) && extension is ".mp3" or ".wav";
    }
    // Saved picture and sound paths that point to a network location are emptied (see SafeFiles.IsNetworkPath).
    // Only the places that hold file paths are touched, so a web address in a chat line or a memo stays as it is.
    public static string DropNetworkPaths(string json)
    {
        JsonNode? root; try { root = JsonNode.Parse(json); } catch (JsonException) { return json; }
        if (root == null) return json; bool changed = false;
        bool Remote(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) && SafeFiles.IsNetworkPath(text);
        void Walk(JsonNode? node, string parent)
        {
            if (node is JsonObject map)
                foreach (var key in map.Select(pair => pair.Key).ToArray())
                {
                    bool path = key is "SoundPath" or "SkinPath" or "Path" or "ImagePath" or "BubbleFontPath" or "StageFontPath" or "Backdrop" || parent is "ImagePaths" or "Images" or "VoicePaths" or "Scenes" || key is "Background";
                    if (path && Remote(map[key])) { map[key] = ""; changed = true; } else Walk(map[key], key);
                }
            else if (node is JsonArray list)
            {
                // A list of paths (the backgrounds of a character page): the remote ones are emptied in place.
                if (parent is "Backgrounds") { for (int i = 0; i < list.Count; i++) if (Remote(list[i])) { list[i] = ""; changed = true; } }
                else foreach (var item in list) Walk(item, parent);
            }
        }
        Walk(root, ""); return changed ? root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) : json;
    }
    // Hosts the AI connections in a backup point to, so the user can see where requests would go before importing.
    public static string[] ConnectionHosts(string archive)
    {
        try
        {
            using var zip = ZipFile.OpenRead(archive); var entry = zip.GetEntry(KeyFile); if (entry == null) return Array.Empty<string>();
            return HostsOf(SafeFiles.ReadText(entry, MaxJson));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or JsonException) { return Array.Empty<string>(); }
    }
    public static string[] HostsOf(string chatSettings)
    {
        JsonNode? root; try { root = JsonNode.Parse(chatSettings); } catch (JsonException) { return Array.Empty<string>(); }
        if (Connections(root) is not JsonArray list) return Array.Empty<string>();
        return list.Select(c => Text(c, "Endpoint") ?? "").Select(address => Uri.TryCreate(address.Replace("{model}", "m"), UriKind.Absolute, out var uri) ? uri.Host : "").Where(host => host.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    // Saved image and sound paths are absolute, so point them at this PC's settings folder when the backup came from another one.
    public static string Rebase(string json, string from, string to)
    {
        from = Path.TrimEndingDirectorySeparator(from) + Path.DirectorySeparatorChar; to = Path.TrimEndingDirectorySeparator(Path.GetFullPath(to)) + Path.DirectorySeparatorChar;
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return json;
        JsonNode? root; try { root = JsonNode.Parse(json); } catch (JsonException) { return json; }
        if (root == null) return json;
        string? Moved(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) && text != null && text.StartsWith(from, StringComparison.OrdinalIgnoreCase) ? to + text[from.Length..] : null;
        void Walk(JsonNode? node)
        {
            if (node is JsonObject map) foreach (var key in map.Select(pair => pair.Key).ToArray()) { if (Moved(map[key]) is string moved) map[key] = moved; else Walk(map[key]); }
            else if (node is JsonArray list) for (int i = 0; i < list.Count; i++) { if (Moved(list[i]) is string moved) list[i] = moved; else Walk(list[i]); }
        }
        Walk(root); return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
