using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

// Text messages as a chat log: the character's messages on the left, the user's on the right, newest at the bottom. Each
// line of an answer is a message of its own. Under every answer are small icon buttons (set aside, excerpt, edit, delete;
// for the last one, answer again). Only the recent part of a long thread is built; the rest opens on request.
// A picture of the user's may lie behind the messages.
public sealed class MessengerView : Grid
{
    const int Step = 40;
    readonly StackPanel thread = new() { Margin = new Thickness(16, 10, 16, 14) }; readonly ScrollViewer scroll;
    readonly Image backdrop = new() { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; readonly Border veil = new() { Visibility = Visibility.Collapsed };
    readonly TextBlock empty = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40) };
    readonly List<UIElement> pending = new(); IReadOnlyList<ChatTurn> turns = Array.Empty<ChatTurn>(); FontFamily font = FontAssets.Standard; double size = 15; int shown = Step; string picture = "";
    public event Action<ChatTurn>? Edit, Delete, Keep;
    // The passage of an answer the user dragged over ("" when nothing is selected).
    public event Action<ChatTurn, string>? Excerpt;
    public event Action? Again;
    // Whether an exchange is set aside already (drawn as ★).
    public Func<ChatTurn, bool> IsKept { get; set; } = _ => false;
    // While an answer is being made the actions do nothing.
    public bool Locked { get; set; }
    public MessengerView()
    {
        Background = Pastel.Paper; ClipToBounds = true; MinHeight = 260; empty.Foreground = Pastel.Ink; empty.Opacity = .8; empty.Text = "아직 주고받은 문자가 없어요.\n아래에 첫 메시지를 보내 보세요.";
        // Over a picture lies a thin veil of the paper colour, so the small grey notes stay readable.
        var paper = ((SolidColorBrush)Pastel.Paper).Color; var thin = new SolidColorBrush(Color.FromArgb(96, paper.R, paper.G, paper.B)); thin.Freeze(); veil.Background = thin;
        scroll = new ScrollViewer { Content = new Border { Child = thread, MaxWidth = 820 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Children.Add(backdrop); Children.Add(veil); Children.Add(scroll); Children.Add(empty);
    }
    public void SetFont(FontFamily family, double fontSize) { font = family; size = Math.Clamp(fontSize - 1, FontAssets.MinSize, 30); }
    // The picture behind the messages ("" = none). It is decoded once, away from the window, no larger than a screen needs.
    public async void SetBackdrop(string path)
    {
        if (string.Equals(path, picture, StringComparison.OrdinalIgnoreCase)) return; picture = path ?? "";
        BitmapSource? decoded = null;
        if (picture.Length > 0)
            try { string file = picture; decoded = await Task.Run(() => File.Exists(file) ? ImageAssets.Read(file, 1600) : null); }
            catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException or FileFormatException) { }
        if (!string.Equals(path ?? "", picture, StringComparison.OrdinalIgnoreCase)) return;   // another picture was chosen meanwhile
        backdrop.Source = decoded; veil.Visibility = decoded == null ? Visibility.Collapsed : Visibility.Visible;
    }

    // A message made only of picture characters is drawn large, like a sticker.
    static bool Sticker(string text) => text.Length is > 0 and <= 12 && StageTalk.HasEmoji(text) && text.All(c => char.IsSurrogate(c) || char.IsWhiteSpace(c) || (c >= 0x2600 && c <= 0x27BF) || c == 0xFE0F || c == 0x200D);
    TextBox Words(string text, bool large) => new() { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Pastel.Ink, FontFamily = font, FontSize = large ? size * 2.2 : size, Padding = new Thickness(0) };
    // Where the picture of an emoticon with this title is (null = there is none, and the message is shown as it was written).
    public Func<string, string?> StickerPath { get; set; } = _ => null;
    FrameworkElement Bubble(string text, bool mine, List<TextBox>? boxes = null)
    {
        // "[이모티콘: 제목]" alone is the user's (or the character's) picture emoticon: shown as the picture, without a bubble.
        if (StageSticker.Named(text) is { } title && StickerPath(title) is { } file)
        {
            var sticker = StageParts.Picture(file, 112); sticker.Margin = new Thickness(4, 4, 4, 4); sticker.HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left; sticker.ToolTip = title; return sticker;
        }
        var words = Words(text, Sticker(text)); boxes?.Add(words);
        return new Border
        {
            Child = words, Background = mine ? Pastel.Pink : Pastel.CardBlue, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.2, 1.2, 2, 2.4), CornerRadius = mine ? new CornerRadius(14, 14, 3, 14) : new CornerRadius(14, 14, 14, 3),
            Padding = new Thickness(12, 7, 12, 8), Margin = new Thickness(mine ? 90 : 0, 3, mine ? 0 : 90, 3), HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
    }
    static TextBlock Note(string text) => new() { Text = text, Foreground = Pastel.Ink, Opacity = .7, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(30, 8, 30, 4) };
    // An answer as messages: every line is one; a stage direction (rare here) becomes a small note between them.
    IEnumerable<UIElement> Messages(string answer, List<TextBox>? boxes = null)
    {
        foreach (var beat in StageScript.Parse(answer))
        {
            if (beat.Narration.Length > 0) yield return Note(beat.Narration);
            foreach (string message in beat.Line.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) yield return Bubble(message, false, boxes);
        }
    }
    // The time of an exchange and what can be done with it: small icons under its answer.
    StackPanel Actions(ChatTurn turn, bool last, List<TextBox> boxes)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 8), Opacity = .6 };
        row.Children.Add(new TextBlock { Text = turn.At.ToLocalTime().ToString("HH:mm"), Foreground = Pastel.Ink, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        void Icon(string glyph, string tip, Action action) { var button = new Button { Content = glyph, FontSize = 13, Padding = new Thickness(5, 0, 5, 1), Foreground = Pastel.Ink, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tip }; Pastel.Ghost(button); button.Click += (_, _) => { if (!Locked) action(); }; row.Children.Add(button); }
        Icon(IsKept(turn) ? "★" : "☆", "이 주고받음을 통째로 모아 둬요 (다시 누르면 빼요)", () => Keep?.Invoke(turn));
        Icon("✂", "답장에서 마우스로 끌어 고른 부분만 모아 둬요", () => Excerpt?.Invoke(turn, boxes.Select(b => b.SelectedText.Trim()).FirstOrDefault(s => s.Length > 0) ?? ""));
        Icon("✎", "내 메시지와 답장을 고쳐요", () => Edit?.Invoke(turn)); Icon("✕", "이 주고받음을 지워요", () => Delete?.Invoke(turn));
        if (last) Icon("↻", "이 답장이 마음에 들지 않으면 지우고 다시 받아요", () => Again?.Invoke());
        row.MouseEnter += (_, _) => row.Opacity = 1; row.MouseLeave += (_, _) => row.Opacity = .6;
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
            var boxes = new List<TextBox>(); foreach (var message in Messages(turn.Assistant, boxes)) thread.Children.Add(message);
            thread.Children.Add(Actions(turn, i == turns.Count - 1, boxes));
        }
        Bottom();
    }
    void Replace(IEnumerable<UIElement> messages) { foreach (var old in pending) thread.Children.Remove(old); pending.Clear(); foreach (var message in messages) { pending.Add(message); thread.Children.Add(message); } Bottom(); }
    // The user's message went out and the answer is awaited; then it arrives bit by bit. The window loads the thread again when it is done (or failed).
    public void Asked(string message) { thread.Children.Add(Bubble(message, true)); pending.Clear(); Replace(new[] { Bubble("…", false) }); }
    public void Streaming(string partial) => Replace(Messages(partial));
}
