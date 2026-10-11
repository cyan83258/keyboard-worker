using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KeyboardWorker;

public sealed class ReactionPreviewWindow : Window
{
    readonly Func<DesktopOptions> options;
    readonly Preferences appearance;
    readonly Character character = new();
    readonly Image image = new() { Stretch = Stretch.Uniform };
    readonly AccessoryLayer front = new(), back = new() { BackLayer = true };
    readonly LazyImageCache images, gear;
    readonly TypingBounce bounce;
    readonly HandReaction hands = new();
    readonly ReactionLimiter handPace = new(), bodyPace = new();
    readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(180) }, demo = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly TextBlock status = Pastel.Text("");
    string pose = "idle";
    bool left, closed;
    int keys, handMoves, bodyMoves;
    long demoUntil;
    public ReactionPreviewWindow(Buddy buddy, Func<DesktopOptions> options)
    {
        this.options = options; appearance = buddy.Pref.Snapshot(); var p = new StackPanel { Margin = new Thickness(22) };
        p.Children.Add(Pastel.Text("저장 전에 직접 톡톡 ♡", 22)); p.Children.Add(Pastel.Text("이 창에 타이핑하면 아직 저장하지 않은 간격과 강도로 반응합니다. 소리·작업 시간·경험치 없이 화면만 테스트해요.", 13));
        var body = new Grid { Width = 240, Height = 180, Margin = new Thickness(14, 20, 14, 10) };
        body.Children.Add(back);
        body.Children.Add(character);
        body.Children.Add(image);
        body.Children.Add(front);
        bounce = new(body);
        p.Children.Add(Pastel.Card(body, true)); p.Children.Add(status);
        var input = new TextBox { Height = 65, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 1000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        p.Children.Add(input);
        var actions = new WrapPanel(); actions.Children.Add(Pastel.Button("빠른 타이핑 예시", () => { demoUntil = Environment.TickCount64 + 3000; demo.Start(); }, true));
        actions.Children.Add(Pastel.Button("멈추기", () => { demo.Stop(); settle.Stop(); bounce.Reset(); Display("idle"); })); p.Children.Add(actions);
        p.Children.Add(Pastel.Text("예시는 50ms마다 입력합니다. 설정 창 값을 바꾸면 다음 반응부터 반영합니다. Esc로 닫습니다.", 12));
        Pastel.Window(this, "타이핑 반응 미리보기", Pastel.Scroll(p), 560, 650);
        images = new(Dispatcher, () => Display(pose)); gear = new(Dispatcher, () => { front.InvalidateVisual(); back.InvalidateVisual(); });
        images.Configure(buddy.Pref.Snapshot(), 400); var outfit = buddy.Game.State.Snapshot(); front.Outfit = outfit; back.Outfit = outfit;
        gear.Configure(new Preferences { ImagePaths = outfit.CustomItems.Where(c => outfit.Equipped.Values.Contains(c.Id)).ToDictionary(c => c.Id, c => c.ImagePath) }, 400);
        front.CustomImage = gear.Get;
        back.CustomImage = gear.Get;
        settle.Tick += (_, _) => { settle.Stop(); Display("idle"); };
        demo.Tick += (_, _) => { if (Environment.TickCount64 >= demoUntil) demo.Stop(); else Tap(); };
        input.PreviewKeyDown += (_, e) => { if (!e.IsRepeat && e.Key != Key.Escape) Tap(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Loaded += (_, _) => { Display("idle"); input.Focus(); };
        Closed += (_, _) => { closed = true; demo.Stop(); settle.Stop(); bounce.Dispose(); images.Dispose(); gear.Dispose(); };
    }
    void Tap()
    {
        var d = options(); keys++; long now = Environment.TickCount64; bounce.Configure(d.TypingBounce);
        if (handPace.Accept(now, d.TypingReactionMs)) { Display(hands.Tap()); handMoves++; }
        if (d.TypingBounce && bodyPace.Accept(now, d.BodyReactionMs)) { left = !left; bounce.Tap(left, d.BounceStrength); bodyMoves++; }
        settle.Stop(); settle.Start(); status.Text = $"입력 {keys}회 · 손 {handMoves}회 · 몸 {bodyMoves}회\n손 {d.TypingReactionMs}ms / 몸 {d.BodyReactionMs}ms / 강도 {d.BounceStrength}";
    }
    void Display(string state)
    {
        if (closed) return; pose = state; var bitmap = images.Get(state); string sourceSlot = state;
        if (bitmap == null) { bitmap = images.Get("idle"); sourceSlot = "idle"; }
        appearance.ImageAdjustments.TryGetValue(sourceSlot, out var correction);
        var reference = images.Get("idle"); PetImageLayout.Apply(image, 240, 180, reference, correction);
        PetImageLayout.ApplyCorrection(front, PetImageLayout.Viewport(240, 180, reference), correction); PetImageLayout.ApplyCorrection(back, PetImageLayout.Viewport(240, 180, reference), correction);
        image.Source = bitmap;
        image.Visibility = bitmap == null ? Visibility.Hidden : Visibility.Visible;
        character.Visibility = bitmap == null ? Visibility.Visible : Visibility.Hidden;
        character.State = state;
        character.InvalidateVisual();
        front.AvatarImage = reference;
        back.AvatarImage = reference;
        front.InvalidateVisual();
        back.InvalidateVisual();
    }
}
