using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KeyboardWorker;

public static class BounceTests
{
    public static int Run()
    {
        int count = 0; void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        var grid = new Grid(); var window = new Window { Content = grid, Width = 200, Height = 160, Left = -10000, Top = -10000, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        window.Show(); window.UpdateLayout(); using var effect = new TypingBounce(grid);
        try
        {
            effect.Configure(false); effect.Tap(true, 5); Check(!effect.Active && effect.VerticalOffset == 0 && effect.Angle == 0, "Bounce off stays neutral");
            effect.Configure(true); effect.Tap(true, 5); Check(Until(() => effect.Active && effect.VerticalOffset < 0 && effect.Angle < 0), "Left tap jumps and tilts left");
            Check(Math.Abs(grid.RenderTransform.Value.Determinant - 1) < .00001, "Active bounce preserves character area without scaling");
            effect.Tap(false, 5); Check(Until(() => effect.Active && effect.Angle > 0), "Right tap tilts right");
            for (int n = 0; n < 100; n++) effect.Tap(n % 2 == 0, 10); Wait(90); Check(Math.Abs(effect.VerticalOffset) <= 12 && Math.Abs(effect.Angle) <= 3, "Rapid typing never accumulates movement");
            Wait(300); Check(!effect.Active && effect.VerticalOffset == 0 && effect.Angle == 0, "Bounce returns to rest and releases animation clocks");
            effect.Tap(true, 10); Wait(50); effect.Configure(false); Check(!effect.Active && effect.VerticalOffset == 0 && effect.Angle == 0, "Disable mid-animation resets immediately");
        }
        finally { window.Close(); }
        return count;
    }
    static void Wait(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    static bool Until(Func<bool> condition)
    {
        bool found = false; long until = Environment.TickCount64 + 600; var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
        timer.Tick += (_, _) => { found = condition(); if (found || Environment.TickCount64 >= until) { timer.Stop(); frame.Continue = false; } };
        timer.Start(); Dispatcher.PushFrame(frame); return found;
    }
}
