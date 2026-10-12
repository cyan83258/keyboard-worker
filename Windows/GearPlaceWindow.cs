using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

// Placing what the character wears by hand: drag an accessory to move it, turn the wheel over it to make it larger or
// smaller (with Shift, to turn it). The character is shown large with everything it has on, drawn by the same layers
// as on the desktop. Nothing changes until "저장"; the places are kept per accessory (GameState.Places / CustomGear).
public sealed class GearPlaceWindow : Window
{
    const double Side = 440;
    readonly PetGame game; readonly GameState draft; readonly AccessoryLayer back = new() { BackLayer = true }, front = new();
    readonly LazyImageCache pictures; readonly TextBlock status = Pastel.Text("", 12), chosenName = Pastel.Text("", 13);
    string chosen = ""; Point last; bool dragging, changed;
    public GearPlaceWindow(Buddy buddy)
    {
        game = buddy.Game; draft = game.State.Snapshot(); draft.ShowAccessories = true;
        var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(Pastel.Text("악세서리 위치 편집", 20));
        top.Children.Add(new TextBlock { Text = "끌기: 위치   ·   휠: 크기   ·   Shift + 휠: 회전", Foreground = Pastel.Ink, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) });
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(chosenName); footer.Children.Add(status);
        var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("저장", () => { game.Place(draft); changed = false; Close(); }));
        buttons.Children.Add(Pastel.Button("고른 것 처음 위치로", Reset, true));
        var copy = Pastel.Button("배치 값 복사", Copy, true); copy.ToolTip = "고른 악세서리의 위치·크기를 프로그램에 함께 넣는 목록(accessories.json)의 한 줄로 복사해요"; buttons.Children.Add(copy);
        buttons.Children.Add(Pastel.Button("취소", () => { changed = false; Close(); }, true)); footer.Children.Add(buttons);

        // The character as it stands on the desktop, with the two layers of what it wears behind and in front of it.
        var portrait = new Image { Stretch = Stretch.Uniform }; BitmapSource? picture = null;
        if (buddy.Pref.ImagePaths.TryGetValue("idle", out var file) && File.Exists(file))
            try { picture = ImageAssets.Read(file, 900); } catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException or FileFormatException) { }
        portrait.Source = picture; back.AvatarImage = front.AvatarImage = picture; back.Outfit = front.Outfit = draft;
        pictures = new LazyImageCache(Dispatcher, Redraw); back.CustomImage = front.CustomImage = pictures.Get;
        pictures.Configure(new Preferences { ImagePaths = draft.Gear.Where(g => draft.Equipped.Values.Contains(g.Id)).ToDictionary(g => g.Id, g => g.ImagePath) }, 700);
        var stage = new Grid { Width = Side, Height = Side, Background = Pastel.Checker(), Cursor = Cursors.SizeAll, ClipToBounds = true };
        stage.Children.Add(back); stage.Children.Add(portrait); stage.Children.Add(front);
        if (picture == null) stage.Children.Add(new TextBlock { Text = "캐릭터 이미지를 등록하면 여기에 함께 보여요.", Foreground = Pastel.Ink, Opacity = .7, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8), IsHitTestVisible = false });
        root.Children.Add(new Border { Child = stage, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });

        stage.MouseLeftButtonDown += (_, e) => { Choose(Under(e.GetPosition(front))); if (chosen.Length == 0) return; dragging = true; last = e.GetPosition(front); stage.CaptureMouse(); };
        stage.MouseLeftButtonUp += (_, _) => { dragging = false; stage.ReleaseMouseCapture(); };
        stage.MouseMove += (_, e) =>
        {
            if (!dragging || chosen.Length == 0) return; var now = e.GetPosition(front); var body = front.Body(); if (body.Width < 1 || body.Height < 1) return;
            double dx = (now.X - last.X) / body.Width * 100, dy = (now.Y - last.Y) / body.Height * 100; last = now;
            Change(g => g with { OffsetX = g.OffsetX + dx, OffsetY = g.OffsetY + dy });
        };
        stage.MouseWheel += (_, e) =>
        {
            if (chosen.Length == 0) Choose(Under(e.GetPosition(front))); if (chosen.Length == 0) return; double steps = e.Delta / 120.0; e.Handled = true;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) Change(g => g with { Rotation = Math.IEEERemainder(g.Rotation + steps * 3, 360) }); else Change(g => g with { WidthPercent = g.WidthPercent * Math.Pow(1.06, steps) });
        };
        Closing += (_, e) => { if (changed && MessageBox.Show(this, "바꾼 위치를 저장하지 않고 닫을까요?", "악세서리 위치 편집", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) e.Cancel = true; };
        Closed += (_, _) => pictures.Dispose();
        Pastel.Window(this, "악세서리 위치 편집", root, 560, 760); MinWidth = 520; MinHeight = 700;
        Choose(draft.Equipped.Values.LastOrDefault(id => draft.Wearing(id) != null) ?? "");
        if (draft.Equipped.Count == 0) status.Text = "입고 있는 악세서리가 없어요. 옷장에서 먼저 장착해 주세요.";
    }
    void Redraw() { back.InvalidateVisual(); front.InvalidateVisual(); }
    // What is in front is taken first.
    string Under(Point at) => front.Hit(at) ?? back.Hit(at) ?? "";
    void Choose(string id)
    {
        chosen = id; back.Marked = front.Marked = id; var gear = id.Length > 0 ? draft.Wearing(id) : null;
        chosenName.Text = gear == null ? "악세서리를 눌러 골라 주세요." : $"고른 것: {gear.Name}  ·  크기 {gear.WidthPercent:0.#}%  ·  위치 {gear.OffsetX:0.#}, {gear.OffsetY:0.#}  ·  회전 {gear.Rotation:0.#}°"; Redraw();
    }
    // The chosen accessory with something about it changed: the user's own gear is changed itself, a shipped one gets a place of its own.
    void Change(Func<CustomGear, CustomGear> change)
    {
        var gear = draft.Wearing(chosen); if (gear == null) return; var next = change(gear).Normalized(); int own = draft.CustomItems.FindIndex(c => c.Id == chosen);
        if (own >= 0) draft.CustomItems[own] = next; else draft.Places[chosen] = GearPlace.Of(next);
        changed = true; status.Text = ""; Choose(chosen);
    }
    void Reset()
    {
        if (chosen.Length == 0) { status.Text = "먼저 악세서리를 골라 주세요."; return; }
        int own = draft.CustomItems.FindIndex(c => c.Id == chosen);
        if (own >= 0) { var saved = game.State.CustomItems.Find(c => c.Id == chosen); if (saved != null) draft.CustomItems[own] = saved; } else draft.Places.Remove(chosen);
        changed = true; Choose(chosen); status.Text = own >= 0 ? "저장돼 있던 위치로 되돌렸어요." : "처음 위치로 되돌렸어요.";
    }
    void Copy()
    {
        var gear = draft.Wearing(chosen); if (gear == null) { status.Text = "먼저 악세서리를 골라 주세요."; return; }
        try { Clipboard.SetText(ShopGear.Describe(gear, Path.GetFileName(gear.ImagePath))); status.Text = "복사했어요. accessories.json 에 붙여 넣을 수 있어요."; }
        catch (System.Runtime.InteropServices.ExternalException) { status.Text = "지금은 복사할 수 없어요. 다시 눌러 주세요."; }
    }
}
