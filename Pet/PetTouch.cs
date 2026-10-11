using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace KeyboardWorker;

public sealed class StrokeGesture
{
    Point? last;
    long began, previous, cooldown;
    double distance, direction;
    int turns;
    public void Reset() { last = null; distance = direction = 0; turns = 0; }
    public bool Move(Point point, long now)
    {
        if (last == null || now - previous > 600 || now - began > 1800) { Reset(); last = point; began = previous = now; return false; }
        var delta = point - last.Value; last = point; previous = now;
        if (delta.Length < 2 || delta.Length > 100) return false;
        double nextDirection = Math.Abs(delta.X) >= Math.Abs(delta.Y) ? Math.Sign(delta.X) : Math.Sign(delta.Y);
        if (direction != 0 && nextDirection != direction) turns++; direction = nextDirection; distance += delta.Length;
        if (now < cooldown || distance < 100 || turns < 2) return false;
        cooldown = now + 2500; Reset(); return true;
    }
}
public sealed class HeartTrail : FrameworkElement, IDisposable
{
    sealed record Heart(Point At, long Born, double Drift);
    readonly List<Heart> particles = new();
    static readonly Geometry shape = Geometry.Parse("M 0,3 C -12,-5 -6,-14 0,-7 C 6,-14 12,-5 0,3 Z");
    static readonly Brush pink = new SolidColorBrush(Color.FromRgb(255, 167, 211));
    long lastEmit, lastFrame;
    bool rendering;
    public bool LowPower { get; set; }
    static HeartTrail() { shape.Freeze(); pink.Freeze(); }
    public HeartTrail() { IsHitTestVisible = false; }
    public void Emit(Point position)
    {
        long now = Environment.TickCount64; if (now - lastEmit < 65) return; lastEmit = now;
        if (particles.Count >= (LowPower ? 6 : 12)) particles.RemoveAt(0); particles.Add(new(position, now, Random.Shared.NextDouble() * 16 - 8));
        InvalidateVisual();
        if (!rendering) { rendering = true; CompositionTarget.Rendering += Frame; }
    }
    void Frame(object? sender, EventArgs args)
    {
        long now = Environment.TickCount64; if (now - lastFrame < (LowPower ? 67 : 33)) return; lastFrame = now;
        particles.RemoveAll(p => now - p.Born >= 1100); InvalidateVisual();
        if (particles.Count == 0) Stop();
    }
    protected override void OnRender(DrawingContext dc)
    {
        foreach (var p in particles)
        {
            double t = Math.Clamp((Environment.TickCount64 - p.Born) / 1100.0, 0, 1);
            dc.PushOpacity(.55 * (1 - t)); dc.PushTransform(new TranslateTransform(p.At.X + p.Drift * t, p.At.Y - 35 * t));
            dc.DrawGeometry(pink, null, shape); dc.Pop(); dc.Pop();
        }
    }
    void Stop() { if (rendering) CompositionTarget.Rendering -= Frame; rendering = false; }
    public void Clear() { particles.Clear(); Stop(); InvalidateVisual(); }
    public void Dispose() => Clear();
}
