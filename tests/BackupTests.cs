using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace KeyboardWorker;

public static class BackupTests
{
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        string source = Path.Combine(root, "backup-source"), target = Path.Combine(root, "backup-target"), zip = Path.Combine(root, "backup.zip");
        Directory.CreateDirectory(Path.Combine(source, "images")); Directory.CreateDirectory(target);
        string picture = Path.Combine(source, "images", "a.png"); File.WriteAllText(picture, "picture");
        File.WriteAllText(Path.Combine(source, "settings.json"), JsonSerializer.Serialize(new Preferences { Size = 222, ImagePaths = new() { ["idle"] = picture }, SoundPath = @"C:\elsewhere\tap.wav" }));
        File.WriteAllText(Path.Combine(source, "chat-history.json"), "[]"); File.WriteAllText(Path.Combine(source, "pending-import.zip"), "staged");
        SettingsBackup.Export(source, zip, true, false);
        var info = SettingsBackup.Describe(zip); Check(info.Media && !info.Chat && info.Version == 1, "Backup manifest describes contents");
        using (var archive = ZipFile.OpenRead(zip)) Check(archive.GetEntry("images/a.png") != null && archive.GetEntry("chat-history.json") == null && archive.GetEntry("pending-import.zip") == null, "Export honors options and skips staging files");
        File.WriteAllText(Path.Combine(target, "settings.json"), JsonSerializer.Serialize(new Preferences { Size = 111 })); File.WriteAllText(Path.Combine(target, "pet-game.json"), "{}");
        SettingsBackup.Import(target, zip);
        var restored = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(target, "settings.json")))!;
        Check(restored.Size == 222, "Import replaces settings");
        Check(restored.ImagePaths["idle"] == Path.Combine(Path.GetFullPath(target), "images", "a.png") && File.ReadAllText(restored.ImagePaths["idle"]) == "picture", "Imported asset paths follow the new settings folder");
        Check(restored.SoundPath == @"C:\elsewhere\tap.wav", "Paths outside the settings folder are untouched");
        Check(File.Exists(Path.Combine(target, "pet-game.json")), "Files missing from the backup are kept");
        var safety = Directory.GetFiles(Path.Combine(target, "backups"), "before-import-*.zip"); Check(safety.Length == 1, "Previous settings saved before import");
        using (var archive = ZipFile.OpenRead(safety[0])) using (var reader = new StreamReader(archive.GetEntry("settings.json")!.Open())) Check(reader.ReadToEnd().Contains("111"), "Safety backup holds the replaced settings");
        string escaping = Path.Combine(root, "escaping.zip");
        using (var archive = ZipFile.Open(escaping, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("../escaped.json").Open())) writer.Write("{}");
            using var manifest = archive.CreateEntry("keyboard-worker-backup.json").Open(); JsonSerializer.Serialize(manifest, new SettingsBackup.Info(1, source, DateTimeOffset.Now, false, false));
        }
        bool refused = false; try { SettingsBackup.Import(target, escaping); } catch (InvalidOperationException) { refused = true; }
        Check(refused && !File.Exists(Path.Combine(root, "escaped.json")), "Archive paths outside the settings folder are refused");
        string plain = Path.Combine(root, "plain.zip"); using (var archive = ZipFile.Open(plain, ZipArchiveMode.Create)) archive.CreateEntry("settings.json");
        bool rejected = false; try { SettingsBackup.Describe(plain); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "ZIP without a backup manifest is rejected");
        // API keys can be left out of a backup, and such a backup never erases the keys saved on this PC.
        string keySource = Path.Combine(root, "key-source"), keyTarget = Path.Combine(root, "key-target"), keyless = Path.Combine(root, "keyless.zip");
        Directory.CreateDirectory(keySource);
        Directory.CreateDirectory(keyTarget);
        var withKeys = new ChatPreferences(); withKeys.Connections[0].Secret = "ENCRYPTED-ONE"; withKeys.Connections[1].Secret = "ENCRYPTED-TWO"; withKeys.Profile.Name = "백업 친구";
        File.WriteAllText(Path.Combine(keySource, "chat-settings.json"), JsonSerializer.Serialize(withKeys));
        SettingsBackup.Export(keySource, keyless, false, false, false);
        string stored; using (var archive = ZipFile.OpenRead(keyless)) using (var reader = new StreamReader(archive.GetEntry("chat-settings.json")!.Open())) stored = reader.ReadToEnd();
        Check(!stored.Contains("ENCRYPTED") && stored.Contains(withKeys.Connections[0].Name) && !SettingsBackup.Describe(keyless).Keys, "A backup made without keys contains no key at all");
        Check(File.ReadAllText(Path.Combine(keySource, "chat-settings.json")).Contains("ENCRYPTED-ONE"), "Leaving keys out of a backup does not touch the saved settings");
        var local = new ChatPreferences(); local.Connections[0].Secret = "LOCAL-KEY"; local.Connections[1].Name = "Renamed"; local.Connections[1].Secret = "OTHER-LOCAL";
        File.WriteAllText(Path.Combine(keyTarget, "chat-settings.json"), JsonSerializer.Serialize(local)); SettingsBackup.Import(keyTarget, keyless);
        var merged = JsonSerializer.Deserialize<ChatPreferences>(File.ReadAllText(Path.Combine(keyTarget, "chat-settings.json")))!;
        Check(merged.Profile.Name == "백업 친구" && merged.Connections[0].Secret == "LOCAL-KEY", "Importing a backup without keys keeps the key already saved for the same connection");
        Check(merged.Connections[1].Secret == "", "A connection that does not match gets no key");
        string keyed = Path.Combine(root, "keyed.zip"); SettingsBackup.Export(keySource, keyed, false, false); SettingsBackup.Import(keyTarget, keyed);
        Check(SettingsBackup.Describe(keyed).Keys && JsonSerializer.Deserialize<ChatPreferences>(File.ReadAllText(Path.Combine(keyTarget, "chat-settings.json")))!.Connections[0].Secret == "ENCRYPTED-ONE", "A backup with keys restores them as before");
        Check(JsonSerializer.Deserialize<SettingsBackup.Info>("{\"Version\":1,\"Root\":\"C:/x\",\"Created\":\"2026-01-01T00:00:00+00:00\",\"Media\":false,\"Chat\":false}")!.Keys, "Older backups are known to contain keys");
        var line = new TimelineState(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60), 1000, true, true);
        Check(line.Now(6000) == TimeSpan.FromSeconds(15) && line.Now(999999) == TimeSpan.FromSeconds(60), "Playing timeline advances locally and stops at the end");
        Check((line with { Playing = false }).Now(6000) == TimeSpan.FromSeconds(10) && !new TimelineState().Available, "Paused timeline holds its position");
        Check(AppVolume.Get(_ => false) == null && !AppVolume.Set(_ => false, 50), "Volume mixer lookup without a matching app changes nothing");
        return count;
    }
}
