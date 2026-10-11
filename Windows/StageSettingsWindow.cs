using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

// Everything about the character pages in one window, opened with "페이지 설정": how the characters talk there
// (StageTalkPanel), this character's standing pictures (SpriteList) and the backgrounds all pages share (BackdropList).
// A tab is built the first time it is shown, so opening the window does not wait for pictures nobody is looking at.
public sealed class StageSettingsWindow : Window
{
    readonly TabControl tabs = new() { Background = Pastel.Paper, BorderBrush = Pastel.Pink };
    public void ShowPage(int index) => tabs.SelectedIndex = index;
    public int Pages => tabs.Items.Count;
    public StageSettingsWindow(Buddy buddy, Buddy.StageCharacter character)
    {
        var root = new DockPanel { Margin = new Thickness(20) }; var status = Pastel.Text("", 12);
        var footer = new StackPanel(); footer.Children.Add(status); footer.Children.Add(Pastel.Button("닫기", Close, true)); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(tabs);
        void Tab(string title, Func<UIElement> content) => tabs.Items.Add(new TabItem { Header = title, Tag = content, Padding = new Thickness(14, 8, 14, 8) });
        void Build() { if (tabs.SelectedItem is TabItem { Tag: Func<UIElement> make } tab) { tab.Tag = null; var p = new StackPanel { Margin = new Thickness(18) }; p.Children.Add(make()); tab.Content = Pastel.Scroll(p); } }
        Tab("대화 · 프롬프트", () => new StageTalkPanel(buddy, character, this, text => status.Text = text));
        Tab("스탠딩 그림", () => new SpriteList(buddy, character, this, text => status.Text = text));
        Tab("배경", () => new BackdropList(buddy, this, text => status.Text = text));
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, tabs)) Build(); }; tabs.SelectedIndex = 0; Build();
        Pastel.Window(this, "페이지 설정", root, 820, 840);
    }
}

// What the lists of pictures share: a heading that folds its content, small buttons, thumbnails.
static class StageParts
{
    // A heading with ▼ / ▶ that shows or hides the content under it. The title is kept on the heading itself (Tag),
    // so it survives folding and can be changed later with Retitle.
    public static Button Fold(string title, FrameworkElement content, bool folded, Action<bool> changed)
    {
        var button = new Button { Tag = title, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(4, 4, 8, 4), Margin = new Thickness(0, 16, 0, 2), FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Pastel.Hot, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "접기 / 펼치기" };
        Pastel.Ghost(button);
        void Show() { button.Content = Heading(folded, (string)button.Tag); content.Visibility = folded ? Visibility.Collapsed : Visibility.Visible; }
        button.Click += (_, _) => { folded = !folded; Show(); changed(folded); }; Show(); return button;
    }
    public static string Heading(bool folded, string title) => (folded ? "▶ " : "▼ ") + title;
    public static void Retitle(Button heading, string title) { bool folded = (heading.Content as string ?? "").StartsWith("▶"); heading.Tag = title; heading.Content = Heading(folded, title); }
    public static Button Small(string text, Action action, bool blue = true) { var button = Pastel.Button(text, action, blue); button.Padding = new Thickness(8, 2, 8, 2); button.FontSize = 11; button.Margin = new Thickness(0, 4, 4, 0); return button; }
    // The buttons along the top of a page: all the same size, whichever page they are on.
    public static Button Tool(string text, string tip, Action action) { var button = Pastel.Button(text, action, true); button.Padding = new Thickness(12, 5, 12, 5); button.Margin = new Thickness(6, 0, 0, 0); button.FontSize = 12; button.MinWidth = 0; button.VerticalAlignment = VerticalAlignment.Center; button.ToolTip = tip; return button; }
    public static TextBox Field(string text, string tip) => new() { Text = text, MaxLength = 20, Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(6, 3, 6, 3), FontSize = 12, ToolTip = tip };
    // checker: a chequered ground that shows where a standing picture is transparent.
    // The picture is decoded away from the window (two at a time) and kept, so a list of many pictures opens at once and
    // is drawn again without reading a file. The stored files never change, so the path and size are the whole key.
    public static Border Thumbnail(string path, bool checker, double width = 132, double height = 132)
    {
        var image = new Image { Stretch = checker ? Stretch.Uniform : Stretch.UniformToFill };
        var frame = new Border { Width = width, Height = height, Child = image, Background = checker ? Pastel.Checker() : Brushes.White, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true };
        int side = (int)Math.Max(width, height) * 2; string key = side + "|" + path;
        if (thumbnails.TryGetValue(key, out var known)) image.Source = known; else _ = Decode(frame, image, path, side, key);
        return frame;
    }
    static readonly Dictionary<string, BitmapSource> thumbnails = new(StringComparer.OrdinalIgnoreCase); static readonly SemaphoreSlim decoding = new(2);
    static async Task Decode(Border frame, Image image, string path, int side, string key)
    {
        await decoding.WaitAsync();
        try
        {
            if (!thumbnails.TryGetValue(key, out var picture)) { picture = await Task.Run(() => ImageAssets.Read(path, side)); if (thumbnails.Count > 400) thumbnails.Clear(); thumbnails[key] = picture; }
            image.Source = picture;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException or FileFormatException) { frame.Child = new TextBlock { Text = "파일 없음", Foreground = Pastel.Ink, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; }
        finally { decoding.Release(); }
    }
}
