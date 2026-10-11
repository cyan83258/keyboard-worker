using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace KeyboardWorker;

public sealed class Character : FrameworkElement
{
    public string State { get; set; } = "idle";
    readonly System.Collections.Generic.Dictionary<string, DrawingGroup> cached = new();
    protected override void OnRender(DrawingContext target)
    {
        base.OnRender(target);
        target.PushTransform(new ScaleTransform(ActualWidth / 240, ActualHeight / 180));
        if (cached.TryGetValue(State, out var existing)) { target.DrawDrawing(existing); target.Pop(); return; }
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
        var outline = new Pen(new SolidColorBrush(Color.FromRgb(67, 56, 70)), 4);
        var fur = new SolidColorBrush(Color.FromRgb(246, 198, 129));
        var pink = new SolidColorBrush(Color.FromRgb(236, 143, 151));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)), null, new Point(120, 163), 87, 9);
        dc.DrawEllipse(fur, outline, new Point(120, 120), 58, 43);
        dc.DrawEllipse(fur, outline, new Point(77, 32), 19, 24); dc.DrawEllipse(fur, outline, new Point(163, 32), 19, 24);
        dc.DrawEllipse(pink, null, new Point(77, 30), 9, 14); dc.DrawEllipse(pink, null, new Point(163, 30), 9, 14);
        dc.DrawRoundedRectangle(fur, outline, new Rect(62, 30, 116, 80), 34, 34);
        dc.DrawEllipse(pink, null, new Point(83, 81), 11, 6); dc.DrawEllipse(pink, null, new Point(157, 81), 11, 6);
        if (State == "nudge")
        {
            dc.DrawLine(outline, new Point(89, 60), new Point(104, 65)); dc.DrawLine(outline, new Point(136, 65), new Point(151, 60));
        }
        dc.DrawEllipse(Brushes.Black, null, new Point(99, 71), 4, 5); dc.DrawEllipse(Brushes.Black, null, new Point(141, 71), 4, 5);
        dc.DrawEllipse(outline.Brush, null, new Point(120, 82), 5, 3);
        dc.DrawLine(new Pen(outline.Brush, 2), new Point(115, 91), new Point(125, 91));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(86, 89, 111)), outline, new Rect(36, 137, 168, 27), 7, 7);
        for (int row = 0; row < 2; row++) for (int col = 0; col < 12; col++) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(205, 210, 223)), null, new Rect(43 + col * 13, 142 + row * 9, 10, 6), 1, 1);
        dc.DrawEllipse(fur, outline, new Point(87, State == "typing-left" ? 144 : 127), 19, 12);
        dc.DrawEllipse(fur, outline, new Point(153, State == "typing-right" ? 144 : 127), 19, 12);
        }
        drawing.Freeze(); cached[State] = drawing;
        target.DrawDrawing(drawing); target.Pop();
    }
}
