using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace KeyboardWorker;

// The summaries of the story open in the novel window, in three tabs as in SillyTavern's scenario summarizer:
// 상태 (what is summarized, summarizing now or a range, the summaries to read, edit, redo or delete, export and import),
// 설정 (sending them, automatic summaries and their numbers, which AI) and 항목 (the headings and the instruction).
public sealed class NovelSummaryWindow : Window
{
    readonly Buddy buddy; readonly string thread; readonly Buddy.StageCharacter[] cast; readonly NovelSummaryOptions draft;
    readonly StackPanel list = new(), items = new(); readonly TextBlock stats = new() { Foreground = Pastel.Ink, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) }, status = Pastel.Text("", 12);
    readonly TextBox search = new() { Padding = new Thickness(6, 3, 6, 3), FontSize = 12, ToolTip = "요약 내용에서 찾기" }, from = Small(), to = Small();
    readonly Button now, range, stop; CancellationTokenSource? request;
    static TextBox Small() => new() { Width = 46, FontSize = 12, Padding = new Thickness(3, 2, 3, 2), TextAlignment = TextAlignment.Center, MaxLength = 5, VerticalAlignment = VerticalAlignment.Center };
    static Button Tiny(string text, Action action, bool blue = true) { var button = Pastel.Button(text, action, blue); button.Padding = new Thickness(10, 3, 10, 4); button.FontSize = 12; button.Margin = new Thickness(0, 0, 6, 0); return button; }
    ChatTurn[] Story => buddy.StageTalks.Turns(thread).ToArray();
    public NovelSummaryWindow(Buddy buddy, string thread, Buddy.StageCharacter[] cast, string title)
    {
        this.buddy = buddy; this.thread = thread; this.cast = cast; draft = buddy.Novel.State.Summary.Snapshot();
        var tabs = new TabControl { Background = Pastel.Paper, BorderBrush = Pastel.Pink }; var root = new DockPanel { Margin = new Thickness(16) };
        StackPanel Tab(string name) { var p = new StackPanel { Margin = new Thickness(14) }; tabs.Items.Add(new TabItem { Header = name, Content = Pastel.Scroll(p), Padding = new Thickness(12, 6, 12, 6) }); return p; }

        // ---- 상태 ----
        var state = Tab("상태"); state.Children.Add(new TextBlock { Text = title, Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 0, 0, 4) }); state.Children.Add(stats);
        var act = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        now = Tiny("지금 요약", () => Run(NovelSummaries.Ready(draft, Story, buddy.Novel.SummariesOf(thread))), false); act.Children.Add(now);
        act.Children.Add(new TextBlock { Text = "범위", Foreground = Pastel.Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) }); act.Children.Add(from);
        act.Children.Add(new TextBlock { Text = "~", Foreground = Pastel.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 3, 0) }); act.Children.Add(to);
        range = Tiny("범위 요약", () => { if (int.TryParse(from.Text, out int a) && int.TryParse(to.Text, out int b) && a >= 1 && b >= a) Run(Enumerable.Range(a - 1, b - a + 1).ToArray()); else status.Text = "범위는 1 이상의 번호로, 앞 번호가 작게 적어 주세요."; }); range.Margin = new Thickness(6, 0, 6, 0); act.Children.Add(range);
        stop = Tiny("중단", () => request?.Cancel()); stop.IsEnabled = false; act.Children.Add(stop); state.Children.Add(act);
        var tools = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
        tools.Children.Add(Tiny("보낼 내용 미리보기", Preview)); tools.Children.Add(Tiny("내보내기…", Export)); tools.Children.Add(Tiny("가져오기…", Import)); state.Children.Add(tools);
        state.Children.Add(search); state.Children.Add(list); search.TextChanged += (_, _) => Fill();

        // ---- 설정 ----
        var settings = Tab("설정");
        CheckBox Check(string text, bool on, string help) { var box = new CheckBox { IsChecked = on, Margin = new Thickness(0, 3, 0, 3) }; var words = new StackPanel { Orientation = Orientation.Horizontal }; words.Children.Add(new TextBlock { Text = text, Foreground = Pastel.Ink, FontSize = 13 }); var mark = Pastel.Help(help); mark.Margin = new Thickness(5, 0, 0, 0); words.Children.Add(mark); box.Content = words; settings.Children.Add(box); return box; }
        var inject = Check("요약을 소설 프롬프트에 넣기", draft.Inject, "켜면 요약한 부분은 원문 대신 요약으로 보내 토큰을 아낍니다. 최근 몇 부분은 늘 원문 그대로 보냅니다.");
        var auto = Check("자동 요약", draft.Auto, "요약하지 않은 부분이 정한 수만큼 쌓이면 쓰기가 끝난 뒤 알아서 요약합니다.");
        TextBox Number(string label, int value, string help) { var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) }; var box = new TextBox { Text = value.ToString(), Width = 80, MaxLength = 6, Padding = new Thickness(4, 2, 4, 2) }; DockPanel.SetDock(box, Dock.Right); row.Children.Add(box); var words = new StackPanel { Orientation = Orientation.Horizontal }; words.Children.Add(new TextBlock { Text = label, Foreground = Pastel.Ink, FontSize = 13, VerticalAlignment = VerticalAlignment.Center }); var mark = Pastel.Help(help); mark.Margin = new Thickness(5, 0, 0, 0); mark.VerticalAlignment = VerticalAlignment.Center; words.Children.Add(mark); row.Children.Add(words); settings.Children.Add(row); return box; }
        var every = Number("자동 요약 간격 (부분)", draft.Every, "요약하지 않은 부분이 이만큼 쌓이면 자동으로 요약합니다.");
        var group = Number("한 요약에 묶을 부분 수", draft.Group, "몇 부분을 하나의 요약으로 묶을지. 적을수록 자세하고 토큰을 더 씁니다.");
        var recent = Number("원문으로 남길 최근 부분 수", draft.KeepRecent, "최근 이만큼의 부분은 요약하지 않고 원문 그대로 보냅니다.");
        var budget = Number("요약 토큰 예산", draft.Budget, "프롬프트에 넣을 요약의 최대 토큰. 넘으면 오래된 요약부터 빠집니다.");
        var context = Number("참고할 앞 요약 수", draft.Context, "새로 요약할 때 앞 요약을 몇 개 보여 줘서 장소·관계 같은 항목이 이어지게 합니다.");
        var names = new[] { "소설과 같은 AI" }.Concat(buddy.Chat.Preferences.Connections.Select(c => c.Name)).ToArray();
        var writer = new ComboBox { ItemsSource = names, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0) }; writer.SelectedIndex = Math.Max(0, Array.IndexOf(names, draft.Connection));
        settings.Children.Add(new TextBlock { Text = "요약에 쓸 AI", Foreground = Pastel.Ink, FontSize = 13, Margin = new Thickness(0, 8, 0, 0) }); settings.Children.Add(writer);

        // ---- 항목 ----
        var heads = Tab("항목");
        heads.Children.Add(new TextBlock { Text = "켠 항목만 요약에 들어가요. 각 항목의 지시를 고칠 수 있어요.", Foreground = Pastel.Ink, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) }); heads.Children.Add(items);
        var guide = new TextBox { Text = draft.Guide, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 150, MaxLength = 8000, FontSize = 12.5, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6) };
        heads.Children.Add(new TextBlock { Text = "요약 지침", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 10, 0, 3) }); heads.Children.Add(guide);
        heads.Children.Add(Tiny("기본값으로", () => { if (MessageBox.Show(this, "항목과 지침을 기본값으로 되돌릴까요? (저장해야 적용돼요)", "요약", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; draft.Items = NovelSummaries.StandardItems(); guide.Text = NovelSummaries.StandardGuide; Items(); }));

        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(status);
        var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("설정 저장", () =>
        {
            int Value(TextBox box, int low, int high, string label) => int.TryParse(box.Text, out int v) && v >= low && v <= high ? v : throw new InvalidOperationException($"'{label}'은(는) {low}~{high} 사이의 숫자로 적어 주세요.");
            try
            {
                draft.Inject = inject.IsChecked == true; draft.Auto = auto.IsChecked == true; draft.Every = Value(every, 1, 200, "자동 요약 간격"); draft.Group = Value(group, 1, 50, "한 요약에 묶을 부분 수");
                draft.KeepRecent = Value(recent, 0, 50, "원문으로 남길 최근 부분 수"); draft.Budget = Value(budget, 500, 500000, "요약 토큰 예산"); draft.Context = Value(context, 0, 20, "참고할 앞 요약 수");
                draft.Connection = writer.SelectedIndex <= 0 ? "" : names[writer.SelectedIndex]; draft.Guide = guide.Text;
                buddy.Novel.State.Summary = NovelSummaries.Normalize(draft.Snapshot()); buddy.Novel.Save(); status.Text = "저장했어요."; Fill();
            }
            catch (InvalidOperationException e) { status.Text = e.Message; }
        }));
        buttons.Children.Add(Pastel.Button("닫기", Close, true)); footer.Children.Add(buttons);
        root.Children.Add(tabs);
        Closed += (_, _) => request?.Cancel();
        Pastel.Window(this, "소설 요약", root, 720, 820); MinWidth = 560; MinHeight = 520; Items(); Fill();
    }
    // The headings, each switched and its instruction editable, in order.
    void Items()
    {
        items.Children.Clear();
        foreach (var item in draft.Items)
        {
            var row = new StackPanel { Margin = new Thickness(0, 2, 0, 4) }; var head = new DockPanel();
            var moves = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(moves, Dock.Right); head.Children.Add(moves);
            foreach (var (glyph, step) in new[] { ("▲", -1), ("▼", 1) }) { var move = new Button { Content = glyph, FontSize = 9, Padding = new Thickness(4, 0, 4, 1), Foreground = Pastel.Ink, Opacity = .6 }; Pastel.Ghost(move); move.Click += (_, _) => { int at = draft.Items.IndexOf(item), there = at + step; if (there < 0 || there >= draft.Items.Count) return; draft.Items.RemoveAt(at); draft.Items.Insert(there, item); Items(); }; moves.Children.Add(move); }
            var on = new CheckBox { IsChecked = item.Enabled, Content = new TextBlock { Text = item.Name, Foreground = Pastel.Ink, FontWeight = FontWeights.SemiBold, FontSize = 13 } }; on.Click += (_, _) => { item.Enabled = on.IsChecked == true; Items(); }; head.Children.Add(on); row.Children.Add(head);
            if (item.Enabled) { var prompt = new TextBox { Text = item.Prompt, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 12, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(26, 2, 0, 0), MaxLength = 2000 }; prompt.TextChanged += (_, _) => item.Prompt = prompt.Text; row.Children.Add(prompt); }
            items.Children.Add(row);
        }
    }
    // The numbers and the summaries, newest first.
    void Fill()
    {
        var story = Story; var summaries = buddy.Novel.SummariesOf(thread); var options = buddy.Novel.State.Summary; int pending = NovelSummaries.Pending(story, summaries).Length, ready = NovelSummaries.Ready(options, story, summaries).Length;
        stats.Text = $"전체 {story.Length}부분 · 요약됨 {story.Length - pending} · 대기 {pending} (지금 요약할 수 있는 것 {ready}) · 요약 {summaries.Count}개" + (options.Auto ? $" · 자동 요약까지 {Math.Max(0, options.Every - ready)}부분" : " · 자동 요약 꺼짐") + (options.Inject ? "" : " · 프롬프트에 넣지 않음");
        if (to.Text.Length == 0) { from.Text = "1"; to.Text = Math.Max(1, story.Length).ToString(); }
        list.Children.Clear(); string words = search.Text.Trim();
        var shown = summaries.Reverse().Where(s => words.Length == 0 || s.Text.Contains(words, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (shown.Length == 0) { list.Children.Add(Pastel.Text(summaries.Count == 0 ? "아직 요약이 없어요." : "맞는 요약이 없어요.", 13)); return; }
        foreach (var summary in shown)
        {
            var body = new StackPanel(); var head = new DockPanel(); var icons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(icons, Dock.Right); head.Children.Add(icons);
            head.Children.Add(new TextBlock { Text = NovelSummaries.Label(summary, story) + "  ·  " + summary.At.ToLocalTime().ToString("M월 d일 HH:mm"), Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            var text = new TextBox { Text = summary.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Padding = new Thickness(4, 2, 4, 2), BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ToolTip = "고치면 바로 저장돼요" };
            text.LostFocus += (_, _) => { if (text.Text.Trim() != summary.Text.Trim()) { buddy.Novel.Rewrite(thread, summary, text.Text); status.Text = "고친 요약을 저장했어요."; Fill(); } };
            void Icon(string glyph, string tip, Action action) { var button = new Button { Content = glyph, FontSize = 13, Padding = new Thickness(5, 0, 5, 1), Foreground = Pastel.Ink, Opacity = .65, ToolTip = tip }; Pastel.Ghost(button); button.Click += (_, _) => action(); icons.Children.Add(button); }
            Icon("⧉", "복사", () => { try { Clipboard.SetText(summary.Text); status.Text = "복사했어요."; } catch (System.Runtime.InteropServices.ExternalException) { } });
            Icon("↻", "이 범위를 다시 요약해요", () => Run(Enumerable.Range(0, story.Length).Where(i => story[i].At.UtcTicks >= summary.From && story[i].At.UtcTicks <= summary.To).ToArray()));
            Icon("✕", "이 요약을 지워요 (그 부분은 다시 원문으로 보내요)", () => { if (MessageBox.Show(this, "이 요약을 지울까요?", "요약", MessageBoxButton.YesNo) == MessageBoxResult.Yes && buddy.Novel.Forget(thread, summary)) { status.Text = "지웠어요."; Fill(); } });
            body.Children.Add(head); body.Children.Add(text);
            var card = Pastel.Card(body); card.Padding = new Thickness(12, 6, 12, 8); card.Margin = new Thickness(0, 0, 4, 6); list.Children.Add(card);
        }
    }
    async void Run(int[] places)
    {
        if (request != null) return; if (places.Length == 0) { status.Text = "요약할 부분이 없어요."; return; }
        request = new CancellationTokenSource(); now.IsEnabled = range.IsEnabled = false; stop.IsEnabled = true; status.Text = "요약하는 중…";
        try { int done = await buddy.SummarizeNovel(thread, cast, places, request.Token, step => Dispatcher.BeginInvoke(() => status.Text = "요약하는 중… " + step)); status.Text = $"요약 {done}개를 썼어요."; }
        catch (OperationCanceledException) { status.Text = "요약을 멈췄어요. 그때까지 쓴 요약은 남아 있어요."; }
        catch (Exception e) { status.Text = "요약하지 못했어요. " + e.Message; }
        finally { request.Dispose(); request = null; now.IsEnabled = range.IsEnabled = true; stop.IsEnabled = false; if (IsLoaded) Fill(); }
    }
    void Preview()
    {
        var (digest, story) = NovelSummaries.Apply(buddy.Novel.State.Summary, Story, buddy.Novel.SummariesOf(thread));
        string text = (digest.Length > 0 ? digest : "(넣을 요약이 없어요)") + $"\n\n── 원문으로 보내는 부분: {story.Length}개 ──";
        var box = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18), FontSize = 12.5 };
        var window = new Window { Owner = this }; Pastel.Window(window, "보낼 요약", box, 700, 720); window.ShowDialog();
    }
    static readonly JsonSerializerOptions json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    void Export()
    {
        var summaries = buddy.Novel.SummariesOf(thread); if (summaries.Count == 0) { status.Text = "내보낼 요약이 없어요."; return; }
        var dialog = new SaveFileDialog { Filter = "요약 (*.json)|*.json|텍스트 (*.txt)|*.txt", FileName = "소설 요약-" + DateTime.Now.ToString("yyyyMMdd") }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            var story = Story; string text = dialog.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? string.Join("\r\n\r\n", summaries.Select(s => NovelSummaries.Label(s, story) + "\r\n" + s.Text.Replace("\n", "\r\n"))) : JsonSerializer.Serialize(new { format = "keyboard-worker-novel-summaries", summaries }, json);
            File.WriteAllText(dialog.FileName, text, new UTF8Encoding(false)); status.Text = $"요약 {summaries.Count}개를 내보냈어요.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "저장하지 못했어요. " + e.Message; }
    }
    void Import()
    {
        var dialog = new OpenFileDialog { Filter = "요약 (*.json)|*.json" }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(dialog.FileName)); int added = 0;
            if (!document.RootElement.TryGetProperty("summaries", out var entries) || entries.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("요약 파일이 아니에요.");
            foreach (var entry in entries.EnumerateArray()) if (entry.Deserialize<NovelSummary>() is { Text: { Length: > 0 } } summary && summary.From <= summary.To) { buddy.Novel.Keep(thread, summary); added++; }
            status.Text = $"요약 {added}개를 가져왔어요."; Fill();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or NotSupportedException) { status.Text = "가져오지 못했어요. " + e.Message; }
    }
}
