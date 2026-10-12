using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardWorker;

// Text messages as a chat log: the character's messages on the left, the user's on the right, newest at the bottom. Each
// line of an answer is a message of its own. Under every exchange are its small actions (edit, delete; for the last one,
// answer again). Only the recent part of a long thread is built; the rest opens on request.
public sealed class MessengerView : Grid
{
    const int Step = 40;
    readonly StackPanel thread = new() { Margin = new Thickness(16, 10, 16, 14) }; readonly ScrollViewer scroll;
    readonly TextBlock empty = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40) };
    readonly List<UIElement> pending = new(); IReadOnlyList<ChatTurn> turns = Array.Empty<ChatTurn>(); FontFamily font = FontAssets.Standard; double size = 15; int shown = Step;
    public event Action<ChatTurn>? Edit, Delete;
    public event Action? Again;
    // While an answer is being made the actions do nothing.
    public bool Locked { get; set; }
    public MessengerView()
    {
        Background = Pastel.Paper; ClipToBounds = true; MinHeight = 260; empty.Foreground = Pastel.Ink; empty.Opacity = .8; empty.Text = "아직 주고받은 문자가 없어요.\n아래에 첫 메시지를 보내 보세요.";
        scroll = new ScrollViewer { Content = new Border { Child = thread, MaxWidth = 820 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Children.Add(scroll); Children.Add(empty);
    }
    public void SetFont(FontFamily family, double fontSize) { font = family; size = Math.Clamp(fontSize - 1, FontAssets.MinSize, 30); }

    Border Bubble(string text, bool mine) => new()
    {
        Child = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Pastel.Ink, FontFamily = font, FontSize = size, Padding = new Thickness(0) },
        Background = mine ? Pastel.Pink : Pastel.CardBlue, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.2, 1.2, 2, 2.4), CornerRadius = mine ? new CornerRadius(14, 14, 3, 14) : new CornerRadius(14, 14, 14, 3),
        Padding = new Thickness(12, 7, 12, 8), Margin = new Thickness(mine ? 90 : 0, 3, mine ? 0 : 90, 3), HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left
    };
    static TextBlock Note(string text) => new() { Text = text, Foreground = Pastel.Ink, Opacity = .7, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(30, 8, 30, 4) };
    // An answer as messages: every line is one; a stage direction (rare here) becomes a small note between them.
    IEnumerable<UIElement> Messages(string answer)
    {
        foreach (var beat in StageScript.Parse(answer))
        {
            if (beat.Narration.Length > 0) yield return Note(beat.Narration);
            foreach (string message in beat.Line.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) yield return Bubble(message, false);
        }
    }
    // The time of an exchange and what can be done with it, small and pale under its messages.
    StackPanel Actions(ChatTurn turn, bool last)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 8), Opacity = .62 };
        row.Children.Add(new TextBlock { Text = turn.At.ToLocalTime().ToString("HH:mm"), Foreground = Pastel.Ink, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        void Act(string text, string tip, Action action) { var button = new Button { Content = text, FontSize = 11, Padding = new Thickness(6, 1, 6, 2), Foreground = Pastel.Ink, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tip }; Pastel.Ghost(button); button.Click += (_, _) => { if (!Locked) action(); }; row.Children.Add(button); }
        Act("편집", "내 메시지와 답장을 고쳐요", () => Edit?.Invoke(turn)); Act("삭제", "이 주고받음을 지워요", () => Delete?.Invoke(turn));
        if (last) Act("↻ 다시 받기", "이 답장을 지우고 다시 받아요", () => Again?.Invoke());
        row.MouseEnter += (_, _) => row.Opacity = 1; row.MouseLeave += (_, _) => row.Opacity = .62;
        return row;
    }
    void Bottom() { empty.Visibility = thread.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed; scroll.UpdateLayout(); scroll.ScrollToEnd(); }
    // The thread as it is saved. fresh: another thread than before, so only its recent part is built again.
    public void Load(IReadOnlyList<ChatTurn> all, bool fresh = true)
    {
        turns = all; if (fresh) shown = Step; thread.Children.Clear(); pending.Clear();
        if (turns.Count > shown)
        {
            var more = Pastel.Button("이전 메시지 더 보기 (" + (turns.Count - shown) + "개)", () => { shown += Step; Load(turns, false); scroll.ScrollToTop(); }, true); more.FontSize = 12; more.HorizontalAlignment = HorizontalAlignment.Center; thread.Children.Add(more);
        }
        string day = "";
        for (int i = Math.Max(0, turns.Count - shown); i < turns.Count; i++)
        {
            var turn = turns[i]; string date = turn.At.ToLocalTime().ToString("yyyy년 M월 d일"); if (date != day) { day = date; thread.Children.Add(Note(date)); }
            if (turn.User.Trim().Length > 0) thread.Children.Add(Bubble(turn.User, true));
            foreach (var message in Messages(turn.Assistant)) thread.Children.Add(message);
            thread.Children.Add(Actions(turn, i == turns.Count - 1));
        }
        Bottom();
    }
    void Replace(IEnumerable<UIElement> messages) { foreach (var old in pending) thread.Children.Remove(old); pending.Clear(); foreach (var message in messages) { pending.Add(message); thread.Children.Add(message); } Bottom(); }
    // The user's message went out and the answer is awaited; then it arrives bit by bit. The window loads the thread again when it is done (or failed).
    public void Asked(string message) { thread.Children.Add(Bubble(message, true)); pending.Clear(); Replace(new[] { Bubble("…", false) }); }
    public void Streaming(string partial) => Replace(Messages(partial));
}
