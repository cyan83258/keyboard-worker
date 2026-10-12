using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

// Draws what the character wears over (or, with BackLayer, behind) its picture. Every accessory is a picture: one that
// came with the program (ShopGear) or the user's own (CustomGear). Where each sits is worked out in one place (Spot), so
// the placement window can ask which accessory is under the mouse and move the very thing that is drawn.
public sealed class AccessoryLayer : FrameworkElement
{
    public GameState Outfit { get; set; } = new();
    public BitmapSource? AvatarImage { get; set; }
    public bool BackLayer { get; set; }
    public Func<string, BitmapSource?>? CustomImage { get; set; }
    // The accessory outlined, while it is being placed ("" = none).
    public string Marked { get; set; } = "";
    static readonly Pen outline = Dashed();
    static Pen Dashed() { var pen = new Pen(Brushes.DeepPink, 1.6) { DashStyle = new DashStyle(new[] { 3.0, 2.5 }, 0) }; pen.Freeze(); return pen; }
    public AccessoryLayer() { IsHitTestVisible = false; }
    // The part of this element the character's picture fills.
    public Rect Body()
    {
        double width = ActualWidth, height = ActualHeight; if (AvatarImage == null) return new Rect(0, 0, width, height);
        double scale = Math.Min(width / AvatarImage.PixelWidth, height / AvatarImage.PixelHeight); double w = AvatarImage.PixelWidth * scale, h = AvatarImage.PixelHeight * scale;
        return new Rect((width - w) / 2, (height - h) / 2, w, h);
    }
    // Where an accessory is drawn: its centre and size here. Null while its picture is not ready.
    public (Point Centre, Size Size)? Spot(string slot, CustomGear gear)
    {
        var bitmap = CustomImage?.Invoke(gear.Id); if (bitmap == null) return null; var body = Body();
        double cx = slot == "head" ? Outfit.HeadX : slot == "face" ? Outfit.FaceX : slot == "neck" ? Outfit.NeckX : 50, cy = slot == "head" ? Outfit.HeadY : slot == "face" ? Outfit.FaceY : slot == "neck" ? Outfit.NeckY : 50;
        double w = body.Width * gear.WidthPercent / 100 * Outfit.AccessoryScale;
        return (new Point(body.X + body.Width * (cx + gear.OffsetX) / 100, body.Y + body.Height * (cy + gear.OffsetY) / 100), new Size(w, w * bitmap.PixelHeight / bitmap.PixelWidth));
    }
    // The accessory of this layer under a point (the one drawn last, so on top, first); null when there is none.
    public string? Hit(Point at)
    {
        foreach (var pair in Outfit.Equipped.Reverse())
        {
            var gear = Outfit.Wearing(pair.Value); if (gear == null || gear.Behind != BackLayer || Spot(pair.Key, gear) is not { } spot) continue;
            if (new Rect(spot.Centre.X - spot.Size.Width / 2, spot.Centre.Y - spot.Size.Height / 2, spot.Size.Width, spot.Size.Height).Contains(at)) return gear.Id;
        }
        return null;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (!Outfit.ShowAccessories || ActualWidth <= 0 || ActualHeight <= 0) return;
        foreach (var pair in Outfit.Equipped)
        {
            var gear = Outfit.Wearing(pair.Value); if (gear == null || gear.Behind != BackLayer || Spot(pair.Key, gear) is not { } spot) continue;
            var area = new Rect(-spot.Size.Width / 2, -spot.Size.Height / 2, spot.Size.Width, spot.Size.Height);
            dc.PushTransform(new TranslateTransform(spot.Centre.X, spot.Centre.Y)); dc.PushTransform(new RotateTransform(gear.Rotation));
            dc.DrawImage(CustomImage!(gear.Id), area); if (Marked == gear.Id) dc.DrawRectangle(null, outline, area);
            dc.Pop(); dc.Pop();
        }
    }
}
