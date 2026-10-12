using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

public sealed class AccessoryLayer : FrameworkElement
{
    public GameState Outfit { get; set; } = new();
    public BitmapSource? AvatarImage { get; set; }
    public bool BackLayer { get; set; }
    public Func<string, BitmapSource?>? CustomImage { get; set; }
    public AccessoryLayer() { IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (!Outfit.ShowAccessories || ActualWidth <= 0 || ActualHeight <= 0) return;
        double width = ActualWidth, height = ActualHeight, x = 0, y = 0;
        if (AvatarImage != null)
        {
            double scale = Math.Min(width / AvatarImage.PixelWidth, height / AvatarImage.PixelHeight);
            width = AvatarImage.PixelWidth * scale;
            height = AvatarImage.PixelHeight * scale;
            x = (ActualWidth - width) / 2;
            y = (ActualHeight - height) / 2;
        }
        dc.PushTransform(new TranslateTransform(x, y));
        foreach (var pair in Outfit.Equipped)
        {
            CustomGear? custom = null; foreach (var gear in Outfit.Gear) if (gear.Id == pair.Value) { custom = gear; break; }
            if (custom != null)
            {
                if (custom.Behind != BackLayer) continue; var bitmap = CustomImage?.Invoke(custom.Id); if (bitmap == null) continue;
                double cx = pair.Key == "head" ? Outfit.HeadX : pair.Key == "face" ? Outfit.FaceX : pair.Key == "neck" ? Outfit.NeckX : 50;
                double cy = pair.Key == "head" ? Outfit.HeadY : pair.Key == "face" ? Outfit.FaceY : pair.Key == "neck" ? Outfit.NeckY : 50;
                double w = width * custom.WidthPercent / 100 * Outfit.AccessoryScale, h = w * bitmap.PixelHeight / bitmap.PixelWidth;
                dc.PushTransform(new TranslateTransform(width * (cx + custom.OffsetX) / 100, height * (cy + custom.OffsetY) / 100)); dc.PushTransform(new RotateTransform(custom.Rotation));
                dc.DrawImage(bitmap, new Rect(-w / 2, -h / 2, w, h)); dc.Pop(); dc.Pop(); continue;
            }
            if (BackLayer) continue;
            double ax = pair.Key == "head" ? Outfit.HeadX : pair.Key == "face" ? Outfit.FaceX : Outfit.NeckX;
            double ay = pair.Key == "head" ? Outfit.HeadY : pair.Key == "face" ? Outfit.FaceY : Outfit.NeckY;
            if (pair.Key == "effect")
            {
                foreach (var p in new[]
                {
                    new Point(.12 * width, .22 * height),
                    new Point(.88 * width, .15 * height),
                    new Point(.14 * width, .72 * height),
                    new Point(.86 * width, .69 * height)
                }) AccessoryArt.Draw(dc, pair.Value, p, width / 240 * Outfit.AccessoryScale);
            }
            else AccessoryArt.Draw(dc, pair.Value, new Point(width * ax / 100, height * ay / 100), width / 240 * Outfit.AccessoryScale);
        }
        dc.Pop();
    }
}
public sealed class ItemPreview : FrameworkElement
{
    public string ItemId { get; set; } = "pink-bow";
    protected override void OnRender(DrawingContext dc) { base.OnRender(dc); AccessoryArt.Draw(dc, ItemId, new Point(ActualWidth / 2, ActualHeight / 2 + (ItemId == "crown" ? 10 : 0)), .65); }
}
public static class AccessoryArt
{
    static readonly Brush pink = Pastel.Original("#FFABD5"), gold = Pastel.Original("#FFD66B"), blue = Pastel.Original("#9FEAF6"), ink = Pastel.Original("#7E3A6C");
    static readonly Pen line = new(ink, 2.2);
    static readonly Geometry leftBow = Geometry.Parse("M 0,0 L -23,-13 Q -32,0 -23,13 Z");
    static readonly Geometry rightBow = Geometry.Parse("M 0,0 L 23,-13 Q 32,0 23,13 Z");
    static readonly Geometry crown = Geometry.Parse("M -29,0 L -34,-20 L -14,-9 L 0,-25 L 14,-9 L 34,-20 L 29,0 Z");
    static readonly Geometry heart = Geometry.Parse("M 0,12 C -30,-6 -14,-25 0,-12 C 14,-25 30,-6 0,12 Z");
    static readonly Geometry star = Geometry.Parse("M 0,-17 L 5,-5 L 18,-5 L 8,3 L 12,16 L 0,8 L -12,16 L -8,3 L -18,-5 L -5,-5 Z");
    static readonly Geometry headphoneArc = Geometry.Parse("M -47,30 C -63,-30 63,-30 47,30");
    static AccessoryArt() { line.Freeze(); foreach (var g in new[] { leftBow, rightBow, crown, heart, star, headphoneArc }) g.Freeze(); }
    public static void Draw(DrawingContext dc, string id, Point position, double scale)
    {
        dc.PushTransform(new TranslateTransform(position.X, position.Y)); dc.PushTransform(new ScaleTransform(scale, scale));
        switch (id)
        {
            case "pink-bow": dc.DrawGeometry(pink, line, leftBow); dc.DrawGeometry(pink, line, rightBow); dc.DrawEllipse(Pastel.Original("#FFF1F8"), line, new Point(0, 0), 5, 6); break;
            case "star-pin": dc.DrawGeometry(gold, line, star); dc.DrawEllipse(Pastel.Original("#FFF1F8"), null, new Point(-3, -5), 2, 2); break;
            case "crown": dc.DrawGeometry(gold, line, crown); dc.DrawEllipse(pink, line, new Point(0, -10), 5, 5); break;
            case "headphones": dc.DrawGeometry(null, new Pen(blue, 9), headphoneArc);
            dc.DrawGeometry(null, line, headphoneArc);
            dc.DrawRoundedRectangle(blue, line, new Rect(-57, 22, 14, 28), 6, 6);
            dc.DrawRoundedRectangle(blue, line, new Rect(43, 22, 14, 28), 6, 6);
            break;
            case "glasses": dc.DrawEllipse(null, line, new Point(-21, 0), 15, 12);
            dc.DrawEllipse(null, line, new Point(21, 0), 15, 12);
            dc.DrawLine(line, new Point(-6, 0), new Point(6, 0));
            dc.DrawLine(line, new Point(-36, -1), new Point(-48, -5));
            dc.DrawLine(line, new Point(36, -1), new Point(48, -5));
            break;
            case "scarf": dc.DrawRoundedRectangle(pink, line, new Rect(-43, -6, 86, 13), 6, 6); dc.DrawRoundedRectangle(pink, line, new Rect(15, 3, 15, 32), 3, 3); break;
            case "hearts": dc.DrawGeometry(pink, line, heart); break;
            case "sparkles": dc.DrawGeometry(gold, line, star); break;
        }
        dc.Pop(); dc.Pop();
    }
}
