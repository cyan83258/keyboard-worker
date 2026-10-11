using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardWorker;

// A character page drawn as a phone's text messages: the character's messages on the left, the user's on the right, newest at
// the bottom. Each line of an answer is a message of its own. Only the recent part of a long thread is built.
public sealed class MessengerView : Grid, IStageSurface
{
    const int Shown = 60;
    readonly StackPanel thread = new() { Margin = new Thickness(14, 10, 14, 14) }; readonly ScrollViewer scroll; readonly TextBlock title = new() { FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock empty = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40) };
    readonly List<UIElement> pending = new(); UIElement? asked; FontFamily font = FontAssets.Standard; double size = 15;
    public bool LowPower { get; set; }
    public FrameworkElement Element => this;
    public bool Scrolls => true;
    public string EmptyNote { set { } }
    public MessengerView()
    {
        Background = Pastel.CardBlue; ClipToBounds = true; MinHeight = 260; empty.Foreground = Pastel.Ink; empty.Opacity = .8; empty.Text = "아직 주고받은 문자가 없어요.\n아래에 첫 메시지를 보내 보세요.";
        // A phone-shaped column in the middle of the page.
        var phone = new DockPanel { MaxWidth = 560 };
        var bar = new Border { Child = title, Background = Pastel.Hot, Padding = new Thickness(12, 7, 12, 8), CornerRadius = new CornerRadius(14, 14, 0, 0) }; DockPanel.SetDock(bar, Dock.Top); phone.Children.Add(bar);
        scroll = new ScrollViewer { Content = thread, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; phone.Children.Add(scroll);
        Children.Add(new Border { Child = phone, Background = Pastel.Paper, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 3, 3), CornerRadius = new CornerRadius(16), Margin = new Thickness(14), MaxWidth = 566 });
        Children.Add(empty);
    }
    public void SetFont(FontFamily family, double fontSize) { font = family; size = Math.Clamp(fontSize - 1, FontAssets.MinSize, 30); }
    public void Show(string background, string standing) { }
    public void Forget() { }

    Border Bubble(string text, bool mine) => new()
    {
        Child = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Pastel.Ink, FontFamily = font, FontSize = size, Padding = new Thickness(0) },
        Background = mine ? Pastel.Pink : Brushes.White, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.2, 1.2, 2, 2.4), CornerRadius = mine ? new CornerRadius(14, 14, 3, 14) : new CornerRadius(14, 14, 14, 3),
        Padding = new Thickness(11, 6, 11, 7), Margin = new Thickness(mine ? 70 : 0, 3, mine ? 0 : 70, 3), HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left
    };
    static TextBlock Note(string text) => new() { Text = text, Foreground = Pastel.Ink, Opacity = .75, FontSize = 11.5, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(30, 5, 30, 5) };
    // An answer as messages: every line is one; a stage direction becomes a small note between them.
    IEnumerable<UIElement> Messages(string answer)
    {
        foreach (var beat in StageScript.Parse(answer))
        {
            if (beat.Narration.Length > 0) yield return Note(beat.Narration);
            foreach (string message in beat.Line.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) yield return Bubble(message, false);
        }
    }
    void Bottom() { empty.Visibility = thread.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed; scroll.UpdateLayout(); scroll.ScrollToEnd(); }
    public void Load(string name, IReadOnlyList<ChatTurn> turns)
    {
        title.Text = name; thread.Children.Clear(); pending.Clear(); asked = null;
        if (turns.Count > Shown) thread.Children.Add(Note("이전 메시지 " + (turns.Count - Shown) + "개는 접혀 있어요."));
        string day = "";
        foreach (var turn in turns.Skip(Math.Max(0, turns.Count - Shown)))
        {
            string date = turn.At.ToLocalTime().ToString("yyyy년 M월 d일"); if (date != day) { day = date; thread.Children.Add(Note(date)); }
            thread.Children.Add(Bubble(turn.User, true)); foreach (var message in Messages(turn.Assistant)) thread.Children.Add(message);
        }
        Bottom();
    }
    void Replace(IEnumerable<UIElement> messages) { foreach (var old in pending) thread.Children.Remove(old); pending.Clear(); foreach (var message in messages) { pending.Add(message); thread.Children.Add(message); } Bottom(); }
    public void Asked(string name, string message) { title.Text = name; asked = Bubble(message, true); thread.Children.Add(asked); pending.Clear(); Replace(new[] { Bubble("…", false) }); }
    public void Streaming(string name, string partial) => Replace(Messages(partial));
    public void Present(string name, string text, bool typed) { title.Text = name; Replace(Messages(text)); pending.Clear(); asked = null; }
    public void Failed() { Replace(Array.Empty<UIElement>()); if (asked != null) thread.Children.Remove(asked); asked = null; Bottom(); }
}
