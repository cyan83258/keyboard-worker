using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KeyboardWorker;

// A story read like a book, one part to a page: "#3" above it, the direction it was written to (small and pale), what the
// model thought folded away when that is kept, the text, and small icons to edit, delete or (on the last page) write it
// again. A small bar at the bottom turns the pages: arrows, and a box to type the number of a part to go to.
public sealed class NovelView : Grid
{
    readonly StackPanel column = new() { Margin = new Thickness(36, 18, 36, 24) }; readonly ScrollViewer scroll;
    readonly TextBlock empty = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40) };
    readonly TextBox number = new() { Width = 40, FontSize = 11, Padding = new Thickness(2, 1, 2, 1), Margin = new Thickness(4, 0, 4, 0), TextAlignment = TextAlignment.Center, MaxLength = 5, VerticalAlignment = VerticalAlignment.Center, ToolTip = "번호를 적고 Enter로 그 부분으로 가요" };
    readonly TextBlock total = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }; readonly Button back, next; readonly StackPanel bar;
    FontFamily font = FontAssets.Standard; double size = 16; int page = -1; IReadOnlyList<ChatTurn> turns = Array.Empty<ChatTurn>();
    TextBox? live; TextBlock? liveThought;
    public event Action<ChatTurn>? Edit, Delete;
    public event Action? Again;
    // What the model thought for a part ("" = nothing kept); shown only when the window says so.
    public Func<ChatTurn, string> Thought { get; set; } = _ => "";
    public bool ShowThinking { get; set; }
    public bool Locked { get; set; }
    public NovelView()
    {
        Background = Pastel.Paper; ClipToBounds = true; empty.Foreground = total.Foreground = Pastel.Ink; empty.Opacity = .75; total.Opacity = .7;
        empty.Text = "아직 쓴 이야기가 없어요.\n아래에 첫 장면의 방향을 적거나, 비워 두고 '쓰기'를 눌러 보세요.";
        RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        scroll = new ScrollViewer { Content = new Border { Child = column, MaxWidth = 820 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Children.Add(scroll); Children.Add(empty);
        Button Arrow(string glyph, string tip, int step) { var button = new Button { Content = glyph, FontSize = 11, Padding = new Thickness(7, 1, 7, 2), Foreground = Pastel.Ink, Cursor = Cursors.Hand, ToolTip = tip }; Pastel.Ghost(button); button.Click += (_, _) => Go(page + step); return button; }
        back = Arrow("◀", "앞 부분", -1); next = Arrow("▶", "다음 부분", 1);
        bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 6), Visibility = Visibility.Collapsed };
        bar.Children.Add(back); bar.Children.Add(number); bar.Children.Add(total); bar.Children.Add(next); SetRow(bar, 1); Children.Add(bar);
        number.KeyDown += (_, e) => { if (e.Key == Key.Enter && int.TryParse(number.Text.Trim().TrimStart('#'), out int wanted)) { e.Handled = true; Go(wanted - 1); } };
    }
    public void SetFont(FontFamily family, double fontSize) { font = family; size = Math.Clamp(fontSize, FontAssets.MinSize, 30); }

    TextBox Passage(string text)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Pastel.Ink, FontFamily = font, FontSize = size, Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 4) };
        TextBlock.SetLineHeight(box, Math.Round(size * 1.75)); TextBlock.SetLineStackingStrategy(box, LineStackingStrategy.BlockLineHeight); return box;
    }
    static TextBlock Heading(string text) => new() { Text = text, Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 0, 0, 2) };
    static TextBlock Direction(string text) => new() { Text = text.Trim().Length > 0 ? "✎  " + text.Trim() : "· · ·", Foreground = Pastel.Ink, Opacity = .55, FontSize = 12, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
    // What the model thought, folded under a small "추론" button.
    (UIElement Head, Border Box, TextBlock Text) Thoughts(string text, bool open = false)
    {
        var words = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Pastel.Ink, Opacity = .75, FontSize = 12, FontStyle = FontStyles.Italic, LineHeight = 19 };
        var box = new Border { Child = words, BorderBrush = Pastel.Line, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 2, 0, 4), Margin = new Thickness(0, 0, 0, 6), Visibility = open ? Visibility.Visible : Visibility.Collapsed };
        var toggle = new Button { Content = open ? "추론 ▾" : "추론 ▸", FontSize = 11, Padding = new Thickness(6, 1, 6, 2), Foreground = Pastel.Ink, Opacity = .7, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand, ToolTip = "AI가 쓰기 전에 생각한 내용 펼치기 / 접기" };
        Pastel.Ghost(toggle); toggle.Click += (_, _) => { bool show = box.Visibility != Visibility.Visible; box.Visibility = show ? Visibility.Visible : Visibility.Collapsed; toggle.Content = show ? "추론 ▾" : "추론 ▸"; };
        return (toggle, box, words);
    }
    StackPanel Actions(ChatTurn turn, bool last)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Opacity = .55 };
        row.Children.Add(new TextBlock { Text = $"{turn.At.ToLocalTime():M월 d일 HH:mm} · {turn.Assistant.Length:N0}자", Foreground = Pastel.Ink, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        void Icon(string glyph, string tip, Action action) { var button = new Button { Content = glyph, FontSize = 13, Padding = new Thickness(5, 0, 5, 1), Foreground = Pastel.Ink, Cursor = Cursors.Hand, ToolTip = tip }; Pastel.Ghost(button); button.Click += (_, _) => { if (!Locked) action(); }; row.Children.Add(button); }
        Icon("✎", "이 부분과 그 지시를 고쳐요", () => Edit?.Invoke(turn)); Icon("✕", "이 부분을 지워요", () => Delete?.Invoke(turn));
        if (last) Icon("↻", "이 부분이 마음에 들지 않으면 지우고 같은 지시로 다시 써요", () => Again?.Invoke());
        row.MouseEnter += (_, _) => row.Opacity = 1; row.MouseLeave += (_, _) => row.Opacity = .55;
        return row;
    }
    // The bar under the page, as the pages stand.
    void Bar(int shown, int count) { bar.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed; number.Text = (shown + 1).ToString(); total.Text = "/ " + count; back.IsEnabled = shown > 0; next.IsEnabled = shown < count - 1; }
    // Opens a page (the number of a part, from 0) - not while a part is being written.
    public void Go(int index) { if (Locked) { Bar(turns.Count, turns.Count + 1); back.IsEnabled = next.IsEnabled = false; return; } Open(index); }
    void Open(int index)
    {
        if (turns.Count == 0) { Bar(0, 0); return; }
        page = Math.Clamp(index, 0, turns.Count - 1); var turn = turns[page]; column.Children.Clear(); live = null; liveThought = null;
        column.Children.Add(Heading("#" + (page + 1))); column.Children.Add(Direction(turn.User));
        if (ShowThinking && Thought(turn) is { Length: > 0 } thought) { var (head, box, _) = Thoughts(thought); column.Children.Add(head); column.Children.Add(box); }
        column.Children.Add(Passage(turn.Assistant)); column.Children.Add(Actions(turn, page == turns.Count - 1));
        empty.Visibility = Visibility.Collapsed; Bar(page, turns.Count); scroll.ScrollToTop();
    }
    // The story as it is kept. last: open its last part (a new story, or one just written); otherwise stay on the page that was open.
    public void Load(IReadOnlyList<ChatTurn> all, bool last = true)
    {
        turns = all; column.Children.Clear(); live = null; liveThought = null;
        if (turns.Count == 0) { page = -1; empty.Visibility = Visibility.Visible; Bar(0, 0); return; }
        Open(last || page < 0 ? turns.Count - 1 : page);
    }
    // A new part (or more of the last one) is being written; the text and what the model thinks arrive bit by bit on a page of its own.
    public void Begin(string direction, bool more = false)
    {
        column.Children.Clear(); empty.Visibility = Visibility.Collapsed;
        column.Children.Add(Heading(more ? $"#{turns.Count} 이어 쓰는 중" : $"#{turns.Count + 1} 쓰는 중")); column.Children.Add(Direction(direction));
        if (ShowThinking) { var (head, box, text) = Thoughts("…", true); column.Children.Add(head); column.Children.Add(box); liveThought = text; }
        live = Passage("…"); column.Children.Add(live); Bar(turns.Count, turns.Count + 1); back.IsEnabled = next.IsEnabled = false; scroll.ScrollToTop();
    }
    public void Streaming(string text) { if (live == null) return; live.Text = text; scroll.ScrollToEnd(); }
    public void Thinking(string text) { if (liveThought != null) liveThought.Text = text; }
    // Nothing came: back to the page that was open.
    public void Failed() { live = null; liveThought = null; Load(turns, false); }
}
