using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

public static class DietTests
{
    static void Picture(string path, int width, int height, byte tone)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) { int i = (y * width + x) * 4; pixels[i] = (byte)(x * 255 / width); pixels[i + 1] = (byte)(y * 255 / height); pixels[i + 2] = tone; pixels[i + 3] = 255; }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        string home = Path.Combine(root, "diet"), images = Path.Combine(home, "images"); Directory.CreateDirectory(images);
        string big = Path.Combine(root, "diet-big.png"), small = Path.Combine(root, "diet-small.png"), other = Path.Combine(root, "diet-other.png");
        Picture(big, 2000, 1000, 40); Picture(small, 64, 64, 80); Picture(other, 64, 64, 120);
        string first = ImageAssets.Import(big, home), again = ImageAssets.Import(big, home);
        Check(first == again && Directory.GetFiles(images).Length == 1, "Importing the same picture twice stores it once");
        Check(ImageAssets.Validate(first) == (1024, 512) && new FileInfo(first).Length < new FileInfo(big).Length, "An oversized picture is stored at the size it is drawn");
        string plain = ImageAssets.Import(small, home); Check(File.ReadAllBytes(plain).AsSpan().SequenceEqual(File.ReadAllBytes(small)), "A small picture is stored untouched");
        string one = Path.Combine(images, Guid.NewGuid().ToString("N") + ".png"), two = Path.Combine(images, Guid.NewGuid().ToString("N") + ".png"), large = Path.Combine(images, Guid.NewGuid().ToString("N") + ".png");
        File.Copy(other, one); File.Copy(other, two); File.Copy(big, large);
        var plan = ImageDiet.Scan(home, new[] { one, two, large, plain, "", other });
        Check(plan.Merge.Count == 1 && plan.DuplicateBytes == new FileInfo(other).Length, "Scan finds identical pictures that are in use");
        Check(plan.Oversized.Length == 1 && plan.Oversized[0] == large, "Scan finds pictures larger than they are drawn and ignores files outside the folder");
        string kept = plan.Merge.Values.Single(), dropped = plan.Merge.Keys.Single();
        Check(ImageDiet.Map(plan.Merge, dropped) == kept && ImageDiet.Map(plan.Merge, plain) == plain && ImageDiet.Map(plan.Merge, "") == "", "References move to the kept copy only");
        var store = new PresetStore(Path.Combine(root, "diet-presets")); store.Add("Diet", new Preferences { ImagePaths = new() { ["idle"] = dropped } }, new Persona { Name = "Diet" });
        store.RemapImagesAsync(plan.Merge).GetAwaiter().GetResult(); Check(store.Items[0].Images["idle"] == kept, "Presets follow merged pictures");
        var game = new PetGame(Path.Combine(root, "diet-game")); game.RegisterCustom(new CustomGear { Name = "Hat", ImagePath = dropped }, false);
        game.RemapImages(plan.Merge); Check(game.State.CustomItems.Single().ImagePath == kept, "Custom gear follows merged pictures");
        var recycled = new List<string>();
        var blocked = ImageDiet.Apply(plan with { Oversized = Array.Empty<string>() }, new[] { dropped }, recycled.Add);
        Check(blocked.Merged == 0 && recycled.Count == 0 && File.Exists(dropped), "A duplicate that is still referenced is left alone");
        long before = new FileInfo(large).Length;
        var result = ImageDiet.Apply(plan, new[] { kept, large }, path => { recycled.Add(path); File.Delete(path); });
        Check(result.Merged == 1 && !File.Exists(dropped) && File.Exists(kept), "Merged duplicates leave through the recycle step");
        Check(result.Resized == 1 && result.Failed == 0 && ImageAssets.Validate(large) == (1024, 512) && recycled.Contains(large), "An oversized picture is replaced in place and its original recycled");
        Check(result.SavedBytes == new FileInfo(other).Length + before - new FileInfo(large).Length && !File.Exists(large + ".tmp"), "Saved space is reported and no temporary file remains");
        Check(new MusicOptions().Compact == false && new MusicOptions { Compact = true }.Normalize().Compact, "Folded player state is kept");
        return count;
    }
}
