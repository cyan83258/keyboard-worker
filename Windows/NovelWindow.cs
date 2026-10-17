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
    readonly TextBox input; readonly Button send;
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
        // The direction for the next part, and the one button that writes it (or stops). The direction is kept with the part as its input.
        var entry = PageFrame.Entry(async () => await Write(input!.Text.Trim(), false), () => request?.Cancel(), () => busy, 6000); input = entry.Input; send = entry.Send; send.Content = "쓰기";
        input.FontSize = 13.5; input.MinHeight = 52; input.ToolTip = "다음 장면에서 일어날 일을 적어요. 비워 두고 '쓰기'를 누르면 알아서 이어 써요. (Enter로 쓰기 · Shift+Enter 줄바꿈)";
        entry.Row.Children.Add(input);
        var directed = new StackPanel(); directed.Children.Add(new TextBlock { Text = "다음 전개 지시 · 이번 장면에만 적용돼요", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(2, 0, 0, 4) }); directed.Children.Add(entry.Row);
        bottom.Children.Add(new Border { Child = directed, Background = Pastel.CardBlue, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 8) });
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
        view.IsHidden = turn => buddy.Novel.IsHidden(Thread, turn);
        view.Hide += (turn, hide) => { buddy.Novel.Hide(Thread, new[] { turn }, hide); Reload(); };
        view.HideRange += (a, b, hide) => { var all = buddy.StageTalks.Turns(Thread); buddy.Novel.Hide(Thread, all.Skip(a).Take(b - a + 1), hide); Reload(); status.Text = $"#{a + 1}~{b + 1}을(를) 입력에서 " + (hide ? "뺐어요." : "다시 넣었어요."); };

        var people = buddy.StageCharacters().Select(c => new Choice(c, c.Role + " · " + c.Title)).ToArray();
        filling = true;
        first.ItemsSource = people; first.SelectedItem = people.FirstOrDefault(p => p.Character!.Id == book.First) ?? people.FirstOrDefault();
        second.ItemsSource = new[] { new Choice(null, Alone) }.Concat(people).ToArray(); second.SelectedItem = ((Choice[])second.ItemsSource).FirstOrDefault(p => p.Character?.Id == book.Second && book.Second != book.First) ?? ((Choice[])second.ItemsSource)[0];
        filling = false;
        first.SelectionChanged += (_, _) => Chosen(); second.SelectionChanged += (_, _) => Chosen();
        Closed += (_, _) => request?.Cancel();
        Pastel.Window(this, "소설", frame.Shell, 1120, 820, PageFrame.Sky); MinWidth = 900; MinHeight = 560; Enter();
        Loaded += (_, _) => input.Focus();
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
    void Reload(bool fresh = false) { view.Load(Page.Length == 0 ? Array.Empty<ChatTurn>() : buddy.StageTalks.Turns(Thread), fresh); Sent(); }
    // "입력 #3~12" under the page: what the next request sends as it is; summaries, closed eyes and what does not fit in the tip.
    void Sent()
    {
        if (Page.Length == 0 || buddy.StageTalks.Turns(Thread).Count == 0) { view.Sent("", ""); return; }
        var (sent, summarized, hidden, left) = buddy.NovelSent(Cast, Page);
        static string Ranges(int[] places) => places.Length == 0 ? "없음" : string.Join(", ", NovelSummaries.Runs(places, int.MaxValue).Select(r => r.Length == 1 ? $"#{r[0] + 1}" : $"#{r[0] + 1}~{r[^1] + 1}"));
        view.Sent("입력 " + Ranges(sent) + (summarized.Length > 0 ? " · 요약 " + summarized.Length : "") + (hidden.Length > 0 ? " · 뺌 " + hidden.Length : ""),
            "다음 쓰기에 보내는 것\n원문: " + Ranges(sent) + "\n요약으로: " + Ranges(summarized) + "\n눈 감음(뺌): " + Ranges(hidden) + "\n컨텍스트 크기를 넘어 빠짐: " + Ranges(left));
    }
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
    // One part of the story, written to the direction. again: in place of the one just taken back. False when nothing came.
    async Task<bool> Write(string direction, bool again)
    {
        var cast = Cast; if (busy || cast.Length == 0) return false; bool written = false;
        busy = view.Locked = true; send.Content = "중단"; first.IsEnabled = second.IsEnabled = input.IsEnabled = false; status.Text = "쓰는 중…"; view.Begin(direction); request = new CancellationTokenSource();
        try
        {
            var turn = await buddy.WriteNovel(Page, cast, direction, request.Token, part => Dispatcher.BeginInvoke(() => view.Streaming(part)), part => Dispatcher.BeginInvoke(() => view.Thinking(part)));
            written = true; if (!again) input.Clear(); status.Text = $"{turn.Assistant.Length:N0}자를 썼어요.";
        }
        catch (OperationCanceledException) { status.Text = "쓰기를 멈췄어요. 저장하지 않았습니다."; view.Failed(); }
        catch (Exception e) { status.Text = "쓰지 못했어요. " + e.Message; view.Failed(); }
        finally
        {
            request?.Dispose(); request = null; busy = view.Locked = false; send.Content = "쓰기"; first.IsEnabled = second.IsEnabled = input.IsEnabled = true;
            if (written) Reload(true); if (IsVisible) input.Focus();
        }
        return written;
    }
}
