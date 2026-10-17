using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KeyboardWorker;

public sealed class MusicPlayerWindow : Window
{
    readonly Buddy buddy; readonly TextBlock title = Pastel.Text("", 15), artist = Pastel.Text("", 11), status = Pastel.Text("", 10), time = Pastel.Text("", 10);
    readonly TranslateTransform slide = new(); readonly System.Windows.Shapes.Rectangle[] bars = new System.Windows.Shapes.Rectangle[5];
    readonly Grid clip = new() { Height = 24, ClipToBounds = true }, progressRow = new() { Margin = new Thickness(0, 0, 0, 2) }, progressTrack = new() { Height = 14, Background = Brushes.Transparent };
    readonly ColumnDefinition played = new() { Width = new GridLength(0, GridUnitType.Star) }, remaining = new() { Width = new GridLength(1, GridUnitType.Star) };
    readonly DockPanel volumeRow = new() { Margin = new Thickness(0, 0, 0, 2) };
    readonly Slider volume = new() { Minimum = 0, Maximum = 100, LargeChange = 10, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center, ToolTip = "브라우저 음량 · Windows 볼륨 믹서와 같은 값" };
    readonly Button play, locking, fold = new() { Padding = new Thickness(6, 0, 6, 1), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    readonly System.Collections.Generic.List<UIElement> detail = new();
    bool closed, busy, syncingVolume, hovering;
    // The equalizer is decoration: a few steps a second cost far less than a continuous animation.
    readonly System.Windows.Threading.DispatcherTimer beat = new() { Interval = TimeSpan.FromMilliseconds(280) }; string track = ""; long volumeReadAt = long.MinValue / 2;
    public MusicPlayerWindow(Buddy buddy)
    {
        this.buddy = buddy;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true; Native.Software(this);
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Width = 330;
        SizeToContent = SizeToContent.Height;
        Icon = PetIcon.WindowIcon;
        Resources = Pastel.Styles(); Foreground = Pastel.Ink; FontFamily = new FontFamily("Malgun Gothic"); FontSize = 12;
        artist.TextWrapping = TextWrapping.NoWrap; artist.TextTrimming = TextTrimming.CharacterEllipsis; artist.Margin = new Thickness(0, 2, 0, 4);
        var p = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        var header = new DockPanel(); var equalizer = new StackPanel { Orientation = Orientation.Horizontal, Height = 18, Width = 46 }; DockPanel.SetDock(equalizer, Dock.Right);
        for (int i = 0; i < bars.Length; i++) { bars[i] = new() { Width = 5, Height = 5, Fill = i % 2 == 0 ? Pastel.Pink : Pastel.Blue, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 0) }; equalizer.Children.Add(bars[i]); }
        header.Children.Add(equalizer);
        var nowPlaying = Pastel.Text("♡ NOW PLAYING", 11);
        nowPlaying.Foreground = Pastel.Hot;
        nowPlaying.FontWeight = FontWeights.Bold;
        nowPlaying.Margin = new Thickness(0, 2, 0, 4);
        header.Children.Add(nowPlaying);
        p.Children.Add(header);
        detail.Add(header);
        title.TextWrapping = TextWrapping.NoWrap;
        title.RenderTransform = slide;
        title.Margin = new Thickness(0);
        title.VerticalAlignment = VerticalAlignment.Center;
        clip.Children.Add(title);
        clip.Background = Brushes.Transparent;
        ToolTipService.SetInitialShowDelay(clip, 250);
        // Resting the mouse on the title stops the scrolling and shows the whole title.
        clip.MouseEnter += (_, _) => { hovering = true; slide.BeginAnimation(TranslateTransform.XProperty, null); slide.X = 0; };
        clip.MouseLeave += (_, _) => { hovering = false; track = ""; Update(); };
        Pastel.Ghost(fold); fold.Click += (_, _) => { buddy.Pref.Music = buddy.Pref.Music with { Compact = !buddy.Pref.Music.Compact }; Storage.Save(buddy.Pref); track = ""; Update(); };
        var titleRow = new DockPanel();
        DockPanel.SetDock(fold, Dock.Right);
        titleRow.Children.Add(fold);
        titleRow.Children.Add(clip);
        p.Children.Add(titleRow);
        p.Children.Add(artist);
        detail.Add(artist);
        // Progress: the filled part is a star-sized column, so it follows the window width without measuring.
        progressTrack.ColumnDefinitions.Add(played); progressTrack.ColumnDefinitions.Add(remaining);
        var rail = new Border { Height = 6, Background = Brushes.White, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumnSpan(rail, 2);
        progressTrack.Children.Add(rail);
        progressTrack.Children.Add(new Border { Height = 6, Background = Pastel.Hot, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center });
        progressTrack.MouseLeftButtonDown += async (_, e) => { e.Handled = true; await Seek(e.GetPosition(progressTrack).X / Math.Max(1, progressTrack.ActualWidth)); };
        time.TextWrapping = TextWrapping.NoWrap; time.Margin = new Thickness(8, 0, 0, 0); time.VerticalAlignment = VerticalAlignment.Center;
        progressRow.ColumnDefinitions.Add(new ColumnDefinition());
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(time, 1);
        progressRow.Children.Add(progressTrack);
        progressRow.Children.Add(time);
        p.Children.Add(progressRow);
        var row = new WrapPanel();
        row.Children.Add(Pastel.Button("◀◀", async () => await Control("previous"), true));
        play = Pastel.Button("▶ / Ⅱ", async () => await Control("toggle"));
        row.Children.Add(play);
        row.Children.Add(Pastel.Button("▶▶", async () => await Control("next"), true));
        locking = Pastel.Button("◇", () => { buddy.Pref.Music = buddy.Pref.Music with { Locked = !buddy.Pref.Music.Locked }; Storage.Save(buddy.Pref); Update(); }, true);
        row.Children.Add(locking);
        p.Children.Add(row);
        detail.Add(row);
        row.Children.Add(Pastel.Button("×", () => { buddy.Pref.Music = buddy.Pref.Music with { PlayerVisible = false }; Storage.Save(buddy.Pref); Sleep(); }, true));
        foreach (Button button in row.Children) { button.Width = 46; button.Height = 30; button.Padding = new Thickness(6, 3, 6, 3); button.Margin = new Thickness(3, 3, 3, 2); }
        var volumeLabel = Pastel.Text("♪ 음량", 10); volumeLabel.Margin = new Thickness(3, 0, 8, 0); volumeLabel.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(volumeLabel, Dock.Left);
        volumeRow.Children.Add(volumeLabel); volumeRow.Children.Add(volume); volumeRow.Visibility = Visibility.Collapsed; p.Children.Add(volumeRow);
        volume.ValueChanged += (_, _) => { if (syncingVolume) return; volumeReadAt = Environment.TickCount64; Note(buddy.Music.SetVolume((int)volume.Value) ? "" : "브라우저 음량을 바꾸지 못했습니다."); };
        status.Margin = new Thickness(0, 2, 0, 0); status.Visibility = Visibility.Collapsed; p.Children.Add(status);
        Content = new Border { Child = p, Background = Pastel.Paper, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 3, 3), CornerRadius = new CornerRadius(14) };
        SourceInitialized += (_, _) => { Native.Style(new WindowInteropHelper(this).Handle, false); Native.RaisePet(new WindowInteropHelper(this).Handle); };
        MouseLeftButtonDown += (_, e) =>
        {
            DependencyObject? at = e.OriginalSource as DependencyObject;
            while (at != null) { if (at is Button) return; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at); } if (buddy.Pref.Music.Locked) return;
            try { DragMove(); buddy.Pref.Music = buddy.Pref.Music with { FollowHead = false, Left = Left, Top = Top }; Storage.Save(buddy.Pref); } catch (InvalidOperationException) { }
        };
        beat.Tick += (_, _) => { for (int i = 0; i < bars.Length; i++) bars[i].Height = 4 + Random.Shared.Next(9 + i); };
        Closed += (_, _) => { closed = true; StopAnimation(); }; Loaded += (_, _) => Update();
    }
    void Note(string text) { status.Text = text; status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible; }
    static string Clock(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    async System.Threading.Tasks.Task Control(string command)
    {
        if (busy) return; busy = true;
        try { bool ok = await buddy.Music.Control(command); Note(ok ? "" : "브라우저가 이 제어를 지원하지 않습니다."); }
        catch (Exception e) { Note(e.Message); } finally { busy = false; }
    }
    async System.Threading.Tasks.Task Seek(double ratio)
    {
        var line = buddy.Music.Timeline; if (busy || !line.Available || !line.CanSeek) return; busy = true;
        try { bool ok = await buddy.Music.Seek(TimeSpan.FromTicks((long)(line.Duration.Ticks * Math.Clamp(ratio, 0, 1)))); Note(ok ? "" : "브라우저가 재생 위치 이동을 지원하지 않습니다."); }
        catch (Exception e) { Note(e.Message); } finally { busy = false; }
        Update();
    }
    void StopAnimation() { slide.BeginAnimation(TranslateTransform.XProperty, null); beat.Stop(); foreach (var bar in bars) bar.Height = 5; }
    public void Update()
    {
        if (closed) return;
        var song = buddy.Music.Song;
        long now = Environment.TickCount64;
        title.Text = song.Title;
        artist.Text = song.Artist + (song.YouTubeMusicConfirmed ? " · YouTube Music" : " · 브라우저 미디어");
        locking.Content = buddy.Pref.Music.Locked ? "◆" : "◇";
        play.Content = song.Playing ? "Ⅱ" : "▶"; clip.ToolTip = song.Artist.Length > 0 ? song.Title + "\n" + song.Artist : song.Title;
        // Folded: only the title line stays.
        bool compact = buddy.Pref.Music.Compact; fold.Content = compact ? "▾" : "▴"; fold.ToolTip = compact ? "플레이어 펼치기" : "제목만 보기";
        foreach (var part in detail) part.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; double wide = compact ? 250 : 330; if (Width != wide) { Width = wide; track = ""; }
        var line = buddy.Music.Timeline; progressRow.Visibility = line.Available && !compact ? Visibility.Visible : Visibility.Collapsed;
        if (line.Available)
        {
            var at = line.Now(now); double ratio = Math.Clamp(at.TotalSeconds / line.Duration.TotalSeconds, 0, 1);
            played.Width = new GridLength(ratio, GridUnitType.Star); remaining.Width = new GridLength(1 - ratio, GridUnitType.Star);
            time.Text = Clock(at) + " / " + Clock(line.Duration); progressTrack.Cursor = line.CanSeek ? Cursors.Hand : Cursors.Arrow;
        }
        bool still = buddy.Pref.LowPower || buddy.IsLight;
        if (track != song.Key + song.Playing + still)
        {
            track = song.Key + song.Playing + still; StopAnimation(); slide.X = 0; volumeReadAt = long.MinValue / 2;
            if (song.Playing && !still) beat.Start();
            if (song.Playing && !still && !hovering)
            {
                UpdateLayout(); title.Measure(new Size(double.PositiveInfinity, 24)); double overflow = Math.Max(0, title.DesiredSize.Width - (clip.ActualWidth > 0 ? clip.ActualWidth : 300));
                if (overflow > 0)
                {
                    // Rest on the start of the title, scroll to its end, pause, then jump back quickly so the start is never away for long.
                    double hold = 2.5, travel = Math.Max(1.5, overflow / 45), rest = 1.2, back = .45; var scroll = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                    scroll.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    scroll.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold))));
                    scroll.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold + travel))));
                    scroll.KeyFrames.Add(new DiscreteDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold + travel + rest))));
                    scroll.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold + travel + rest + back)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                    // A slow scroll does not need the full refresh rate.
                    Timeline.SetDesiredFrameRate(scroll, 30); slide.BeginAnimation(TranslateTransform.XProperty, scroll);
                }
            }
        }
        // The mixer is polled sparingly, and never while the user is dragging the slider.
        if (compact) volumeRow.Visibility = Visibility.Collapsed;
        else if (now - volumeReadAt >= (buddy.Pref.LowPower ? 10000 : 3000) && !volume.IsMouseCaptureWithin)
        {
            volumeReadAt = now; int? level = buddy.Music.Volume; volumeRow.Visibility = level == null ? Visibility.Collapsed : Visibility.Visible;
            if (level != null && (int)volume.Value != level) { syncingVolume = true; volume.Value = level.Value; syncingVolume = false; }
        }
        UpdateLayout();
        if (buddy.Pref.Music.FollowHead || buddy.Pref.Music.Left == -9999) FloatingPlacement.Place(this, buddy, "위", new DesktopOptions { BubbleGap = 8 });
        else
        {
            var point = buddy.PointToScreen(new Point(buddy.Width / 2, buddy.Height / 2));
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y)).WorkingArea;
            var matrix = PresentationSource.FromVisual(buddy)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var start = matrix.Transform(new Point(screen.Left, screen.Top));
            var end = matrix.Transform(new Point(screen.Right, screen.Bottom));
            Left = Math.Clamp(buddy.Pref.Music.Left, start.X, Math.Max(start.X, end.X - Width)); Top = Math.Clamp(buddy.Pref.Music.Top, start.Y, Math.Max(start.Y, end.Y - ActualHeight));
        }
    }
    public void Raise() { if (IsVisible) Native.RaisePet(new WindowInteropHelper(this).Handle); }
    public void Sleep() { StopAnimation(); track = ""; Hide(); }
}
