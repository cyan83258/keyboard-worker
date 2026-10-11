using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Threading;

namespace KeyboardWorker;

public static class SoundPresetTests
{
    public static double VerifyAudio(string path)
    {
        var player = new MediaPlayer(); var frame = new DispatcherFrame(); bool opened = false; string error = "";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        player.MediaOpened += (_, _) => { opened = true; frame.Continue = false; };
        player.MediaFailed += (_, e) => { error = e.ErrorException.Message; frame.Continue = false; };
        try
        {
            player.Open(new Uri(Path.GetFullPath(path))); timer.Start(); Dispatcher.PushFrame(frame);
            if (!opened || !player.NaturalDuration.HasTimeSpan) throw new Exception("소리 재생 준비 실패: " + Path.GetFileName(path) + " " + error);
            return player.NaturalDuration.TimeSpan.TotalSeconds;
        }
        finally { timer.Stop(); player.Close(); }
    }
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        string folder = Path.Combine(root, "sound-presets"); Directory.CreateDirectory(folder); string wav = Path.Combine(folder, "sample.wav");
        using (var writer = new BinaryWriter(File.Create(wav)))
        {
            int size = 1600; writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + size); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(size); writer.Write(new byte[size]);
        }
        SoundAssets.Validate(wav); string importedWav = SoundAssets.Import(wav, folder);
        Check(Path.GetExtension(importedWav) == ".wav" && File.ReadAllBytes(wav).SequenceEqual(File.ReadAllBytes(importedWav)), "WAV import preserves extension and audio bytes");
        Check(VerifyAudio(importedWav) > 0, "Imported WAV decodes through actual WPF audio player");
        Check(SoundAssets.Import(importedWav, folder) == importedWav && Directory.GetFiles(Path.Combine(folder, "sounds")).Length == 1, "Using existing sound preset does not create another copy");
        string mp3 = Path.Combine(folder, "sample.mp3"); File.WriteAllBytes(mp3, new byte[] { 73, 68, 51, 4, 0, 0, 0, 0, 0, 0 }); string importedMp3 = SoundAssets.Import(mp3, folder);
        Check(importedMp3.EndsWith(".mp3"), "MP3 registration remains supported");
        string fake = Path.Combine(folder, "fake.wav");
        File.WriteAllText(fake, "this is not wav data");
        bool rejected = false;
        try { SoundAssets.Import(fake, folder); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Mismatched WAV file rejected before copying");
        var prefs = new Preferences { SoundPath = importedMp3, SoundPresets = new() { new() { Name = "wave", Path = importedWav }, new() { Name = "mp3", Path = importedMp3 } } };
        string json = Path.Combine(folder, "settings.json"); OrderedStore.SaveJsonAsync(json, prefs.Snapshot()).GetAwaiter().GetResult();
        var reloaded = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(json))!; reloaded.Normalize();
        Check(reloaded.SoundPresets.Count == 2 && reloaded.SoundPresets[0].Path == importedWav, "Multiple sound presets survive reload");
        var next = reloaded.Snapshot(); next.SoundPath = next.SoundPresets[0].Path; OrderedStore.SaveJsonAsync(json, next).GetAwaiter().GetResult();
        Check(JsonSerializer.Deserialize<Preferences>(File.ReadAllText(json))!.SoundPath == importedWav && reloaded.SoundPath == importedMp3, "Preset switching persists active audio without changing old snapshot");
        File.SetLastWriteTimeUtc(importedWav, DateTime.UtcNow.AddDays(-2)); File.SetLastWriteTimeUtc(importedMp3, DateTime.UtcNow.AddDays(-2));
        Check(AssetCleanup.Scan(folder, Array.Empty<string>(), DateTime.UtcNow).Length == 0, "Cleanup protects inactive MP3 and WAV sound presets");
        next.SoundPresets.RemoveAll(p => p.Path == importedMp3); OrderedStore.SaveJsonAsync(json, next).GetAwaiter().GetResult();
        var unused = AssetCleanup.Scan(folder, Array.Empty<string>(), DateTime.UtcNow);
        Check(unused.Length == 1 && unused[0].Path == importedMp3, "Deleting inactive preset makes only that copied file eligible for cleanup");
        var old = JsonSerializer.Deserialize<Preferences>("{}")!; old.Normalize(); Check(old.SoundPresets.Count == 0, "Previous settings load with empty sound preset list");
        return count;
    }
}
