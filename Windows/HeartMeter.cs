using System;
using System.Windows;
using System.Windows.Media;

namespace KeyboardWorker;

// Closeness as hearts: five outlines that fill with colour from the left as closeness grows, one heart per fifth of the way.
public sealed class HeartMeter : FrameworkElement
{
    public const int Hearts = 5;
    static readonly Geometry heart = Geometry.Parse("M 0,6 C -14,-4 -7,-14 0,-6 C 7,-14 14,-4 0,6 Z");
    static HeartMeter() { heart.Freeze(); }
    double value; readonly double size;
    public HeartMeter(double size = 20) { this.size = size; Width = Hearts * (size + 4); Height = size + 2; IsHitTestVisible = true; }
    // Hearts filled, 0 to 5, for a closeness of 0 to 1000.
    public static double Filled(int bond) => Math.Clamp(bond, 0, 1000) / 200.0;
    public int Bond { set { double next = Filled(value); if (Math.Abs(next - this.value) < .004) return; this.value = next; InvalidateVisual(); } }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Width, Height));   // so the tooltip shows between the hearts too
        var outline = new Pen(Pastel.Line, 1.6); outline.Freeze(); Color colour = ((SolidColorBrush)Pastel.Hot).Color; double scale = size / 22.0;
        for (int i = 0; i < Hearts; i++)
        {
            double part = Math.Clamp(value - i, 0, 1);
            dc.PushTransform(new TranslateTransform(i * (size + 4) + size / 2 + 2, size * .62 + 1)); dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(Brushes.White, null, heart);
            if (part >= 1) dc.DrawGeometry(Pastel.Hot, null, heart);
            else if (part > 0)
            {
                // Colour up to the filled part, then a short fade, so a heart looks as if it is being painted in.
                var paint = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                paint.GradientStops.Add(new GradientStop(colour, 0)); paint.GradientStops.Add(new GradientStop(colour, Math.Max(0, part - .12)));
                paint.GradientStops.Add(new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), Math.Min(1, part + .12))); paint.Freeze();
                dc.DrawGeometry(paint, null, heart);
            }
            dc.DrawGeometry(null, outline, heart);
            dc.Pop(); dc.Pop();
        }
    }
}
