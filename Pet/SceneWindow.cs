using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KeyboardWorker;

// A picture of the two pets together, shown above them for a few seconds. A click puts it away.
public sealed class SceneWindow : Window
{
    readonly DispatcherTimer leave = new() { Interval = TimeSpan.FromSeconds(9) };
    public SceneWindow(Buddy buddy, string path, string title)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Native.Software(this);
        ResizeMode = ResizeMode.NoResize; Topmost = true; Width = 340; SizeToContent = SizeToContent.Height; Icon = PetIcon.WindowIcon;
        var panel = new StackPanel();
        try { panel.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = new Image { Source = ImageAssets.Read(path, 640), Stretch = Stretch.Uniform, MaxHeight = 300 } }); }
        catch (Exception e) when (e is System.IO.IOException or NotSupportedException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException) { panel.Children.Add(Pastel.Text("그림을 열 수 없어요.", 12)); }
        panel.Children.Add(new TextBlock { Text = "♡ " + title, Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
        Content = new Border { Child = panel, Background = Pastel.Paper, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 3, 3), CornerRadius = new CornerRadius(16), Padding = new Thickness(10), Margin = new Thickness(6) };
        Cursor = System.Windows.Input.Cursors.Hand; ToolTip = "누르면 닫혀요"; MouseLeftButtonDown += (_, _) => Close();
        SourceInitialized += (_, _) => { var handle = new WindowInteropHelper(this).Handle; Native.Style(handle, false); Native.RaisePet(handle); };
        Loaded += (_, _) =>
        {
            UpdateLayout(); FloatingPlacement.Place(this, buddy, "위", new DesktopOptions { BubbleGap = 8 }, buddy.OtherPet(buddy), buddy.OtherBubble(buddy), buddy.MusicBounds);
            if (!buddy.Pref.LowPower) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
        };
        leave.Tick += (_, _) => { leave.Stop(); if (IsMouseOver) { leave.Start(); return; } var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500)); fade.Completed += (_, _) => Close(); BeginAnimation(OpacityProperty, fade); };
        leave.Start(); Closed += (_, _) => leave.Stop();
    }
}
