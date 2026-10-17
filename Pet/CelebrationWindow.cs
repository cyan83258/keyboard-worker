using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KeyboardWorker;

// A burst of stars and hearts, drawn for a couple of seconds and then gone.
public sealed class ConfettiBurst : FrameworkElement
{
    sealed record Piece(double Angle, double Speed, double Size, int Shape, Brush Fill, double Spin);
    static readonly Geometry star = Geometry.Parse("M 0,-10 L 2.6,-3 10,-3 4,1.6 6.2,9 0,4.6 -6.2,9 -4,1.6 -10,-3 -2.6,-3 Z"), heart = Geometry.Parse("M 0,6 C -14,-4 -7,-14 0,-6 C 7,-14 14,-4 0,6 Z");
    readonly List<Piece> pieces = new(); long started, lastFrame; bool rendering; const double Life = 2400;
    public bool LowPower { get; set; }
    static ConfettiBurst() { star.Freeze(); heart.Freeze(); }
    public ConfettiBurst() { IsHitTestVisible = false; Unloaded += (_, _) => Stop(); }
    public void Start()
    {
        var fills = new[] { Pastel.Hot, Pastel.Pink, Pastel.Blue, Pastel.Original("#FFD66B"), Brushes.White }; pieces.Clear();
        for (int i = 0; i < (LowPower ? 12 : 30); i++) pieces.Add(new(-Math.PI / 2 + (Random.Shared.NextDouble() - .5) * 2.4, 120 + Random.Shared.NextDouble() * 190, .5 + Random.Shared.NextDouble() * .7, Random.Shared.Next(3), fills[Random.Shared.Next(fills.Length)], (Random.Shared.NextDouble() - .5) * 500));
        started = Environment.TickCount64; if (!rendering) { rendering = true; CompositionTarget.Rendering += Frame; }
        InvalidateVisual();
    }
    void Frame(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64; if (now - lastFrame < (LowPower ? 67 : 16)) return; lastFrame = now;
        if (now - started >= Life) { pieces.Clear(); Stop(); }
        InvalidateVisual();
    }
    void Stop() { if (rendering) CompositionTarget.Rendering -= Frame; rendering = false; }
    protected override void OnRender(DrawingContext dc)
    {
        if (pieces.Count == 0) return; double t = (Environment.TickCount64 - started) / 1000.0, life = Math.Clamp(t * 1000 / Life, 0, 1);
        var origin = new Point(ActualWidth / 2, ActualHeight * .62);
        foreach (var p in pieces)
        {
            // Thrown upward, slowed by drag, pulled down by gravity.
            double travelled = p.Speed * (1 - Math.Exp(-1.8 * t)) / 1.8 * 1.6, x = origin.X + Math.Cos(p.Angle) * travelled, y = origin.Y + Math.Sin(p.Angle) * travelled + 95 * t * t;
            dc.PushOpacity(Math.Clamp(1.6 * (1 - life), 0, 1));
            dc.PushTransform(new TranslateTransform(x, y));
            dc.PushTransform(new RotateTransform(p.Spin * t));
            dc.PushTransform(new ScaleTransform(p.Size, p.Size));
            if (p.Shape == 0) dc.DrawGeometry(p.Fill, null, star); else if (p.Shape == 1) dc.DrawGeometry(p.Fill, null, heart); else dc.DrawEllipse(p.Fill, null, new Point(0, 0), 5, 5);
            dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
        }
    }
}

// Shown above the pet when a story quest is reached, a new quest arrives or a finished story lands in the storybook.
public sealed class CelebrationWindow : Window
{
    readonly DispatcherTimer leave = new() { Interval = TimeSpan.FromSeconds(14) };
    public CelebrationWindow(Buddy buddy, string headline, string title, string detail, string action, Action open, bool effects)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true; Native.Software(this);
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        Icon = PetIcon.WindowIcon;
        Resources = Pastel.Styles(); Foreground = Pastel.Ink; FontFamily = new FontFamily("Malgun Gothic"); FontSize = 12;
        var text = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        text.Children.Add(new TextBlock { Text = "✦ " + headline + " ✦", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center });
        text.Children.Add(new TextBlock { Text = title, Foreground = Pastel.Ink, FontWeight = FontWeights.Bold, FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 2) });
        if (detail.Length > 0) text.Children.Add(new TextBlock { Text = detail, Foreground = Pastel.Ink, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Pastel.Button(action, () => { Close(); open(); })); buttons.Children.Add(Pastel.Button("닫기", Close, true)); text.Children.Add(buttons);
        foreach (Button button in buttons.Children) { button.Padding = new Thickness(12, 4, 12, 4); button.Margin = new Thickness(4, 0, 4, 0); }
        var card = new Border
        {
            Child = text,
            Background = Pastel.Paper,
            BorderBrush = Pastel.Line,
            BorderThickness = new Thickness(1.5, 1.5, 3, 3),
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(18, 96, 18, 6),
            RenderTransformOrigin = new Point(.5, 1)
        };
        var confetti = new ConfettiBurst { LowPower = buddy.Pref.LowPower }; var layers = new Grid(); layers.Children.Add(card); layers.Children.Add(confetti); Content = layers;
        SourceInitialized += (_, _) => { var handle = new WindowInteropHelper(this).Handle; Native.Style(handle, false); Native.RaisePet(handle); };
        Loaded += (_, _) =>
        {
            UpdateLayout(); FloatingPlacement.Place(this, buddy, "위", new DesktopOptions { BubbleGap = 2 }, overlay: buddy.MusicBounds);
            if (!effects) return;
            // The card pops up, then the confetti flies.
            var pop = new ScaleTransform(.6, .6);
            card.RenderTransform = pop;
            var grow = new DoubleAnimation(.6, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { Amplitude = .5, EasingMode = EasingMode.EaseOut } };
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, grow); pop.BeginAnimation(ScaleTransform.ScaleYProperty, grow); confetti.Start();
        };
        // Stays while the mouse is on it; otherwise fades away by itself.
        leave.Tick += (_, _) => { leave.Stop(); if (IsMouseOver) { leave.Start(); return; } var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500)); fade.Completed += (_, _) => Close(); BeginAnimation(OpacityProperty, fade); };
        leave.Start(); Closed += (_, _) => leave.Stop();
    }
}
