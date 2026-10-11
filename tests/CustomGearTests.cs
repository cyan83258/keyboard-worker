using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

public static class CustomGearTests
{
    public static int Run(string root, string png)
    {
        int count = 0; void Check(bool value, string text) { if (!value) throw new Exception(text); count++; }
        var game = new PetGame(Path.Combine(root, "custom-gear")); var item = new CustomGear { Name = "내 모자", ImagePath = png };
        game.RegisterCustom(item, true); Check(game.State.CustomItems.Count == 1 && game.State.Owned.Contains(item.Id), "Custom gear is owned on registration");
        Check(game.State.Coins == 20 && game.State.TotalXp == 0 && !game.State.Badges.Contains("first-shop"), "Registration costs no coins and grants no shop reward");
        Check(game.Catalog.Any(i => i.Id == item.Id && i.Name == item.Name), "Custom gear included in dynamic catalog");
        Check(game.State.Equipped["head"] == item.Id, "Custom gear equipped immediately");
        game.RegisterCustom(item with { Name = "내 의상", Slot = "body", WidthPercent = 400, OffsetX = 55, Behind = true }, true);
        Check(!game.State.Equipped.ContainsKey("head") && game.State.Equipped["body"] == item.Id && game.State.CustomItems.Count == 1, "Editing slot preserves identity and removes old slot");
        Check(game.State.CustomItems[0].WidthPercent == 200, "Custom image size normalized");
        Check(game.ChatContext().Contains("내 의상"), "Custom outfit names available to optional chat context");
        var snapshot = game.State.Snapshot(); snapshot.CustomItems.Clear(); Check(game.State.CustomItems.Count == 1, "Custom catalog snapshot isolated");
        game.Save(true); OrderedStore.FlushAsync().GetAwaiter().GetResult(); var reloaded = new PetGame(Path.Combine(root, "custom-gear"));
        Check(reloaded.State.CustomItems.Count == 1 && reloaded.State.Owned.Contains(item.Id) && reloaded.State.Equipped["body"] == item.Id, "Custom catalog and equipped gear survive reload");
        var bitmap = ImageAssets.Read(png, 32); var outfit = reloaded.State.Snapshot();
        bool HasPixels(bool behind)
        {
            var layer = new AccessoryLayer { Outfit = outfit, BackLayer = behind, CustomImage = _ => bitmap };
            layer.Measure(new Size(240, 180));
            layer.Arrange(new Rect(0, 0, 240, 180));
            layer.UpdateLayout();
            var target = new RenderTargetBitmap(240, 180, 96, 96, PixelFormats.Pbgra32);
            target.Render(layer);
            byte[] pixels = new byte[240 * 180 * 4];
            target.CopyPixels(pixels, 240 * 4, 0);
            return Enumerable.Range(0, 240 * 180).Any(n => pixels[n * 4 + 3] > 0);
        }
        Check(HasPixels(true) && !HasPixels(false), "Custom bitmap renders only in selected front/back layer");
        reloaded.RegisterCustom(reloaded.State.CustomItems[0] with { Slot = "face" }, false);
        Check(reloaded.State.Equipped.Count == 0 && reloaded.State.Owned.Contains(item.Id), "Save without equip removes previous worn slot");
        reloaded.DeleteCustom(item.Id); Check(reloaded.State.CustomItems.Count == 0 && !reloaded.State.Owned.Contains(item.Id) && File.Exists(png), "Delete registration preserves original image");
        reloaded.Save(true); OrderedStore.FlushAsync().GetAwaiter().GetResult(); Check(new PetGame(Path.Combine(root, "custom-gear")).State.CustomItems.Count == 0, "Custom deletion persists");
        bool badSlot = false; try { game.RegisterCustom(item with { Slot = "invalid" }, true); } catch (InvalidOperationException) { badSlot = true; } Check(badSlot, "Invalid gear slot rejected");
        var settings = new Preferences { PatLines = "대사 하나\n대사 둘" }; var preset = CharacterPreset.Capture("대사 프리셋", settings, new Persona()); settings.PatLines = "변경";
        Check(preset.Prepare(settings, new ChatPreferences()).Settings.PatLines == "대사 하나\n대사 둘", "Pat dialogue included in preset snapshot");
        preset.PatLines = null;
        Check(preset.Prepare(settings, new ChatPreferences()).Settings.PatLines == new Preferences().PatLines, "Old preset uses default pat dialogue without copying another character");
        settings.PatLines = ""; settings.Normalize(); Check(settings.PatLines == "", "Empty pat dialogue remains muted");
        return count;
    }
}
