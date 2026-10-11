using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace KeyboardWorker;

// Two character presets and how they relate, as one file (.kwcouple, a ZIP): both presets (each packed as a .kwpreset),
// the relationship written by the user, and the pictures of the two together. Their closeness numbers, album and
// chat history stay on this PC; API keys and connections are never part of it.
public static class CouplePackage
{
    public const string Extension = ".kwcouple";
    const string Manifest = "keyboard-worker-couple.json", FirstName = "first" + PresetPackage.Extension, SecondName = "second" + PresetPackage.Extension;
    const long PresetLimit = 400L * 1024 * 1024;
    public sealed record Info(int Version, string Style, string Description, string FirstCalls, string SecondCalls, string Rules, Dictionary<string, string> Scenes);
    public sealed record Result(CharacterPreset First, CharacterPreset Second, PairCard Pair);

    public static void Export(CharacterPreset first, CharacterPreset second, PairCard pair, string target)
    {
        string work = Path.Combine(Path.GetTempPath(), "kw-couple-" + Guid.NewGuid().ToString("N")), temporary = target + ".tmp"; Directory.CreateDirectory(work);
        try
        {
            string one = Path.Combine(work, FirstName), two = Path.Combine(work, SecondName); PresetPackage.Export(first, one); PresetPackage.Export(second, two);
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(one, FirstName, CompressionLevel.NoCompression); zip.CreateEntryFromFile(two, SecondName, CompressionLevel.NoCompression);
                var scenes = new Dictionary<string, string>();
                foreach (var (key, path) in pair.Scenes ?? new())
                {
                    if (!PairScenes.Known(key) || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                    string name = "scenes/" + key + Path.GetExtension(path).ToLowerInvariant(); zip.CreateEntryFromFile(path, name, CompressionLevel.NoCompression); scenes[key] = name;
                }
                using var manifest = zip.CreateEntry(Manifest).Open();
                JsonSerializer.Serialize(manifest, new Info(1, pair.Style ?? "", pair.Description ?? "", pair.FirstCalls ?? "", pair.SecondCalls ?? "", pair.Rules ?? "", scenes));
            }
            File.Move(temporary, target, true);
        }
        catch { try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } throw; }
        finally { try { Directory.Delete(work, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }

    // Reads the file and copies its pictures and sounds into this PC's settings folder. The two presets come back under new ids;
    // the caller adds them to the preset list and stores the relationship for that pair.
    public static Result Import(string archive, string root, IEnumerable<string> existingNames)
    {
        ZipArchive zip; try { zip = ZipFile.OpenRead(archive); } catch (InvalidDataException) { throw new InvalidOperationException("커플 프리셋 파일을 읽을 수 없습니다."); }
        using (zip)
        {
            var entry = zip.GetEntry(Manifest) ?? throw new InvalidOperationException("커플 프리셋 파일이 아닙니다. 관계 창에서 내보낸 " + Extension + " 파일을 골라 주세요.");
            Info? info; try { info = JsonSerializer.Deserialize<Info>(SafeFiles.ReadText(entry, 1024 * 1024)); } catch (Exception e) when (e is JsonException or InvalidDataException) { info = null; }
            if (info == null || info.Version != 1) throw new InvalidOperationException("지원하지 않는 커플 프리셋 형식입니다.");
            var one = zip.GetEntry(FirstName); var two = zip.GetEntry(SecondName); if (one == null || two == null) throw new InvalidOperationException("커플 프리셋 안에 두 캐릭터가 모두 들어 있지 않습니다.");
            string work = Path.Combine(root, "couple-import-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
            try
            {
                string firstFile = Path.Combine(work, FirstName), secondFile = Path.Combine(work, SecondName);
                SafeFiles.Extract(one, firstFile, PresetLimit); SafeFiles.Extract(two, secondFile, PresetLimit);
                var names = existingNames.ToList(); var first = PresetPackage.Import(firstFile, root, names); names.Add(first.Name); var second = PresetPackage.Import(secondFile, root, names);
                string Cut(string? value, int length) { value ??= ""; return value[..Math.Min(value.Length, length)]; }
                var pair = new PairCard { Description = Cut(info.Description, 4000), FirstCalls = Cut(info.FirstCalls, 60), SecondCalls = Cut(info.SecondCalls, 60), Rules = Cut(info.Rules, 2000) };
                if (!string.IsNullOrWhiteSpace(info.Style)) pair.Style = Cut(info.Style.Trim(), 60);
                foreach (var (key, name) in info.Scenes ?? new())
                {
                    if (!PairScenes.Known(key)) continue; var picture = zip.GetEntry(name ?? ""); if (picture == null) continue;
                    string extension = Path.GetExtension(name!).ToLowerInvariant(); if (extension is not ".png" and not ".jpg" and not ".jpeg") continue;
                    if (picture.Length > 20 * 1024 * 1024) throw new InvalidOperationException("커플 프리셋 안의 장면 그림이 너무 큽니다 (장당 20MB 이하).");
                    string temporary = Path.Combine(work, "scene-" + key + extension); SafeFiles.Extract(picture, temporary, 20 * 1024 * 1024); pair.Scenes[key] = ImageAssets.Import(temporary, root);
                }
                return new(first, second, pair);
            }
            finally { try { Directory.Delete(work, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
        }
    }
}
