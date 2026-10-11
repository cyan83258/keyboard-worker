using System;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public static class FlipTests
{
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        var picture = new Image(); var viewport = new Size(200, 100); var correction = new ImageAdjustment { Scale = 150, X = 10, Y = 20 };
        PetImageLayout.ApplyCorrection(picture, viewport, correction); var plain = picture.RenderTransform.Value;
        Check(plain.M11 == 1.5 && plain.OffsetX == 20 && plain.OffsetY == 20, "Unflipped correction is unchanged");
        PetImageLayout.ApplyCorrection(picture, viewport, correction, true); var mirrored = picture.RenderTransform.Value;
        Check(mirrored.M11 == -1.5 && mirrored.M22 == 1.5 && mirrored.OffsetX == -20 && mirrored.OffsetY == 20, "Flip mirrors the picture and its sideways correction only");
        Check(picture.RenderTransformOrigin == new Point(.5, .5), "Flip turns around the centre");
        var settings = new Preferences { FlipImages = true }; var store = new PresetStore(System.IO.Path.Combine(root, "flip-presets"));
        var preset = store.Add("Mirrored", settings, new Persona { Name = "Mirror" }); Check(preset.Flip, "Preset captures the flip option");
        Check(preset.Prepare(new Preferences(), new ChatPreferences()).Settings.FlipImages, "Applying a preset restores its flip option");
        store.SaveFlipAsync(preset.Id, false).GetAwaiter().GetResult(); Check(!store.Items[0].Flip, "Flip can be toggled per preset");
        var changed = new Preferences { ActivePresetId = preset.Id, FlipImages = true }; store.SaveAppearanceAsync(changed).GetAwaiter().GetResult();
        Check(new PresetStore(System.IO.Path.Combine(root, "flip-presets")).Items[0].Flip, "Flip is stored with the preset appearance");
        return count;
    }
}
