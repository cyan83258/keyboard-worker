using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Runtime.CompilerServices;

namespace KeyboardWorker;

public static class PetImageLayout
{
    sealed class BoundsBox { public Rect Bounds; }
    static readonly ConditionalWeakTable<BitmapSource, BoundsBox> opaque = new();
    public static Rect ImageBounds(Image image)
    {
        if (image.Source is not BitmapSource bitmap || image.ActualWidth <= 0 || image.ActualHeight <= 0) return new Rect(0, 0, Math.Max(1, image.ActualWidth), Math.Max(1, image.ActualHeight));
        var pixels = opaque.GetValue(bitmap, source =>
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); int w = source.PixelWidth, h = source.PixelHeight;
            byte[] data = new byte[w * h * 4]; converted.CopyPixels(data, w * 4, 0); int left = w, top = h, right = -1, bottom = -1;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (data[(y * w + x) * 4 + 3] > 20) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            return new BoundsBox { Bounds = right < 0 ? new Rect(0, 0, w, h) : new Rect(left, top, right - left + 1, bottom - top + 1) };
        }).Bounds;
        double scale = Math.Min(image.ActualWidth / bitmap.PixelWidth, image.ActualHeight / bitmap.PixelHeight);
        return new Rect((image.ActualWidth - bitmap.PixelWidth * scale) / 2 + pixels.X * scale, (image.ActualHeight - bitmap.PixelHeight * scale) / 2 + pixels.Y * scale, pixels.Width * scale, pixels.Height * scale);
    }
    public static Transform Adjustment(Size viewport, ImageAdjustment? value)
    {
        var a = (value ?? new()).Normalize();
        var transform = new MatrixTransform(a.Scale / 100, 0, 0, a.Scale / 100, viewport.Width * a.X / 100, viewport.Height * a.Y / 100); transform.Freeze(); return transform;
    }
    // flip mirrors the corrected picture around the centre of the viewport, so a sideways correction mirrors with it.
    public static void ApplyCorrection(FrameworkElement target, Size viewport, ImageAdjustment? value, bool flip = false)
    {
        var a = (value ?? new()).Normalize(); int side = flip ? -1 : 1;
        var matrix = new Matrix(side * a.Scale / 100, 0, 0, a.Scale / 100, side * viewport.Width * a.X / 100, viewport.Height * a.Y / 100);
        target.RenderTransformOrigin = new Point(.5, .5);
        if (target.RenderTransform.Value == matrix) return;
        var transform = new MatrixTransform(matrix); transform.Freeze(); target.RenderTransform = transform;
    }
    public static Size Viewport(double width, double height, BitmapSource? idle)
    {
        double ratio = idle == null ? 1 : (double)idle.PixelWidth / idle.PixelHeight;
        double w = Math.Min(width, height * ratio);
        return new Size(w, w / ratio);
    }
    public static void Apply(Image image, double width, double height, BitmapSource? idle, ImageAdjustment? adjustment = null, bool flip = false)
    {
        var viewport = Viewport(width, height, idle);
        image.Width = viewport.Width; image.Height = viewport.Height;
        image.HorizontalAlignment = HorizontalAlignment.Center; image.VerticalAlignment = VerticalAlignment.Center;
        ApplyCorrection(image, viewport, adjustment, flip);
    }
}

public sealed record ImageAdjustment
{
    public double Scale { get; init; } = 100;
    public double X { get; init; }
    public double Y { get; init; }
    public ImageAdjustment Normalize() => this with { Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, 25, 200) : 100, X = double.IsFinite(X) ? Math.Clamp(X, -100, 100) : 0, Y = double.IsFinite(Y) ? Math.Clamp(Y, -100, 100) : 0 };
}
