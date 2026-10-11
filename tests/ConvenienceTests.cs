using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardWorker;

public static class ConvenienceTests
{
    public static int Run(string root)
    {
        int count = 0; void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        var correction = new ImageAdjustment { Scale = 80, X = 10, Y = -5 };
        var transform = PetImageLayout.Adjustment(new Size(200, 200), correction);
        Check(Math.Abs(transform.Value.M11 - .8) < .001 && transform.Value.OffsetX == 20 && transform.Value.OffsetY == -10, "Per-pose size and position correction uses reference canvas percentages");
        var bad = new ImageAdjustment { Scale = double.NaN, X = double.PositiveInfinity, Y = -200 }.Normalize();
        Check(bad.Scale == 100 && bad.X == 0 && bad.Y == -100, "Invalid and extreme corrections normalized");
        var prefs = new Preferences { ImageAdjustments = new() { ["typing-left"] = correction } };
        var snapshot = prefs.Snapshot(); snapshot.ImageAdjustments["typing-left"] = correction with { Scale = 120 };
        Check(prefs.ImageAdjustments["typing-left"].Scale == 80, "Correction snapshot is isolated from edit draft");
        var preset = CharacterPreset.Capture("correction", prefs, new Persona());
        Check(preset.Prepare(new Preferences(), new ChatPreferences()).Settings.ImageAdjustments["typing-left"] == correction, "Character presets restore per-pose corrections");
        var appearanceStore = new PresetStore(Path.Combine(root, "appearance-presets")); var active = appearanceStore.Add("active", prefs, new Persona()); prefs.ActivePresetId = active.Id;
        prefs.ImageAdjustments["typing-left"] = correction with { Scale = 130 }; prefs.ImagePaths["idle"] = "new.png";
        appearanceStore.SaveAppearanceAsync(prefs).GetAwaiter().GetResult();
        var restoredAppearance = new PresetStore(Path.Combine(root, "appearance-presets")).Items.Single();
        Check(restoredAppearance.ImageAdjustments["typing-left"].Scale == 130 && restoredAppearance.Images["idle"] == "new.png", "Saving image correction also updates active preset on disk");
        var element = new Image(); PetImageLayout.ApplyCorrection(element, new Size(200, 200), correction); var firstTransform = element.RenderTransform;
        PetImageLayout.ApplyCorrection(element, new Size(200, 200), correction);
        Check(ReferenceEquals(firstTransform, element.RenderTransform) && firstTransform.IsFrozen, "Unchanged pose correction reuses frozen transform");
        var legacy = JsonSerializer.Deserialize<CharacterPreset>("{\"Id\":\"old\",\"Images\":{}}")!;
        Check(legacy.Prepare(prefs, new ChatPreferences()).Settings.ImageAdjustments.Count == 0, "Legacy preset resets previous character's corrections");
        var life = new BubbleLifetime(); life.Start(0, 5); life.Hover(true, 2000);
        Check(!life.Expired(20000, true), "Hover pauses expired bubble timer");
        life.Hover(false, 12000);
        Check(life.Deadline == 15000 && !life.Expired(14999, true) && life.Expired(15000, true), "Mouse leave resumes exact remaining duration");
        life.Hover(true, 16000); life.Hover(true, 17000); life.Start(18000, 4); life.Hover(false, 20000);
        Check(life.Deadline == 24000 && !life.Expired(23999, true), "New reply while hovered receives full duration after leaving");
        string folder = Path.Combine(root, "asset-cleanup"); Directory.CreateDirectory(folder);
        DateTime now = DateTime.UtcNow;
        string Asset(string category, string extension, bool recent = false)
        {
            string dir = Path.Combine(folder, category); Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + extension); File.WriteAllText(path, "test fixture");
            File.SetLastWriteTimeUtc(path, recent ? now : now.AddDays(-2)); return path;
        }
        string current = Asset("images", ".png"), presetFile = Asset("images", ".jpg"), gearFile = Asset("images", ".png"), sound = Asset("sounds", ".mp3");
        string unused = Asset("images", ".png"), unusedSound = Asset("sounds", ".mp3"), recentFile = Asset("images", ".png", true);
        string manual = Path.Combine(folder, "images", "manual.png"); File.WriteAllText(manual, "manual"); File.SetLastWriteTimeUtc(manual, now.AddDays(-2));
        string nested = Asset("images/nested", ".png");
        File.WriteAllText(Path.Combine(folder, "settings.json"), JsonSerializer.Serialize(new Preferences { ImagePaths = new() { ["idle"] = current }, SoundPath = sound }));
        File.WriteAllText(Path.Combine(folder, "character-presets.json"), JsonSerializer.Serialize(new[] { new CharacterPreset { Images = new() { ["idle"] = presetFile }, Advanced = false } }));
        File.WriteAllText(Path.Combine(folder, "pet-game.json"), JsonSerializer.Serialize(new GameState { CustomItems = new() { new CustomGear { ImagePath = gearFile } } }));
        var candidates = AssetCleanup.Scan(folder, Array.Empty<string>(), now);
        Check(candidates.Length == 2 && candidates.Any(c => c.Path == unused) && candidates.Any(c => c.Path == unusedSound), "Cleanup protects disk settings, presets, custom gear, recent and non-managed files");
        var protectedAgain = AssetCleanup.Scan(folder, new[] { unused }, now);
        Check(protectedAgain.Length == 1 && protectedAgain[0].Path == unusedSound, "Live unsaved references are also protected");
        var result = AssetCleanup.Clean(folder, candidates, new[] { unused }, now, File.Delete);
        Check(result.Removed == 1 && result.Skipped == 1 && File.Exists(unused) && !File.Exists(unusedSound), "Cleanup rechecks newly referenced files before deleting reviewed candidates");
        string changed = Asset("images", ".png");
        var beforeChange = AssetCleanup.Scan(folder, new[] { unused }, now);
        File.AppendAllText(changed, "changed");
        File.SetLastWriteTimeUtc(changed, now.AddDays(-2));
        var changeResult = AssetCleanup.Clean(folder, beforeChange, new[] { unused }, now, File.Delete);
        Check(changeResult.Removed == 0 && changeResult.Skipped == 1 && File.Exists(changed), "Changed file is excluded from reviewed cleanup");
        string external = Path.Combine(root, Guid.NewGuid().ToString("N") + ".png"); File.WriteAllText(external, "outside"); var info = new FileInfo(external);
        var outResult = AssetCleanup.Clean(folder, new[] { new UnusedAsset(external, info.Length, info.LastWriteTimeUtc) }, Array.Empty<string>(), now, File.Delete);
        Check(outResult.Removed == 0 && File.Exists(external) && File.Exists(nested) && File.Exists(manual), "Cleanup never deletes outside managed directories or recursively");
        File.WriteAllText(Path.Combine(folder, "character-presets.json"), "broken-json"); bool blocked = false;
        try { AssetCleanup.Clean(folder, candidates, Array.Empty<string>(), now, File.Delete); } catch (JsonException) { blocked = true; }
        Check(blocked && File.Exists(unused) && File.Exists(presetFile), "Unreadable reference data blocks cleanup without deleting files");
        return count;
    }
}
