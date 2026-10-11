using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

public static class PetIcon
{
    static readonly Lazy<System.Drawing.Icon> tray = new(() =>
    {
        using var stream = typeof(PetIcon).Assembly.GetManifestResourceStream("KeyboardWorker.pet.ico") ?? new MemoryStream(IconBytes());
        using var source = new System.Drawing.Icon(stream); return (System.Drawing.Icon)source.Clone();
    });
    static readonly Lazy<BitmapSource> window = new(() => { var bitmap = MakeBitmap(256); bitmap.Freeze(); return bitmap; });
    public static System.Drawing.Icon TrayIcon => tray.Value;
    public static BitmapSource WindowIcon => window.Value;
    public static void Generate(string folder)
    {
        Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, "pet.ico"), IconBytes());
        File.WriteAllBytes(Path.Combine(folder, "pet-icon.png"), Png(256));
    }
    static byte[] IconBytes()
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 }; byte[][] frames = sizes.Select(Png).ToArray();
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length); uint offset = (uint)(6 + 16 * sizes.Length);
        for (int n = 0; n < sizes.Length; n++)
        {
            writer.Write((byte)(sizes[n] == 256 ? 0 : sizes[n])); writer.Write((byte)(sizes[n] == 256 ? 0 : sizes[n])); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write((uint)frames[n].Length); writer.Write(offset); offset += (uint)frames[n].Length;
        }
        foreach (var frame in frames) writer.Write(frame); return stream.ToArray();
    }
    static byte[] Png(int size)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(MakeBitmap(size))); using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    static RenderTargetBitmap MakeBitmap(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 64.0, size / 64.0));
            var outline = new Pen(Pastel.Original("#7E3A6C"), 2.3); var fur = Pastel.Original("#FFE1A8");
            dc.DrawRoundedRectangle(Pastel.Original("#B3EFF4"), new Pen(Pastel.Original("#78CBD5"), 1.5), new Rect(2, 2, 60, 60), 16, 16);
            dc.DrawEllipse(fur, outline, new Point(17, 18), 8, 10); dc.DrawEllipse(fur, outline, new Point(47, 18), 8, 10);
            dc.DrawEllipse(Pastel.Original("#FFB9DC"), null, new Point(17, 17), 4, 6); dc.DrawEllipse(Pastel.Original("#FFB9DC"), null, new Point(47, 17), 4, 6);
            dc.DrawRoundedRectangle(fur, outline, new Rect(10, 16, 44, 33), 15, 15);
            dc.DrawEllipse(Pastel.Original("#553D55"), null, new Point(24, 31), 2.4, 3); dc.DrawEllipse(Pastel.Original("#553D55"), null, new Point(40, 31), 2.4, 3);
            dc.DrawEllipse(Pastel.Original("#F5A0BB"), null, new Point(17, 38), 4, 2.6); dc.DrawEllipse(Pastel.Original("#F5A0BB"), null, new Point(47, 38), 4, 2.6);
            dc.DrawEllipse(Pastel.Original("#7E3A6C"), null, new Point(32, 37), 2.1, 1.5);
            dc.DrawLine(new Pen(Pastel.Original("#7E3A6C"), 1.6), new Point(29, 42), new Point(35, 42));
            dc.DrawRoundedRectangle(Pastel.Original("#FFB9DC"), outline, new Rect(8, 47, 48, 12), 4, 4);
            if (size >= 24) for (int col = 0; col < 8; col++) dc.DrawRoundedRectangle(Pastel.Original("#FFF1F8"), null, new Rect(12 + col * 5.2, 51, 3.5, 3.5), 0.7, 0.7);
            dc.DrawEllipse(fur, outline, new Point(20, 47), 6, 3.5); dc.DrawEllipse(fur, outline, new Point(44, 47), 6, 3.5);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); return bitmap;
    }
}
