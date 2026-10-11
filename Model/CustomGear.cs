using System;
using System.Linq;

namespace KeyboardWorker;

public sealed record CustomGear
{
    public string Id { get; init; } = "custom-" + Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "내 장비";
    public string Slot { get; init; } = "head";
    public string ImagePath { get; init; } = "";
    public double WidthPercent { get; init; } = 35;
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    public double Rotation { get; init; }
    public bool Behind { get; init; }
    public static readonly string[] Slots = { "head", "face", "neck", "body", "effect" };
    public CustomGear Normalized()
    {
        static double Safe(double v, double min, double max, double fallback) => double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
        return this with { Name = (Name ?? "내 장비")[..Math.Min((Name ?? "내 장비").Length, 80)], WidthPercent = Safe(WidthPercent, 5, 200, 35), OffsetX = Safe(OffsetX, -100, 100, 0), OffsetY = Safe(OffsetY, -100, 100, 0), Rotation = Safe(Rotation, -180, 180, 0) };
    }
}
