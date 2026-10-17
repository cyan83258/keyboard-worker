using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// The novel: one or two of the characters in a story the AI writes part by part, read like a book (NovelView). The
// story is kept in the novel's own rooms (PageFrame's list: author's note, memory, export), written with its own prompt
// set (NovelPromptWindow) and its own AI settings (NovelApiWindow). Nothing is shared with the other pages.
public sealed class NovelWindow : Window
{
    const string Alone = "(혼자)";
    readonly Buddy buddy; readonly PageFrame frame; readonly NovelView view = new();
    readonly ComboBox first = new() { DisplayMemberPath = "Label", MinWidth = 160 }, second = new() { DisplayMemberPath = "Label", MinWidth = 150 };
    readonly TextBlock status = new() { Foreground = Pastel.Ink, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly CheckBox stream = Small("스트리밍", "쓰는 중인 글을 실시간으로 보여 줘요"), thinking = Small("추론 보기", "AI가 쓰기 전에 생각한 내용을 받아 두고, '추론' 버튼으로 펼쳐 볼 수 있게 해요 (생각을 보내 주는 모델만)");
    readonly TextBox input, guide = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000, Height = 52, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13, Padding = new Thickness(8, 5, 8, 5) };
    readonly Button send, guideButton, goOn; readonly Border guidePanel; readonly TextBlock guideNote = new() { Foreground = Pastel.Ink, Opacity = .7, FontSize = 11.5, Margin = new Thickness(2, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
    // The guide for one part: applied to the next part written and to writing that part again; once a newer part is
    // asked for, a guide that was already used is cleared instead of being applied a second time.
    string usedGuide = ""; bool guideUsed;
    // The typeface the story is read in: the one that came with the program, else the page font.
    static readonly string BookFont = System.IO.Path.Combine(AppContext.BaseDirectory, "fonts", "RIDIBatang.otf");
    sealed record Choice(Buddy.StageCharacter? Character, string Label);
    CancellationTokenSource? request; bool busy, filling;
    // Small, so the two take one thin line.
    static CheckBox Small(string text, string tip) => new() { Content = text, FontSize = 12, ToolTip = tip, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = .85, LayoutTransform = new System.Windows.Media.ScaleTransform(.82, .82) };
    Buddy.StageCharacter[] Cast => new[] { (first.SelectedItem as Choice)?.Character, (second.SelectedItem as Choice)?.Character }.Where(c => c != null).Select(c => c!).DistinctBy(c => c.Id).ToArray();
    string Page => Cast is { Length: > 0 } cast ? NovelBook.Page(cast[0].Id, cast.Length > 1 ? cast[1].Id : "") : "";
    string Thread => buddy.Rooms.Thread(Page);
    public NovelWindow(Buddy buddy)
    {
        this.buddy = buddy; var book = buddy.Novel.State;
        frame = new PageFrame(buddy, this, StageMode.Novel, () => busy, Enter, () => Cast.FirstOrDefault()?.Id);
        var with = new TextBlock { Text = "＋", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 16, Margin = new Thickness(8, 0, 8, 0), ToolTip = "함께 등장할 캐릭터 (두 명까지)" };
        frame.Left(first); frame.Left(with); frame.Left(second);
        frame.Tool("요약", "지금까지의 이야기를 요약해 두고, 요약한 부분은 요약으로 보내 긴 이야기도 이어 갈 수 있게 해요", () =>
        {
            if (busy || Page.Length == 0) return; var cast = Cast;
            new NovelSummaryWindow(buddy, Thread, cast, string.Join(" ♡ ", cast.Select(c => c.Name))) { Owner = this, ShowInTaskbar = false }.ShowDialog();
            Reload();
        });
        frame.Tool("프롬프트","소설에 쓰는 프롬프트: 항목 켜고 끄기 · 순서 · 편집 · SillyTavern 프리셋 불러오기", () => { if (busy) return; new NovelPromptWindow(buddy) { Owner = this, ShowInTaskbar = false }.ShowDialog(); });
        frame.Tool("API 설정", "소설 전용 AI 연결 · 모델 · 컨텍스트와 응답 길이 · 샘플링 · 추론", () =>
        {
            if (busy) return; new NovelApiWindow(buddy) { Owner = this, ShowInTaskbar = false }.ShowDialog();
            stream.IsChecked = book.Stream; thinking.IsChecked = view.ShowThinking = book.ShowThinking; Reload();
        });

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); frame.Root.Children.Add(bottom);
        // Above the entry: the guide for the next part, shown when its button is pressed.
        var guideHead = new TextBlock { Text = "다음 전개 지시 · 이번 장면에만 적용돼요", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(2, 0, 0, 3) };
        var guideBody = new StackPanel(); guideBody.Children.Add(guideHead); guideBody.Children.Add(guide); guideBody.Children.Add(guideNote);
        guidePanel = new Border { Child = guideBody, Background = Pastel.CardBlue, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        bottom.Children.Add(guidePanel);
        guide.TextChanged += (_, _) => { if (guideUsed && guide.Text.Trim() != usedGuide) guideUsed = false; Guide(); };
        var entry = PageFrame.Entry(async () => await Write(input!.Text.Trim(), false), () => request?.Cancel(), () => busy, 6000); input = entry.Input; send = entry.Send; send.Content = "쓰기";
        input.ToolTip = "다음 장면에서 일어날 일을 적어요. 비워 두고 '쓰기'를 누르면 이어서 써요. (Enter로 쓰기 · Shift+Enter 줄바꿈)";
        Button Side(string text, string tip, Action action) { var button = Pastel.Button(text, action, true); button.Padding = new Thickness(11, 6, 11, 6); button.FontSize = 12.5; button.Margin = new Thickness(6, 0, 0, 0); button.VerticalAlignment = VerticalAlignment.Bottom; button.ToolTip = tip; DockPanel.SetDock(button, Dock.Right); entry.Row.Children.Add(button); return button; }
        goOn = Side("이어 쓰기", "마지막 부분을 끊긴 곳에서 이어서 더 써요", async () => await Write("", false, true));
        guideButton = Side("다음 전개 지시", "이번 장면에만 적용할 전개 지시를 적어요 (쓴 뒤 다시 쓰기에도 적용, 그다음 장면부터는 빠져요)", () => { guidePanel.Visibility = guidePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; if (guidePanel.Visibility == Visibility.Visible) guide.Focus(); });
        entry.Row.Children.Add(input); bottom.Children.Add(entry.Row);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 6, 0, 0) }; options.Children.Add(stream); options.Children.Add(thinking); options.Children.Add(status); bottom.Children.Add(options);
        stream.IsChecked = book.Stream; thinking.IsChecked = book.ShowThinking;
        stream.Click += (_, _) => { book.Stream = stream.IsChecked == true; buddy.Novel.Save(); };
        thinking.Click += (_, _) => { book.ShowThinking = view.ShowThinking = thinking.IsChecked == true; buddy.Novel.Save(); Reload(); };
        // Once, the first time: a note that a novel wants a capable model.
        if (!book.Welcomed)
        {
            var note = new DockPanel(); var close = Pastel.Button("알겠어요", () => { }, true); close.Padding = new Thickness(10, 3, 10, 4); close.FontSize = 12; DockPanel.SetDock(close, Dock.Right); note.Children.Add(close);
            note.Children.Add(new TextBlock { Text = "소설은 긴 이야기의 흐름과 인물을 함께 붙잡아야 해서, 지능이 높은 모델(각 회사의 최상위 모델)을 쓸수록 훨씬 잘 써요. 'API 설정'에서 소설에만 쓸 모델을 따로 고를 수 있어요.", Foreground = Pastel.Ink, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var card = new Border { Child = note, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 7, 10, 7), Margin = new Thickness(0, 0, 0, 8) };
            close.Click += (_, _) => { card.Visibility = Visibility.Collapsed; book.Welcomed = true; buddy.Novel.Save(); }; DockPanel.SetDock(card, Dock.Top); frame.Root.Children.Add(card);
        }
        frame.Root.Children.Add(new Border { Child = view,BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 3, 3.5), CornerRadius = new CornerRadius(12), ClipToBounds = true });
        view.ShowThinking = book.ShowThinking; view.Thought = turn => buddy.Novel.Thought(Thread, turn);
        view.Edit += Edit; view.Delete += Delete; view.Again += async () => await Again();

        var people = buddy.StageCharacters().Select(c => new Choice(c, c.Role + " · " + c.Title)).ToArray();
        filling = true;
        first.ItemsSource = people; first.SelectedItem = people.FirstOrDefault(p => p.Character!.Id == book.First) ?? people.FirstOrDefault();
        second.ItemsSource = new[] { new Choice(null, Alone) }.Concat(people).ToArray(); second.SelectedItem = ((Choice[])second.ItemsSource).FirstOrDefault(p => p.Character?.Id == book.Second && book.Second != book.First) ?? ((Choice[])second.ItemsSource)[0];
        filling = false;
        first.SelectionChanged += (_, _) => Chosen(); second.SelectionChanged += (_, _) => Chosen();
        Closed += (_, _) => request?.Cancel();
        Pastel.Window(this, "소설", frame.Shell, 1120, 820, PageFrame.Sky); MinWidth = 900; MinHeight = 560; Enter();
        Loaded += (_, _) => input.Focus(); Guide();
    }
    public void Select(string id) { foreach (Choice choice in first.Items) if (choice.Character?.Id == id) { first.SelectedItem = choice; return; } }
    // The cast changed: it is remembered, and the novel of these characters is opened.
    void Chosen()
    {
        if (filling) return; var cast = Cast; var book = buddy.Novel.State;
        book.First = cast.Length > 0 ? cast[0].Id : ""; book.Second = cast.Length > 1 ? cast[1].Id : ""; buddy.Novel.Save(); Enter();
    }
    void Enter()
    {
        var cast = Cast; string names = string.Join(" ♡ ", cast.Select(c => c.Name));
        frame.Rooms.Load(Page, names + " · 소설");
        Title = names + " · 소설" + (Page.Length > 0 && buddy.Rooms.Rooms(Page).Count > 1 ? " · " + buddy.Rooms.Current(Page).Name : "");
        view.SetFont(System.IO.File.Exists(BookFont) ? FontAssets.Get(BookFont) : FontAssets.Get(buddy.Pref.StageFontPath), Math.Max(16, buddy.Pref.StageFontSize)); Reload(true); status.Text = cast.Length == 0 ? "등장할 캐릭터를 골라 주세요." : "";
    }
    void Reload(bool fresh = false) => view.Load(Page.Length == 0 ? Array.Empty<ChatTurn>() : buddy.StageTalks.Turns(Thread), fresh);
    void Edit(ChatTurn turn)
    {
        var editor = new StageTurnEditor(turn) { Owner = this, ShowInTaskbar = false };
        if (editor.ShowDialog() == true && editor.Result != null && buddy.StageTalks.Replace(Thread, turn, editor.Result)) { Reload(); status.Text = "고친 내용을 저장했어요."; }
    }
    void Delete(ChatTurn turn)
    {
        if (MessageBox.Show(this, "이 부분을 삭제할까요?", "소설", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (buddy.StageTalks.Remove(Thread, turn)) { Reload(); status.Text = "삭제했어요."; }
    }
    // The last part is taken back and written again from the same direction; it returns if no new part comes.
    async Task Again()
    {
        string thread = Thread; var last = buddy.StageTalks.Turns(thread).LastOrDefault(); if (busy || last == null) return;
        buddy.StageTalks.Remove(thread, last); Reload();
        if (!await Write(last.User, true)) { buddy.StageTalks.Import(thread, new[] { last }); Reload(); }
    }
    // How the guide stands: waiting to be used, used by the last part (and kept for writing it again), or empty.
    void Guide()
    {
        string text = guide.Text.Trim(); guideButton.Content = text.Length > 0 ? "다음 전개 지시 ●" : "다음 전개 지시";
        guideNote.Text = text.Length == 0 ? "비어 있어요." : guideUsed ? "방금 쓴 부분에 적용됐어요. ↻ 다시 쓰기에도 적용되고, 새 장면을 쓰면 지워져요." : "다음에 쓰는 장면에 적용돼요.";
    }
    // One part of the story. again: written in place of the one just taken back; more: the last part goes on. False when nothing came.
    async Task<bool> Write(string direction, bool again, bool more = false)
    {
        var cast = Cast; if (busy || cast.Length == 0) return false; bool written = false;
        // A guide already used by the last part is only for that part: asking for a new one clears it.
        string text = guide.Text.Trim(); if (!again && !more && guideUsed) { guide.Text = ""; text = ""; }
        string applied = again || more ? usedGuide : text;
        busy = view.Locked = true; send.Content = "중단"; first.IsEnabled = second.IsEnabled = input.IsEnabled = goOn.IsEnabled = false; status.Text = more ? "이어 쓰는 중…" : "쓰는 중…"; view.Begin(more ? "" : direction, more); request = new CancellationTokenSource();
        try
        {
            var turn = await buddy.WriteNovel(Page, cast, direction, applied, more, request.Token, part => Dispatcher.BeginInvoke(() => view.Streaming(part)), part => Dispatcher.BeginInvoke(() => view.Thinking(part)));
            written = true; if (!again && !more) input.Clear(); status.Text = $"{turn.Assistant.Length:N0}자" + (more ? "가 됐어요." : "를 썼어요.");
            if (!more) { usedGuide = applied; guideUsed = applied.Length > 0; if (guideUsed && guide.Text.Trim() != applied) guide.Text = applied; Guide(); }
        }
        catch (OperationCanceledException) { status.Text = "쓰기를 멈췄어요. 저장하지 않았습니다."; view.Failed(); }
        catch (Exception e) { status.Text = "쓰지 못했어요. " + e.Message; view.Failed(); }
        finally
        {
            request?.Dispose(); request = null; busy = view.Locked = false; send.Content = "쓰기"; first.IsEnabled = second.IsEnabled = input.IsEnabled = goOn.IsEnabled = true;
            if (written) Reload(true); if (IsVisible) input.Focus();
        }
        return written;
    }
}
