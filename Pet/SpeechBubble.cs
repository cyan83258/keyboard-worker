using System;
using System.Threading;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace KeyboardWorker;

// A dragged bubble keeps its place relative to the pet window. Above the pet it is pinned by its bottom edge, so longer text grows upward.
public sealed record BubbleSpot(double X, double Y, bool FromBottom)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool Valid => double.IsFinite(X) && double.IsFinite(Y) && Math.Abs(X) <= 10000 && Math.Abs(Y) <= 10000;
    public static BubbleSpot From(Rect bubble, Rect pet)
    {
        bool above = bubble.Top + bubble.Height / 2 < pet.Top + pet.Height / 2;
        return new(bubble.Left - pet.Left, (above ? bubble.Bottom : bubble.Top) - pet.Top, above);
    }
    public Point Place(Size bubble, Point pet, Rect area)
    {
        double left = pet.X + X, top = pet.Y + Y - (FromBottom ? bubble.Height : 0);
        return new Point(Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - bubble.Width)), Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - bubble.Height)));
    }
}
public static class FloatingPlacement
{
    public static Point Bubble(Size size, Rect pet, Rect area, string position, double gap, double dx, double dy, Rect? otherPet = null, Rect? otherBubble = null, Rect? overlay = null)
    {
        Point Candidate(string side)
        {
            double x = pet.Left + (pet.Width - size.Width) / 2, y = pet.Top - size.Height - gap;
            if (side == "아래") y = pet.Bottom + gap;
            if (side == "왼쪽") { x = pet.Left - size.Width - gap; y = pet.Top; }
            if (side == "오른쪽") { x = pet.Right + gap; y = pet.Top; }
            x += dx; y += dy;
            if (side == "위") y = Math.Min(y, pet.Top - size.Height - gap);
            if (side == "아래") y = Math.Max(y, pet.Bottom + gap);
            if (side == "왼쪽") x = Math.Min(x, pet.Left - size.Width - gap);
            if (side == "오른쪽") x = Math.Max(x, pet.Right + gap);
            return new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size.Width)), Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
        }
        var excluded = pet; excluded.Inflate(gap, gap);
        bool Overlap(Rect a, Rect b) { var intersection = Rect.Intersect(a, b); return !intersection.IsEmpty && intersection.Width > 0 && intersection.Height > 0; }
        foreach (var side in new[] { position, "위", "아래", "왼쪽", "오른쪽" })
        {
            var point = Candidate(side); var box = new Rect(point, size);
            if (!Overlap(box, excluded) && (!otherPet.HasValue || !Overlap(box, otherPet.Value)) && (!otherBubble.HasValue || !Overlap(box, otherBubble.Value)) && (!overlay.HasValue || !Overlap(box, overlay.Value))) return point;
        }
        // If offsets cause overlap, retry without them instead of covering the pet.
        if (dx != 0 || dy != 0) return Bubble(size, pet, area, position, gap, 0, 0, otherPet, otherBubble, overlay);
        return Candidate(position);
    }
    public static void Place(Window window, Window buddy, string position, DesktopOptions? options = null, Rect? otherPet = null, Rect? otherBubble = null, Rect? overlay = null)
    {
        var pet = buddy is IPetAnchor anchor ? anchor.PetBounds : new Rect(buddy.Left, buddy.Top, buddy.Width, buddy.Height);
        var pixel = buddy.PointToScreen(new Point(buddy.Width / 2, buddy.Height / 2));
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)pixel.X, (int)pixel.Y)).WorkingArea;
        var matrix = PresentationSource.FromVisual(buddy)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var start = matrix.Transform(new Point(screen.Left, screen.Top)); var end = matrix.Transform(new Point(screen.Right, screen.Bottom));
        var at = Bubble(new Size(window.Width, Math.Max(1, window.ActualHeight)), pet, new Rect(start, end), position, options?.BubbleGap ?? 4, options?.BubbleOffsetX ?? 0, options?.BubbleOffsetY ?? 0, otherPet, otherBubble, overlay);
        window.Left = at.X; window.Top = at.Y;
    }
}
public sealed class PetBubbleWindow : Window
{
    readonly Buddy buddy;
    readonly Window anchor;
    readonly Func<string>? name;
    readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock caption = Pastel.Text("", 11);
    readonly Border border;
    readonly ScrollViewer scroll;
    readonly BubbleLifetime lifetime = new();
    readonly Button keep = new() { Content = "♡", Padding = new Thickness(5, 0, 5, 1), FontWeight = FontWeights.Bold, ToolTip = "이 대사를 도감에 담기" };
    public bool IsReply { get; private set; }
    bool dragging;
    BubbleSpot? Spot
    {
        get => ReferenceEquals(anchor, buddy) ? buddy.Pref.BubbleSpot : buddy.Pref.CompanionBubbleSpot;
        set { if (ReferenceEquals(anchor, buddy)) buddy.Pref.BubbleSpot = value; else buddy.Pref.CompanionBubbleSpot = value; Storage.Save(buddy.Pref); }
    }
    public string LastText => text.Text;
    string Speaker => name?.Invoke() ?? buddy.Chat.Preferences.Profile.Name;
    void MarkKept() { bool kept = buddy.Collection.Has(Speaker, text.Text.Trim()); keep.Content = kept ? "♥" : "♡"; keep.ToolTip = kept ? "도감에서 빼기" : "이 대사를 도감에 담기"; }
    public PetBubbleWindow(Buddy buddy, Window? anchor = null, Func<string>? name = null)
    {
        this.buddy = buddy; this.anchor = anchor ?? buddy; this.name = name; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowActivated = false;
        ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Height; Icon = PetIcon.WindowIcon;
        var panel = new DockPanel(); var header = new DockPanel();
        var close = new Button { Content = "×", Padding = new Thickness(6, 0, 6, 1), FontWeight = FontWeights.Bold }; Pastel.Ghost(close); caption.FontWeight = FontWeights.Bold;
        close.Click += (_, _) => Hide();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        // ♡ keeps the line in the collection; pressing it again takes it out.
        Pastel.Ghost(keep); DockPanel.SetDock(keep, Dock.Right); header.Children.Add(keep);
        keep.Click += (_, _) =>
        {
            string speaker = Speaker, line = text.Text; if (lifetime.Streaming || line.Length == 0) return;
            var kept = buddy.Collection.Items.FirstOrDefault(l => l.Speaker == speaker && l.Text == line.Trim());
            if (kept != null) buddy.Collection.Remove(kept.Id); else buddy.Collection.Add(this.anchor is CompanionWindow friend ? friend.Preset.Id : buddy.CharacterId, speaker, line);
            MarkKept();
        };
        header.Children.Add(caption);
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        MouseEnter += (_, _) => lifetime.Hover(true, Environment.TickCount64);
        MouseLeave += (_, _) => lifetime.Hover(false, Environment.TickCount64);
        IsVisibleChanged += (_, _) => { if (!IsVisible) lifetime.Hover(false, Environment.TickCount64); };
        scroll = new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; panel.Children.Add(scroll);
        border = new Border { Child = panel, Padding = new Thickness(12), BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 3, 3.5), CornerRadius = new CornerRadius(16) };
        Content = border;
        // Drag the bubble to put it where you like; it then keeps that place relative to its pet.
        MouseLeftButtonDown += (_, e) =>
        {
            for (var at = e.OriginalSource as DependencyObject; at != null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at)) if (at is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.ScrollBar) return;
            if (buddy.Pref.Locked) return; double left = Left, top = Top; dragging = true;
            try { DragMove(); } catch (InvalidOperationException) { } finally { dragging = false; }
            if (Math.Abs(Left - left) > 1 || Math.Abs(Top - top) > 1) Spot = BubbleSpot.From(new Rect(Left, Top, ActualWidth, ActualHeight), new Rect(this.anchor.Left, this.anchor.Top, this.anchor.ActualWidth, this.anchor.ActualHeight));
            Reposition(); e.Handled = true;
        };
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!; Native.Style(source.Handle, false);
            source.AddHook((IntPtr h, int m, IntPtr w, IntPtr l, ref bool handled) => { if (m == 0x21) { handled = true; return new IntPtr(3); } return IntPtr.Zero; });
        };
    }
    public void Configure()
    {
        var p = buddy.Pref.Desktop; Width = p.BubbleWidth; scroll.MaxHeight = p.BubbleHeight; text.FontSize = p.BubbleFont;
        var font = FontAssets.Get(buddy.Pref.BubbleFontPath); if (!ReferenceEquals(text.FontFamily, font)) text.FontFamily = font;
        bool second = !ReferenceEquals(anchor, buddy);
        var fill = Pastel.Custom(second ? p.BubbleBackground2 : p.BubbleBackground, second ? DesktopOptions.StandardBubbleBackground2 : DesktopOptions.StandardBubbleBackground);
        var ink = Pastel.Custom(second ? p.BubbleText2 : p.BubbleText, DesktopOptions.StandardBubbleText); if (Pastel.Contrast(ink, fill) < 2) ink = Pastel.Readable(fill);
        text.Foreground = ink; caption.Foreground = ink; border.Background = fill; Opacity = p.BubbleOpacity / 100.0; Topmost = buddy.Pref.Topmost;
        caption.Text = "♡ " + (name?.Invoke() ?? buddy.Chat.Preferences.Profile.Name);
        if (IsVisible) { if (!lifetime.Streaming) lifetime.Start(Environment.TickCount64, p.BubbleHideSeconds ?? (IsReply ? p.ReplySeconds : buddy.Pref.BubbleSeconds)); UpdateLayout(); Reposition(); }
    }
    public void ShowText(string message, bool reply, int seconds)
    {
        IsReply = reply;
        text.Text = message;
        buddy.Remember(Speaker, message);
        Configure(); MarkKept();
        lifetime.Start(Environment.TickCount64, buddy.Pref.Desktop.BubbleHideSeconds ?? seconds);
        Show();
        UpdateLayout();
        Reposition();
        scroll.ScrollToTop();
    }
    public void UpdateStreaming(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (!IsVisible) { ShowText(message, true, buddy.Pref.Desktop.ReplySeconds); lifetime.Streaming = true; return; }
        double height = ActualHeight; IsReply = true; text.Text = message; lifetime.Streaming = true;
        UpdateLayout(); if (Math.Abs(ActualHeight - height) > .5) Reposition(); scroll.ScrollToEnd();
    }
    public void Reposition()
    {
        if (!IsVisible || dragging) return;
        if (Spot is not BubbleSpot spot) { FloatingPlacement.Place(this, anchor, buddy.Pref.Desktop.BubblePosition, buddy.Pref.Desktop, buddy.OtherPet(anchor), buddy.OtherBubble(anchor), buddy.MusicBounds); return; }
        // Keep the dragged place, but never let the bubble leave the screen it lands on.
        var target = PresentationSource.FromVisual(anchor)?.CompositionTarget;
        Matrix toDevice = target?.TransformToDevice ?? Matrix.Identity, fromDevice = target?.TransformFromDevice ?? Matrix.Identity;
        var size = new Size(Width, Math.Max(1, ActualHeight)); var wanted = spot.Place(size, new Point(anchor.Left, anchor.Top), new Rect(-100000, -100000, 200000, 200000));
        var centre = toDevice.Transform(new Point(wanted.X + size.Width / 2, wanted.Y + size.Height / 2));
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)centre.X, (int)centre.Y)).WorkingArea;
        var at = spot.Place(size, new Point(anchor.Left, anchor.Top), new Rect(fromDevice.Transform(new Point(screen.Left, screen.Top)), fromDevice.Transform(new Point(screen.Right, screen.Bottom))));
        Left = at.X; Top = at.Y;
    }
    public void Raise() { if (IsVisible) Native.RaisePet(new WindowInteropHelper(this).Handle); }
    public void Tick(long now, bool hidden) { if (hidden || lifetime.Expired(now, buddy.Pref.Desktop.BubbleAutoHide)) Hide(); }
}
