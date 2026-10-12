using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

// Pictures for the character pages: copied into the images folder at a size that suits a window, and decoded only as large as shown.
public static class StageAssets
{
    public const int SpriteSide = 1600, BackgroundSide = 1920;
    static string Folder(string root) { string folder = Path.Combine(root, "images"); Directory.CreateDirectory(folder); return folder; }
    // A standing picture is kept as PNG so its transparency survives. clear: make a plain backdrop transparent first.
    public static string ImportSprite(string source, string root, bool clear)
    {
        ImageAssets.Validate(source); BitmapSource picture = new FormatConvertedBitmap(ImageAssets.Read(source, SpriteSide), PixelFormats.Bgra32, null, 0);
        if (clear) picture = ClearBackdrop(picture);
        string target = Path.Combine(Folder(root), Guid.NewGuid().ToString("N") + ".png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(picture)); using (var file = File.Create(target)) encoder.Save(file);
        return target;
    }
    // An emoticon for text messages is shown small, so it is stored small: a PNG (its transparency kept) of at most StickerSide.
    public const int StickerSide = 320;
    public static string ImportSticker(string source, string root)
    {
        ImageAssets.Validate(source); BitmapSource picture = new FormatConvertedBitmap(ImageAssets.Read(source, StickerSide), PixelFormats.Bgra32, null, 0);
        string target = Path.Combine(Folder(root), Guid.NewGuid().ToString("N") + ".png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(picture)); using (var file = File.Create(target)) encoder.Save(file);
        return target;
    }
    // A background has no transparency worth keeping, so it is stored as JPEG: a fraction of the size.
    public static string ImportBackground(string source, string root)
    {
        ImageAssets.Validate(source); var picture = new FormatConvertedBitmap(ImageAssets.Read(source, BackgroundSide), PixelFormats.Bgr24, null, 0);
        string target = Path.Combine(Folder(root), Guid.NewGuid().ToString("N") + ".jpg");
        var encoder = new JpegBitmapEncoder { QualityLevel = 88 }; encoder.Frames.Add(BitmapFrame.Create(picture)); using (var file = File.Create(target)) encoder.Save(file);
        return target;
    }
    // Makes the backdrop of a picture transparent: everything connected to the edges that has (nearly) the colour of the
    // top-left corner. A picture whose corners are already transparent is returned as it is. Areas of the same colour
    // inside the character are kept, because they do not touch the edge.
    public static BitmapSource ClearBackdrop(BitmapSource source, int tolerance = 26)
    {
        var picture = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = picture.PixelWidth, height = picture.PixelHeight, stride = width * 4; if (width < 2 || height < 2) return picture;
        var pixels = new byte[stride * height]; picture.CopyPixels(pixels, stride, 0);
        if (pixels[3] == 0 || pixels[(width - 1) * 4 + 3] == 0 || pixels[(height - 1) * stride + 3] == 0 || pixels[pixels.Length - 1] == 0) return picture;
        byte blue = pixels[0], green = pixels[1], red = pixels[2];
        bool Backdrop(int at) => pixels[at + 3] != 0 && Math.Abs(pixels[at] - blue) <= tolerance && Math.Abs(pixels[at + 1] - green) <= tolerance && Math.Abs(pixels[at + 2] - red) <= tolerance;
        var pending = new Stack<int>();
        void Visit(int x, int y) { int at = y * stride + x * 4; if (Backdrop(at)) { pixels[at + 3] = 0; pending.Push(y * width + x); } }
        for (int x = 0; x < width; x++) { Visit(x, 0); Visit(x, height - 1); }
        for (int y = 0; y < height; y++) { Visit(0, y); Visit(width - 1, y); }
        while (pending.Count > 0)
        {
            int index = pending.Pop(), x = index % width, y = index / width;
            if (x > 0) Visit(x - 1, y); if (x < width - 1) Visit(x + 1, y); if (y > 0) Visit(x, y - 1); if (y < height - 1) Visit(x, y + 1);
        }
        // Cleared pixels keep no colour, so no fringe of the old backdrop shows when the picture is scaled.
        for (int at = 0; at < pixels.Length; at += 4) if (pixels[at + 3] == 0) pixels[at] = pixels[at + 1] = pixels[at + 2] = 0;
        var result = BitmapSource.Create(width, height, picture.DpiX, picture.DpiY, PixelFormats.Bgra32, null, pixels, stride); result.Freeze(); return result;
    }
}

// Decoded pictures of one open page, by file and size. Each is decoded once, at the size it is drawn, and let go with the page.
public sealed class StagePictures
{
    readonly Dictionary<string, BitmapSource?> cache = new(StringComparer.OrdinalIgnoreCase);
    public BitmapSource? Get(string path, int side)
    {
        if (string.IsNullOrWhiteSpace(path)) return null; side = Math.Clamp((side + 199) / 200 * 200, 200, StageAssets.BackgroundSide);   // sizes in steps, so resizing the window does not decode again and again
        string key = side + "|" + path; if (cache.TryGetValue(key, out var known)) return known;
        BitmapSource? picture = null;
        try { if (File.Exists(path)) picture = ImageAssets.Read(path, side); }
        catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException or FileFormatException) { }
        if (cache.Count > 60) cache.Clear();
        return cache[key] = picture;
    }
    public void Clear() => cache.Clear();
}
