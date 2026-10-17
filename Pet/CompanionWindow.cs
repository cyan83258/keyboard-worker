using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace KeyboardWorker;

public sealed class CompanionWindow : Window, IPetAnchor
{
    readonly Buddy owner;
    readonly bool persistPosition;
    readonly Grid body = new() { Background = Brushes.Transparent };
    readonly Character character = new();
    readonly Image image = new() { Stretch = Stretch.Uniform };
    readonly AccessoryLayer front = new(), back = new() { BackLayer = true };
    readonly HeartTrail hearts = new(); readonly StrokeGesture strokes = new();
    readonly LazyImageCache images, gear; readonly TypingBounce bounce; readonly HandReaction hands = new();
    readonly ReactionLimiter handGate = new(), bounceGate = new();
    readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(180) };
    readonly PetBubbleWindow bubble;
    Preferences appearance = new();
    long typedAt = long.MinValue / 2, expressedUntil;
    string pose = "idle", expression = "idle";
    bool disposed, direction;
    bool thinking;
    IntPtr hwnd;
    public CharacterPreset Preset { get; private set; }
    public bool BubbleVisible => bubble.IsVisible;
    public bool ImagesReady => new[] { "idle", "typing-left", "typing-right" }.All(key => !appearance.ImagePaths.ContainsKey(key) || images.Get(key) != null);
    public string CurrentPose => pose;
    public Rect? BubbleRect => bubble.IsVisible ? new Rect(bubble.Left, bubble.Top, bubble.ActualWidth, bubble.ActualHeight) : null;
    public Rect PetBounds
    {
        get
        {
            if (!IsLoaded) return new Rect(Left, Top, Math.Max(1, Width), Math.Max(1, Height));
            var rect = image.Visibility == Visibility.Visible ? image.TransformToAncestor(this).TransformBounds(PetImageLayout.ImageBounds(image)) : new Rect(0, 0, ActualWidth, ActualHeight);
            rect.Offset(Left, Top); return rect;
        }
    }
    public CompanionWindow(Buddy owner, CharacterPreset preset, bool persistPosition = true)
    {
        this.owner = owner; this.persistPosition = persistPosition; Preset = preset; Title = preset.Name; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Native.Software(this);
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Icon = PetIcon.WindowIcon;
        body.Children.Add(back); body.Children.Add(character); body.Children.Add(image); body.Children.Add(front); body.Children.Add(hearts); Content = body; bounce = new(body);
        images = new(Dispatcher, () => Display(pose)); gear = new(Dispatcher, () => { front.InvalidateVisual(); back.InvalidateVisual(); });
        front.CustomImage = gear.Get; back.CustomImage = gear.Get;
        bubble = new PetBubbleWindow(owner, this, () => Preset.Profile.Name);
        body.MouseMove += (_, e) =>
        {
            if (Opacity == 0 || owner.Pref.ClickThrough || e.LeftButton == MouseButtonState.Pressed) return;
            var at = e.GetPosition(this); var rect = PetBounds; rect.Offset(-Left, -Top); if (!rect.Contains(at)) { strokes.Reset(); return; }
            if (owner.Pref.PettingHearts && !owner.IsLight) hearts.Emit(e.GetPosition(body));
            if (owner.Pref.MousePetting && strokes.Move(e.GetPosition(body), Environment.TickCount64)) Pat();
        };
        body.MouseLeave += (_, _) => strokes.Reset();
        settle.Tick += (_, _) => { settle.Stop(); Display(thinking ? "thinking" : Environment.TickCount64 < expressedUntil ? expression : "idle"); };
        MouseLeftButtonDown += (_, e) => { owner.CancelAutomatic(); if (owner.Pref.Locked) return; try { DragMove(); away = false; Clamp(); SavePosition(); bubble.Reposition(); } catch (InvalidOperationException) { } e.Handled = true; };
        MouseWheel += (_, e) => { if (Native.CtrlDown) { e.Handled = true; owner.ResizeBy(e.Delta > 0 ? 10 : -10); } };
        var menu = Pastel.Menu(new ContextMenu());
        void Add(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("설정", owner.OpenSettings);
        Add("두 친구 관리", owner.OpenDuo);
        Add("말풍선 위치 자동으로 되돌리기", () => owner.ResetBubble(true));
        Add("이미지 좌우반전 켜기 / 끄기", () => _ = owner.Presets.SaveFlipAsync(Preset.Id, !Preset.Flip));
        Add("둘째 친구 넣어두기", () => owner.SetCompanion(false));
        ContextMenu = menu;
        Pastel.PlaceBeside(menu);
        menu.Closed += (_, _) => owner.RaisePets();
        SourceInitialized += (_, _) => { hwnd = new WindowInteropHelper(this).Handle; Native.Style(hwnd, owner.Pref.ClickThrough); Native.RaisePet(hwnd); };
        Loaded += (_, _) =>
        {
            Clamp(true); Display("idle"); SavePosition();
            // The typing pictures were left out at start-up so the main pet appears sooner; fetch them once things have settled.
            var warm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            warm.Tick += (_, _) => { warm.Stop(); if (!disposed) { images.Get("typing-left"); images.Get("typing-right"); } };
            warm.Start();
        };
        Closing += (_, _) => SavePosition();
        Closed += (_, _) => { disposed = true; settle.Stop(); owner.Game.Changed -= RefreshGear; bubble.Close(); images.Dispose(); gear.Dispose(); hearts.Dispose(); bounce.Dispose(); };
        owner.Game.Changed += RefreshGear; Configure(preset);
        var placement = owner.Pref.Duo; Left = placement.Left == -9999 ? owner.Left - Width - 28 : placement.Left; Top = placement.Top == -9999 ? owner.Top : placement.Top;
    }
    public void Configure(CharacterPreset preset)
    {
        Preset = preset; appearance = preset.Prepare(owner.Pref, owner.Chat.Preferences).Settings;
        Width = appearance.Size + 80; Height = appearance.Size + 26; body.Margin = new Thickness(10, 18, 10, 8);
        double dpi = PresentationSource.FromVisual(owner)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        images.Configure(appearance, Math.Clamp((int)Math.Ceiling((appearance.Size + 60) * dpi), 100, owner.Pref.LowPower ? 320 : 1024), eager: IsLoaded);
        bounce.Configure(appearance.Desktop.TypingBounce);
        bounce.LowPower = owner.Pref.LowPower;
        bubble.Configure();
        Native.Style(hwnd, owner.Pref.ClickThrough);
        hearts.LowPower = owner.Pref.LowPower;
        owner.Adventure.Friend(preset.Id, preset.Profile.Name);
        if (!owner.Pref.PettingHearts) hearts.Clear(); RefreshGear(); Display("idle"); if (IsLoaded) Clamp();
    }
    void RefreshGear()
    {
        // The second pet has an outfit of its own.
        var outfit = owner.Game.State.For(true); front.Outfit = back.Outfit = outfit;
        gear.Configure(new Preferences { ImagePaths = outfit.Gear.Where(c => outfit.Equipped.Values.Contains(c.Id)).ToDictionary(c => c.Id, c => c.ImagePath) }, appearance.Size + 60);
        front.InvalidateVisual(); back.InvalidateVisual();
    }
    void Display(string next)
    {
        if (disposed) return; pose = ImageAssets.VisibleState(next, appearance.AdvancedImages);
        string slot = owner.IsListening && appearance.ImagePaths.ContainsKey(ImageAssets.MusicKey(pose)) ? ImageAssets.MusicKey(pose) : pose;
        var bitmap = images.Get(slot); if (bitmap == null && slot != pose) { bitmap = images.Get(pose); slot = pose; } if (bitmap == null) { bitmap = images.Get("idle"); slot = "idle"; }
        var idle = images.Get("idle"); var correction = appearance.ImageAdjustments.GetValueOrDefault(slot);
        image.Source = bitmap;
        image.Visibility = bitmap == null ? Visibility.Hidden : Visibility.Visible;
        character.Visibility = bitmap == null ? Visibility.Visible : Visibility.Hidden;
        character.State = pose;
        character.InvalidateVisual();
        PetImageLayout.Apply(image, appearance.Size + 60, appearance.Size, idle, correction, appearance.FlipImages);
        front.AvatarImage = back.AvatarImage = idle; var viewport = PetImageLayout.Viewport(appearance.Size + 60, appearance.Size, idle);
        PetImageLayout.ApplyCorrection(front, viewport, correction, appearance.FlipImages);
        PetImageLayout.ApplyCorrection(back, viewport, correction, appearance.FlipImages);
        PetImageLayout.ApplyCorrection(character, viewport, null, appearance.FlipImages);
        front.InvalidateVisual();
        back.InvalidateVisual();
        if (bubble.IsVisible) bubble.Reposition();
    }
    public void Tap()
    {
        if (disposed || Opacity == 0) return; long now = Environment.TickCount64; typedAt = now; if (!bubble.IsReply) bubble.Hide();
        if (handGate.Accept(now, appearance.Desktop.TypingReactionMs)) Display(hands.Tap());
        if (!owner.IsQuiet && appearance.Desktop.TypingBounce && bounceGate.Accept(now, appearance.Desktop.BodyReactionMs)) { direction = !direction; bounce.Tap(direction, appearance.Desktop.BounceStrength); }
        settle.Stop(); settle.Start();
    }
    public void Tick(long now, bool hidden)
    {
        if (disposed) return; Opacity = hidden ? 0 : 1; Native.Style(hwnd, hidden || owner.Pref.ClickThrough); bubble.Tick(now, hidden);
        if (hidden || owner.IsQuiet) { bounce.Reset(); hearts.Clear(); if (hidden) settle.Stop(); }
        if (now - typedAt >= 180) { string idle = thinking ? "thinking" : now < expressedUntil ? expression : "idle"; if (pose != idle) Display(idle); }
    }
    void Pat()
    {
        owner.Adventure.Pat(Preset.Id, Preset.Profile.Name, Environment.TickCount64);
        bool counted = owner.Game.Pat(Environment.TickCount64, WorkTimeStore.Today); expression = "pat"; expressedUntil = Environment.TickCount64 + appearance.Desktop.PatSeconds * 1000L; Display("pat"); owner.Patted(true, counted);
    }
    public void Speak(string text, bool reply, int seconds) { if (!disposed && Opacity > 0) bubble.ShowText(text, reply, seconds); }
    public void Nudge(string text, int seconds) { expression = "nudge"; expressedUntil = Environment.TickCount64 + seconds * 1000L + 2000; Display("nudge"); Speak(text, false, seconds); }
    public void Say(string text) { if (!disposed && Opacity > 0) bubble.ShowText(text, true, owner.Pref.Desktop.ReplySeconds); }
    public void Express(string mood) { if (mood == "neutral") mood = "chatting"; expression = mood; expressedUntil = Environment.TickCount64 + appearance.Desktop.ExpressionSeconds * 1000L; Display(mood); }
    public void BeginThinking() { thinking = true; Display("thinking"); }
    public void EndThinking() { thinking = false; Display(Environment.TickCount64 < expressedUntil ? expression : "idle"); }
    public void Progress(string text) { if (!disposed && Opacity > 0) bubble.UpdateStreaming(text); }
    public void RefreshMusic() => Display(pose);
    public void PlaceBubble() => bubble.Reposition();
    public void Raise() { Native.RaisePet(hwnd); bubble.Raise(); }
    // While the saved spot is off-screen (monitor unplugged) the companion waits elsewhere without overwriting it.
    bool away;
    public void RestoreLayout()
    {
        if (disposed || !IsLoaded) return; var saved = owner.Pref.Duo;
        if (saved.Left != -9999) { Left = saved.Left; Top = saved.Top; }
        Clamp(); away = saved.Left != -9999 && (Math.Abs(Left - saved.Left) > 1 || Math.Abs(Top - saved.Top) > 1); bubble.Reposition();
    }
    public void ResetPosition() { away = false; Left = owner.Left - Width - 28; Top = owner.Top; Clamp(true); SavePosition(); bubble.Reposition(); }
    public System.Threading.Tasks.Task Ready()
    {
        if (IsLoaded) return System.Threading.Tasks.Task.CompletedTask;
        var done = new System.Threading.Tasks.TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? loaded = null; EventHandler? closed = null;
        loaded = (_, _) => { Loaded -= loaded; Closed -= closed; done.TrySetResult(); };
        closed = (_, _) => { Loaded -= loaded; Closed -= closed; done.TrySetException(new InvalidOperationException("둘째 친구 창이 표시되기 전에 닫혔습니다.")); };
        Loaded += loaded; Closed += closed; return done.Task;
    }
    public void SavePosition() { if (persistPosition && !disposed && !away && double.IsFinite(Left) && double.IsFinite(Top)) { owner.Pref.Duo = owner.Pref.Duo with { Left = Left, Top = Top }; Storage.Save(owner.Pref); } }
    public void Capture(string path) { UpdateLayout(); Pastel.Capture(this, path); }
    void Clamp(bool keepDistinct = false)
    {
        if (!IsLoaded) return;
        var pixel = PointToScreen(new Point(Width / 2, Height / 2));
        var work = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)pixel.X, (int)pixel.Y)).WorkingArea;
        var matrix = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var a = matrix.Transform(new Point(work.Left, work.Top));
        var b = matrix.Transform(new Point(work.Right, work.Bottom));
        Left = Math.Clamp(Left, a.X, Math.Max(a.X, b.X - Width)); Top = Math.Clamp(Top, a.Y, Math.Max(a.Y, b.Y - Height));
        if (keepDistinct)
        {
            var at = CompanionPlacement.Place(new Size(Width, Height), new Rect(owner.Left, owner.Top, owner.Width, owner.Height), new Rect(a, b), new Point(Left, Top)); Left = at.X; Top = at.Y;
        }
    }
}
