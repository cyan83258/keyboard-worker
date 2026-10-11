using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KeyboardWorker;

// The stage of a character page, laid out like a visual novel: a background, the character's standing picture in front of it,
// and the line being said in a box along the bottom. What the character does or thinks (the bracketed parts of an answer)
// is shown apart from the speech, in a small note above the box. A long answer comes a page at a time; a click turns the page.
public sealed class StageView : Grid, IStageSurface
{
    readonly Image backdrop = new() { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Image figure = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
    // The other character of a scene for two; unused on a page for one.
    readonly Image partner = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
    readonly TextBlock speaker = new() { FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Brushes.White };
    readonly TextBlock line = new() { TextWrapping = TextWrapping.Wrap, FontSize = 16, LineHeight = 25 };
    readonly TextBlock aside = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, FontStyle = FontStyles.Italic, TextAlignment = TextAlignment.Right };
    readonly TextBlock more = new() { FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 4, 0, -6) };
    readonly TextBlock empty = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 40, 150) };
    readonly ScrollViewer scroll; readonly Border asideBox;
    readonly DispatcherTimer typing = new() { Interval = TimeSpan.FromMilliseconds(28) };
    readonly StagePictures pictures = new();
    List<StageBeat> pages = new(); int page;
    string full = "", backdropPath = "", figurePath = "", partnerPath = ""; int shown; bool together, partnerSpeaks;
    // Raised by a click when the last page of the line is already showing: the page may bring the next line.
    public event Action? Ended;
    // More lines follow this one, so the box shows that a click goes on.
    public bool Continues { get; set; }
    public bool LowPower { get; set; }
    public FrameworkElement Element => this;
    public bool Scrolls => false;
    public StageView()
    {
        ClipToBounds = true; MinHeight = 260;
        Color top = ((SolidColorBrush)Pastel.CardBlue).Color, bottom = ((SolidColorBrush)Pastel.CardPink).Color;
        var plain = new LinearGradientBrush(top, bottom, 90); plain.Freeze(); Background = plain;
        line.Foreground = Pastel.Ink; aside.Foreground = Pastel.Ink; more.Foreground = Pastel.Hot; empty.Foreground = Pastel.Ink; empty.Opacity = .8;
        var paper = ((SolidColorBrush)Pastel.Paper).Color; var glass = new SolidColorBrush(Color.FromArgb(238, paper.R, paper.G, paper.B)); glass.Freeze();
        var plate = new Border { Child = speaker, Background = Pastel.Hot, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 1.5, 0), CornerRadius = new CornerRadius(10, 10, 0, 0), Padding = new Thickness(14, 3, 14, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 0, -1.5) };
        // What the character does or thinks, to the upper right of the box; gone when there is nothing to tell.
        asideBox = new Border { Child = aside, Background = glass, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(10), Padding = new Thickness(11, 5, 11, 6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(150, 0, 6, 6), Visibility = Visibility.Collapsed };
        var head = new Grid(); head.Children.Add(asideBox); head.Children.Add(plate);
        scroll = new ScrollViewer { Content = line, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 132 };
        var inside = new DockPanel(); DockPanel.SetDock(more, Dock.Bottom); inside.Children.Add(more); inside.Children.Add(scroll);
        var box = new Border { Child = inside, Background = glass, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 3, 3.5), CornerRadius = new CornerRadius(0, 14, 14, 14), Padding = new Thickness(18, 12, 16, 14), MinHeight = 96, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "누르면 다음으로 넘어가요" };
        var speech = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 14, 14) }; speech.Children.Add(head); speech.Children.Add(box);
        Children.Add(backdrop); Children.Add(partner); Children.Add(figure); Children.Add(empty); Children.Add(speech);
        box.MouseLeftButtonDown += (_, _) => Advance();
        typing.Tick += (_, _) => { shown = Math.Min(full.Length, shown + 2); line.Text = full[..shown]; scroll.ScrollToEnd(); if (shown >= full.Length) typing.Stop(); };
        SizeChanged += (_, _) => Draw();
        Unloaded += (_, _) => { typing.Stop(); pictures.Clear(); };
    }
    public string EmptyNote { get => empty.Text; set => empty.Text = value; }
    public void SetFont(FontFamily family, double size)
    {
        if (!ReferenceEquals(line.FontFamily, family)) { line.FontFamily = family; speaker.FontFamily = family; aside.FontFamily = family; }
        if (Math.Abs(line.FontSize - size) > .1) { line.FontSize = size; line.LineHeight = Math.Round(size * 1.55); scroll.MaxHeight = Math.Max(132, size * 1.55 * 5.2); aside.FontSize = Math.Max(11, size * .78); }
    }

    // ---- pictures ----
    public void Show(string background, string standing)
    {
        bool changed = !string.Equals(figurePath, standing, StringComparison.OrdinalIgnoreCase); backdropPath = background ?? ""; figurePath = standing ?? ""; partnerPath = ""; together = false; Draw();
        if (changed && figure.Source != null && !LowPower) figure.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(160)));
    }
    // A scene for two. both: the two stand side by side (the one who is not speaking a little dimmed); otherwise only the
    // speaker is shown, in the middle.
    public void ShowTwo(string background, string first, string second, bool both, bool secondSpeaks)
    {
        backdropPath = background ?? ""; figurePath = first ?? ""; partnerPath = second ?? ""; together = both; partnerSpeaks = secondSpeaks; Draw();
    }
    // Pictures are decoded for the size of the stage in screen pixels, so a small window does not hold large bitmaps.
    void Draw()
    {
        if (ActualHeight < 1) return; double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        figure.Height = partner.Height = ActualHeight * .96;
        bool pair = partnerPath.Length > 0 || together, soloSecond = pair && !together && partnerSpeaks;
        // Alone: in the middle. Two: left and right halves. A scene for two that shows one: the speaker in the middle.
        figure.HorizontalAlignment = pair && together ? HorizontalAlignment.Left : HorizontalAlignment.Center; figure.MaxWidth = partner.MaxWidth = pair && together ? ActualWidth * .56 : ActualWidth;
        partner.HorizontalAlignment = together ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        figure.Visibility = soloSecond ? Visibility.Collapsed : Visibility.Visible; partner.Visibility = pair && (together || partnerSpeaks) ? Visibility.Visible : Visibility.Collapsed;
        figure.Opacity = pair && together && partnerSpeaks ? .62 : 1; partner.Opacity = together && !partnerSpeaks ? .62 : 1;
        partner.Source = partner.Visibility == Visibility.Visible ? pictures.Get(partnerPath, (int)(ActualHeight * scale)) : null;
        figure.Source = figure.Visibility == Visibility.Visible ? pictures.Get(figurePath, (int)(ActualHeight * scale)) : null; backdrop.Source = pictures.Get(backdropPath, (int)(Math.Max(ActualWidth, ActualHeight) * scale));
        backdrop.Width = ActualWidth; backdrop.Height = ActualHeight;
        empty.Visibility = figure.Source == null && partner.Source == null ? Visibility.Visible : Visibility.Collapsed;
    }
    public void Forget() { pictures.Clear(); Draw(); }

    // ---- the line ----
    // About two lines of the box, in characters (a Korean letter is roughly as wide as the font is tall).
    int Budget() => Math.Max(24, (int)(Math.Max(240, ActualWidth - 66) / (line.FontSize * .98)) * 2);
    public void Load(string name, IReadOnlyList<ChatTurn> thread) { }
    public void Asked(string name, string message) { pages.Clear(); Put(name, "", "…", false); }
    public void Failed() { }
    // While the answer arrives it is shown whole and scrolls; the pages are made when it is complete.
    public void Streaming(string name, string partial)
    {
        var beats = StageScript.Parse(partial); pages.Clear();
        Put(name, beats.LastOrDefault(b => b.Narration.Length > 0)?.Narration ?? "", string.Join(" ", beats.Where(b => b.Line.Length > 0).Select(b => b.Line)), false);
    }
    public void Present(string name, string text, bool typed)
    {
        speaker.Text = name; pages = StageScript.Pages(StageScript.Parse(text), Budget()); page = 0;
        if (pages.Count == 0) pages.Add(new("", "……"));
        Turn(typed);
    }
    void Turn(bool typed) { var now = pages[page]; Put(speaker.Text, now.Narration, now.Line, typed); more.Text = page < pages.Count - 1 ? $"▼ {page + 1}/{pages.Count}" : Continues ? "▼" : pages.Count > 1 ? $"{page + 1}/{pages.Count}" : ""; }
    void Put(string name, string narration, string text, bool typed)
    {
        speaker.Text = name; aside.Text = narration; asideBox.Visibility = narration.Length > 0 ? Visibility.Visible : Visibility.Collapsed; more.Text = "";
        full = text; typing.Stop();
        if (typed && !LowPower && text.Length > 0) { shown = 0; line.Text = ""; typing.Start(); } else { shown = text.Length; line.Text = text; scroll.ScrollToEnd(); }
    }
    // A click: first the rest of the page at once, then the next page.
    void Advance()
    {
        if (typing.IsEnabled) { typing.Stop(); shown = full.Length; line.Text = full; scroll.ScrollToEnd(); return; }
        if (page < pages.Count - 1) { page++; Turn(true); } else Ended?.Invoke();
    }
}
