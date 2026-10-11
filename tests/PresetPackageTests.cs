using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

public static class PresetPackageTests
{
    static void Picture(string path, byte tone)
    {
        var pixels = new byte[32 * 32 * 4]; for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = tone; pixels[i + 1] = (byte)(i / 16); pixels[i + 3] = 255; }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, pixels, 32 * 4)));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        string home = Path.Combine(root, "preset-home"), other = Path.Combine(root, "preset-other"), file = Path.Combine(root, "hero" + PresetPackage.Extension);
        Directory.CreateDirectory(Path.Combine(home, "images"));
        Directory.CreateDirectory(other);
        string idle = Path.Combine(home, "images", "idle.png"), happy = Path.Combine(home, "images", "happy.png"); Picture(idle, 10); Picture(happy, 200);
        var preset = new CharacterPreset { Name = "영웅왕", Advanced = true, Flip = true, NudgeLines = "일어나라", PatLines = "흥", Motion = new MotionSettings(150, 300, true, 7, true, 9, 3) };
        preset.Profile.Name = "영웅"; preset.Profile.Speech = "오만한 말투"; preset.Profile.UserNotes = "기억 메모";
        preset.Images["idle"] = idle;
        preset.Images["typing-left"] = idle;
        preset.Images["happy"] = happy;
        preset.Images["sad"] = Path.Combine(home, "images", "missing.png");
        preset.ImageAdjustments["happy"] = new ImageAdjustment { Scale = 120, X = 5 };
        PresetPackage.Export(preset, file);
        using (var zip = ZipFile.OpenRead(file))
        {
            Check(zip.Entries.Count == 3 && zip.Entries.Count(e => e.FullName.StartsWith("images/")) == 2, "A preset file holds the preset and each picture once");
            string manifest; using (var reader = new StreamReader(zip.GetEntry("keyboard-worker-preset.json")!.Open())) manifest = reader.ReadToEnd();
            Check(!manifest.Contains("preset-home") && !manifest.Contains("Secret") && !manifest.Contains("Connections"), "The file carries no folder path, API key or connection");
        }
        Check(preset.Images["idle"] == idle, "Exporting does not change the preset itself");
        var imported = PresetPackage.Import(file, other, new[] { "영웅왕", "영웅왕 2" });
        Check(imported.Id != preset.Id && imported.Name == "영웅왕 3", "An imported preset gets its own id and a name that is not taken");
        Check(imported.Profile.Name == "영웅" && imported.Profile.Speech == "오만한 말투" && imported.Profile.UserNotes == "기억 메모" && imported.NudgeLines == "일어나라" && imported.PatLines == "흥", "Profile and lines come along");
        Check(imported.Advanced && imported.Flip && imported.Motion == preset.Motion && imported.ImageAdjustments["happy"].Scale == 120, "Picture options and motion settings come along");
        string folder = Path.Combine(Path.GetFullPath(other), "images");
        Check(imported.Images.Count == 3 && imported.Images.Values.All(p => Path.GetDirectoryName(p) == folder && File.Exists(p)) && !imported.Images.ContainsKey("sad"), "Pictures are copied into this PC's settings folder; a missing one is skipped");
        Check(imported.Images["idle"] == imported.Images["typing-left"] && imported.Images["idle"] != imported.Images["happy"] && Directory.GetFiles(folder).Length == 2, "A picture shared by two slots is stored once");
        Check(File.ReadAllBytes(imported.Images["happy"]).AsSpan().SequenceEqual(File.ReadAllBytes(happy)) && !Directory.GetDirectories(other).Any(d => Path.GetFileName(d).StartsWith("preset-import-")), "Pictures arrive unchanged and no working folder is left behind");
        PresetPackage.Import(file, other, Array.Empty<string>()); Check(Directory.GetFiles(folder).Length == 2, "Importing the same preset again does not duplicate its pictures");
        var store = new PresetStore(other);
        store.AddImported(imported);
        OrderedStore.FlushAsync().GetAwaiter().GetResult();
        Check(new PresetStore(other).Items.Single().Name == "영웅왕 3", "An imported preset is saved in the preset list");

        string backup = Path.Combine(root, "not-a-preset.zip"); using (var zip = ZipFile.Open(backup, ZipArchiveMode.Create)) zip.CreateEntry("settings.json");
        bool refused = false;
        try { PresetPackage.Import(backup, other, Array.Empty<string>()); } catch (InvalidOperationException) { refused = true; } Check(refused, "A ZIP that is not a preset file is refused");
        string text = Path.Combine(root, "plain" + PresetPackage.Extension); File.WriteAllText(text, "not a zip");
        refused = false; try { PresetPackage.Import(text, other, Array.Empty<string>()); } catch (InvalidOperationException) { refused = true; } Check(refused, "A file that is not a ZIP is refused");
        string odd = Path.Combine(root, "odd" + PresetPackage.Extension);
        using (var zip = ZipFile.Open(odd, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("../outside.png").Open())) writer.Write("x");
            var strange = new CharacterPreset { Name = "" }; strange.Images["idle"] = "../outside.png"; strange.Images["unknown-slot"] = "../outside.png"; strange.Profile = null!;
            using var manifest = zip.CreateEntry("keyboard-worker-preset.json").Open(); JsonSerializer.Serialize(manifest, new PresetPackage.Info(1, strange));
        }
        bool invalid = false; CharacterPreset? safe = null; try { safe = PresetPackage.Import(odd, other, Array.Empty<string>()); } catch { invalid = true; }
        Check((invalid || safe!.Images.Count == 0) && !File.Exists(Path.Combine(root, "outside.png")) && Directory.GetFiles(folder).Length == 2, "A picture that is not a real image, or a name pointing outside, never lands on disk");
        return count;
    }
}
