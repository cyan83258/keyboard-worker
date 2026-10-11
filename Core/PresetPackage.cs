using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace KeyboardWorker;

// One character preset as a single file (.kwpreset, a ZIP): the preset with its pictures and voice clips, and nothing else.
// API keys, connections and chat history are not part of a preset, so they are never in the file.
public static class PresetPackage
{
    public const string Extension = ".kwpreset";
    const string Manifest = "keyboard-worker-preset.json";
    // Inside the file the preset's picture paths are replaced by the names of the pictures stored next to it.
    public sealed record Info(int Version, CharacterPreset Preset);

    public static void Export(CharacterPreset preset, string target)
    {
        string temporary = target + ".tmp"; var copy = preset.Snapshot(); var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var slots = new Dictionary<string, string>();
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var (slot, path) in preset.Images)
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                    string full = Path.GetFullPath(path);
                    // A picture used by several slots is stored once.
                    if (!names.TryGetValue(full, out var name)) { name = "images/" + names.Count + Path.GetExtension(full).ToLowerInvariant(); names[full] = name; zip.CreateEntryFromFile(full, name, CompressionLevel.NoCompression); }
                    slots[slot] = name;
                }
                copy.Images = slots; var voices = new Dictionary<string, string>();
                foreach (var (slot, path) in preset.VoicePaths ?? new())
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue; string full = Path.GetFullPath(path);
                    if (!names.TryGetValue(full, out var name)) { name = "voices/" + names.Count + Path.GetExtension(full).ToLowerInvariant(); names[full] = name; zip.CreateEntryFromFile(full, name, CompressionLevel.NoCompression); }
                    voices[slot] = name;
                }
                copy.VoicePaths = voices;
                using var manifest = zip.CreateEntry(Manifest).Open(); JsonSerializer.Serialize(manifest, new Info(1, copy));
            }
            File.Move(temporary, target, true);
        }
        catch { try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } throw; }
    }

    // Reads a preset file, copies its pictures into this PC's settings folder and returns the preset under a new id.
    // The caller adds it to the preset list; the current character is not changed.
    public static CharacterPreset Import(string archive, string root, IEnumerable<string> existingNames)
    {
        ZipArchive zip; try { zip = ZipFile.OpenRead(archive); } catch (InvalidDataException) { throw new InvalidOperationException("캐릭터 프리셋 파일을 읽을 수 없습니다."); }
        using (zip)
        {
            var entry = zip.GetEntry(Manifest) ?? throw new InvalidOperationException("캐릭터 프리셋 파일이 아닙니다. 프리셋 창에서 내보낸 " + Extension + " 파일을 골라 주세요.");
            if (entry.Length > 4 * 1024 * 1024) throw new InvalidOperationException("프리셋 정보가 너무 큽니다.");
            Info? info; try { info = JsonSerializer.Deserialize<Info>(SafeFiles.ReadText(entry, 4 * 1024 * 1024)); } catch (Exception e) when (e is JsonException or InvalidDataException) { info = null; }
            var preset = info?.Preset; if (info == null || info.Version != 1 || preset == null) throw new InvalidOperationException("지원하지 않는 프리셋 파일 형식입니다.");
            preset.Id = Guid.NewGuid().ToString("N"); preset.Profile ??= new(); preset.Images ??= new(); preset.ImageAdjustments ??= new(); preset.NudgeLines ??= "";
            // A preset is something people pass around: keep every text field to a sane length.
            foreach (var property in typeof(Persona).GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanWrite)) { string value = (string?)property.GetValue(preset.Profile) ?? ""; property.SetValue(preset.Profile, value[..Math.Min(value.Length, property.Name == "Name" || property.Name == "UserName" ? 100 : 8000)]); }
            preset.NudgeLines = preset.NudgeLines[..Math.Min(preset.NudgeLines.Length, 10000)]; if (preset.PatLines != null) preset.PatLines = preset.PatLines[..Math.Min(preset.PatLines.Length, 10000)];
            preset.Profile.Prompts = (preset.Profile.Prompts ?? new()).Normalize(); preset.LineBook = LineBook.Normalize(preset.LineBook);
            var clips = (preset.VoicePaths ?? new()).Where(p => VoiceSlots.All.Any(s => s.Key == p.Key) && !string.IsNullOrWhiteSpace(p.Value)).ToArray();
            var known = ImageAssets.Basic.Concat(ImageAssets.Advanced).Concat(ImageAssets.Music).Select(s => s.Key).ToHashSet();
            var wanted = preset.Images.Where(p => known.Contains(p.Key) && !string.IsNullOrWhiteSpace(p.Value)).ToArray();
            string work = Path.Combine(root, "preset-import-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
            try
            {
                var stored = new Dictionary<string, string>(); var copied = new Dictionary<string, string>();
                foreach (var (slot, name) in wanted)
                {
                    if (!copied.TryGetValue(name, out var path))
                    {
                        var picture = zip.GetEntry(name); if (picture == null) continue;
                        string extension = Path.GetExtension(name).ToLowerInvariant(); if (extension is not ".png" and not ".jpg" and not ".jpeg") continue;
                        if (picture.Length > 20 * 1024 * 1024) throw new InvalidOperationException("프리셋 안의 이미지가 너무 큽니다 (장당 20MB 이하).");
                        // The picture gets a name chosen here, so a name inside the file can never point outside the folder.
                        string temporary = Path.Combine(work, copied.Count + extension); SafeFiles.Extract(picture, temporary, 20 * 1024 * 1024);
                        path = ImageAssets.Import(temporary, root); copied[name] = path;
                    }
                    stored[slot] = path;
                }
                var voices = new Dictionary<string, string>();
                foreach (var (slot, name) in clips)
                {
                    if (!copied.TryGetValue(name, out var path))
                    {
                        var clip = zip.GetEntry(name); if (clip == null) continue;
                        string extension = Path.GetExtension(name).ToLowerInvariant(); if (extension is not ".mp3" and not ".wav") continue;
                        if (clip.Length > 20 * 1024 * 1024) throw new InvalidOperationException("프리셋 안의 음성 파일이 너무 큽니다 (개당 20MB 이하).");
                        string temporary = Path.Combine(work, "v" + copied.Count + extension); SafeFiles.Extract(clip, temporary, 20 * 1024 * 1024);
                        // A file that is not really MP3 / WAV is left out rather than failing the whole preset.
                        try { path = SoundAssets.Import(temporary, root); } catch (InvalidDataException) { continue; }
                        copied[name] = path;
                    }
                    voices[slot] = path;
                }
                preset.VoicePaths = voices;
                preset.Images = stored; preset.ImageAdjustments = preset.ImageAdjustments.Where(p => known.Contains(p.Key) && p.Value != null).ToDictionary(p => p.Key, p => p.Value.Normalize());
            }
            finally { try { Directory.Delete(work, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
            string name0 = string.IsNullOrWhiteSpace(preset.Name) ? preset.Profile.Name : preset.Name.Trim();
            if (string.IsNullOrWhiteSpace(name0)) name0 = "가져온 프리셋";
            name0 = name0[..Math.Min(name0.Length, 70)];
            var taken = existingNames.ToHashSet(); string unique = name0; for (int n = 2; taken.Contains(unique); n++) unique = name0 + " " + n;
            preset.Name = unique; return preset;
        }
    }
}
