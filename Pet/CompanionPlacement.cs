using System;
using System.Windows;

namespace KeyboardWorker;

public static class CompanionPlacement
{
    public static Point Place(Size size, Rect primary, Rect area, Point? saved)
    {
        Point Clamp(Point p) => new(Math.Clamp(p.X, area.Left, Math.Max(area.Left, area.Right - size.Width)), Math.Clamp(p.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
        bool Clear(Point p)
        {
            var overlap = Rect.Intersect(new Rect(p, size), primary);
            return overlap.IsEmpty || overlap.Width * overlap.Height < Math.Min(size.Width * size.Height, primary.Width * primary.Height) * .65;
        }
        if (saved.HasValue) { var at = Clamp(saved.Value); if (Clear(at)) return at; }
        foreach (var candidate in new[] { new Point(primary.Left - size.Width - 24, primary.Top), new Point(primary.Right + 24, primary.Top), new Point(primary.Left, primary.Top - size.Height - 24), new Point(primary.Left, primary.Bottom + 24) })
        { var at = Clamp(candidate); if (Clear(at)) return at; }
        return Clamp(new Point(primary.Left - size.Width - 24, primary.Top));
    }
}
