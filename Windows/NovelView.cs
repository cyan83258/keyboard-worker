using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardWorker;

// A story as a page of a book: a calm column of text, each part under the direction it was written to (small and pale),
// with what the model thought folded away above it when that is kept. Under each part are small icons to edit or delete
// it; the last one can be written again. Only the newest parts are built; older ones open on request.
public sealed class NovelView : Grid
{
    const int Step = 20;
    readonly StackPanel column = new() { Margin = new Thickness(36, 22, 36, 30) }; readonly ScrollViewer scroll;
    readonly TextBlock empty = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40) };
    FontFamily font = FontAssets.Standard; double size = 16; int shown = Step; IReadOnlyList<ChatTurn> turns = Array.Empty<ChatTurn>();
    TextBox? live; TextBlock? liveThought; Border? liveThoughtBox; readonly List<UIElement> pending = new();
    public event Action<ChatTurn>? Edit, Delete;
    public event Action? Again;
    // What the model thought for a part ("" = nothing kept); shown only when the window says so.
    public Func<ChatTurn, string> Thought { get; set; } = _ => "";
    public bool ShowThinking { get; set; }
    public bool Locked { get; set; }
    public NovelView()
    {
        Background = Pastel.Paper; ClipToBounds = true; empty.Foreground = Pastel.Ink; empty.Opacity = .75;
        empty.Text = "아직 쓴 이야기가 없어요.\n아래에 첫 장면의 방향을 적거나, 비워 두고 '쓰기'를 눌러 보세요.";
        scroll = new ScrollViewer { Content = new Border { Child = column, MaxWidth = 820 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Children.Add(scroll); Children.Add(empty);
    }
    public void SetFont(FontFamily family, double fontSize) { font = family; size = Math.Clamp(fontSize, FontAssets.MinSize, 30); }

    TextBox Passage(string text)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Pastel.Ink, FontFamily = font, FontSize = size, Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 4) };
        TextBlock.SetLineHeight(box, Math.Round(size * 1.75)); TextBlock.SetLineStackingStrategy(box, LineStackingStrategy.BlockLineHeight); return box;
    }
    static TextBlock Direction(string text) => new() { Text = text.Trim().Length > 0 ? "✎  " + text.Trim() : "· · ·", Foreground = Pastel.Ink, Opacity = .55, FontSize = 12, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 2), TextAlignment = text.Trim().Length > 0 ? TextAlignment.Left : TextAlignment.Center };
    // What the model thought, folded under a small "추론" button.
    (UIElement Head, Border Box, TextBlock Text) Thoughts(string text)
    {
        var words = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Pastel.Ink, Opacity = .75, FontSize = 12, FontStyle = FontStyles.Italic, LineHeight = 19 };
        var box = new Border { Child = words, BorderBrush = Pastel.Line, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 2, 0, 4), Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
        var toggle = new Button { Content = "추론 ▸", FontSize = 11, Padding = new Thickness(6, 1, 6, 2), Foreground = Pastel.Ink, Opacity = .7, HorizontalAlignment = HorizontalAlignment.Left, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "AI가 쓰기 전에 생각한 내용 펼치기 / 접기" };
        Pastel.Ghost(toggle); toggle.Click += (_, _) => { bool open = box.Visibility != Visibility.Visible; box.Visibility = open ? Visibility.Visible : Visibility.Collapsed; toggle.Content = open ? "추론 ▾" : "추론 ▸"; };
        return (toggle, box, words);
    }
    StackPanel Actions(ChatTurn turn, bool last)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Opacity = .55 };
        row.Children.Add(new TextBlock { Text = $"{turn.At.ToLocalTime():M월 d일 HH:mm} · {turn.Assistant.Length:N0}자", Foreground = Pastel.Ink, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        void Icon(string glyph, string tip, Action action) { var button = new Button { Content = glyph, FontSize = 13, Padding = new Thickness(5, 0, 5, 1), Foreground = Pastel.Ink, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tip }; Pastel.Ghost(button); button.Click += (_, _) => { if (!Locked) action(); }; row.Children.Add(button); }
        Icon("✎", "이 부분과 그 지시를 고쳐요", () => Edit?.Invoke(turn)); Icon("✕", "이 부분을 지워요", () => Delete?.Invoke(turn));
        if (last) Icon("↻", "이 부분이 마음에 들지 않으면 지우고 같은 지시로 다시 써요", () => Again?.Invoke());
        row.MouseEnter += (_, _) => row.Opacity = 1; row.MouseLeave += (_, _) => row.Opacity = .55;
        return row;
    }
    void Bottom() { empty.Visibility = column.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed; scroll.UpdateLayout(); scroll.ScrollToEnd(); }
    // The story as it is kept. fresh: another story than before, so only its newest parts are built again.
    public void Load(IReadOnlyList<ChatTurn> all, bool fresh = true, bool toEnd = true)
    {
        turns = all; if (fresh) shown = Step; column.Children.Clear(); pending.Clear(); live = null; liveThought = null; liveThoughtBox = null;
        if (turns.Count > shown) { var more = Pastel.Button("앞부분 더 보기 (" + (turns.Count - shown) + "개)", () => { shown += Step; Load(turns, false, false); scroll.ScrollToTop(); }, true); more.FontSize = 12; more.HorizontalAlignment = HorizontalAlignment.Center; column.Children.Add(more); }
        for (int i = Math.Max(0, turns.Count - shown); i < turns.Count; i++)
        {
            var turn = turns[i]; column.Children.Add(Direction(turn.User));
            if (ShowThinking && Thought(turn) is { Length: > 0 } thought) { var (head, box, _) = Thoughts(thought); column.Children.Add(head); column.Children.Add(box); }
            column.Children.Add(Passage(turn.Assistant)); column.Children.Add(Actions(turn, i == turns.Count - 1));
        }
        if (toEnd) Bottom(); else empty.Visibility = column.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    // A new part is being written under this direction; the text (and what the model thinks) arrive bit by bit. The window loads the story again when it is done.
    public void Begin(string direction)
    {
        pending.Clear(); void Add(UIElement part) { pending.Add(part); column.Children.Add(part); }
        Add(Direction(direction));
        if (ShowThinking) { var (head, box, text) = Thoughts("…"); box.Visibility = Visibility.Visible; Add(head); Add(box); liveThought = text; liveThoughtBox = box; }
        live = Passage("…"); Add(live); Bottom();
    }
    public void Streaming(string text) { if (live == null) return; live.Text = text; scroll.ScrollToEnd(); }
    public void Thinking(string text) { if (liveThought != null) liveThought.Text = text; }
    // No part came: what Begin added goes again.
    public void Failed() { foreach (var part in pending) column.Children.Remove(part); pending.Clear(); live = null; liveThought = null; Bottom(); }
}
