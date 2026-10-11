using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardWorker;

// Attendance, the week's goals (written by the user), mini events and achievements.
public sealed class ProgressWindow : Window
{
    readonly Buddy buddy; readonly TabControl tabs = new() { Background = Pastel.Paper, BorderBrush = Pastel.Pink };
    readonly TextBlock attendance = Pastel.Text("", 14), eventInfo = Pastel.Text("", 13), status = Pastel.Text("", 12), coins = Pastel.Text("", 12);
    readonly StackPanel goals = new(), achievements = new(); readonly CheckBox events = new() { Content = "작업 중 미니 이벤트 열기" };
    readonly TextBox goalTitle = new() { MaxLength = 60 }, goalTarget = new() { Text = "120", Width = 90 }; readonly ComboBox goalKind = new() { DisplayMemberPath = "Title" };
    readonly Button goalSave; string editing = "";
    sealed record KindChoice(string Key, string Title);
    public void ShowPage(int index) => tabs.SelectedIndex = index;
    public int Pages => tabs.Items.Count;
    public ProgressWindow(Buddy buddy)
    {
        this.buddy = buddy; var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel(); footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(tabs);
        StackPanel Tab(string title) { var p = new StackPanel { Margin = new Thickness(18) }; tabs.Items.Add(new TabItem { Header = title, Content = Pastel.Scroll(p), Padding = new Thickness(14, 8, 14, 8) }); return p; }

        var days = Tab("출석"); days.Children.Add(Pastel.Text("오늘도 만났어요 ♡", 22)); days.Children.Add(Pastel.Card(attendance, true));
        days.Children.Add(Pastel.Text("하루에 한 번, 그날 처음 키보드나 마우스를 쓸 때 출석이 찍힙니다. 연속으로 올수록 코인이 늘어요 (첫날 5코인, 하루마다 +2, 최대 25코인). 3·7·14·30·50·100일 연속에는 보너스가 있어요.\n하루를 건너뛰면 연속 기록만 1일부터 다시 시작하고, 지금까지 받은 것은 그대로예요.", 12));

        var week = Tab("주간 목표"); week.Children.Add(Pastel.Text("이번 주의 약속", 22));
        week.Children.Add(Pastel.Text("목표는 직접 만들고 고치고 지울 수 있어요 (최대 " + ProgressBook.MaxGoals + "개). 월요일마다 진행도가 0으로 돌아가고 목표는 그대로 남습니다. 달성하면 코인을 받아요.", 12));
        week.Children.Add(goals);
        var form = new StackPanel(); form.Children.Add(Pastel.Text("목표 만들기 · 고치기", 15));
        goalKind.ItemsSource = GoalKinds.All.Select(k => new KindChoice(k.Key, k.Title + " (" + k.Unit + ") · 달성 " + k.Coins + "코인")).ToArray(); goalKind.SelectedIndex = 0;
        form.Children.Add(Pastel.Text("이름", 12)); form.Children.Add(goalTitle); form.Children.Add(Pastel.Text("세는 방법", 12)); form.Children.Add(goalKind);
        var targetRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) }; var range = Pastel.Text("", 12); range.Margin = new Thickness(10, 0, 0, 0); range.VerticalAlignment = VerticalAlignment.Center;
        targetRow.Children.Add(new TextBlock { Text = "목표 수치 ", Foreground = Pastel.Ink, VerticalAlignment = VerticalAlignment.Center }); targetRow.Children.Add(goalTarget); targetRow.Children.Add(range); form.Children.Add(targetRow);
        void ShowRange() { if (goalKind.SelectedItem is KindChoice choice) { var kind = GoalKinds.Find(choice.Key); range.Text = $"{kind.Low}~{kind.High}{kind.Unit}" + (kind.Key == "manual" ? " · 직접 +1 버튼으로 셉니다 (보상은 주 " + ProgressBook.ManualRewardsPerWeek + "번까지)" : ""); } }
        goalKind.SelectionChanged += (_, _) => ShowRange(); ShowRange();
        var formButtons = new WrapPanel();
        goalSave = Pastel.Button("목표 추가", () =>
        {
            try
            {
                if (!int.TryParse(goalTarget.Text, out int target)) throw new InvalidOperationException("목표 수치를 숫자로 적어 주세요.");
                string kind = (goalKind.SelectedItem as KindChoice)?.Key ?? "work";
                if (editing.Length > 0) { buddy.Progress.EditGoal(editing, goalTitle.Text, kind, target); status.Text = "목표를 고쳤어요."; }
                else { buddy.Progress.AddGoal(goalTitle.Text, kind, target, DateTime.Now); status.Text = "목표를 추가했어요 ♡"; }
                buddy.Progress.Save(true); Reset();
            }
            catch (Exception e) { status.Text = e.Message; }
        });
        formButtons.Children.Add(goalSave); formButtons.Children.Add(Pastel.Button("새로 쓰기", Reset, true)); form.Children.Add(formButtons); week.Children.Add(Pastel.Card(form));

        var mini = Tab("미니 이벤트"); mini.Children.Add(Pastel.Text("작업하다 만나는 작은 도전", 22)); mini.Children.Add(events); mini.Children.Add(Pastel.Card(eventInfo, true));
        mini.Children.Add(Pastel.Text("키보드 작업이 40~80분쯤 쌓이면 가끔 이벤트가 열립니다 (하루 " + ProgressBook.EventsPerDay + "번까지).\n· 반짝 집중: 20분 안에 작업 10분 → 15코인\n· 꾸준히 한 걸음: 45분 안에 작업 20분 → 30코인\n· 둘이서 응원 (둘째 친구가 있을 때): 30분 안에 작업 15분 → 25코인\n놓쳐도 잃는 것은 없어요. 조용한 모드나 숨김 중에는 알림이 뜨지 않고 기다립니다.", 12));
        events.Click += (_, _) => { buddy.Progress.SetEvents(events.IsChecked == true); buddy.Progress.Save(true); status.Text = events.IsChecked == true ? "미니 이벤트를 켰어요." : "미니 이벤트를 껐어요."; };

        var medals = Tab("업적"); medals.Children.Add(Pastel.Text("함께 쌓아 온 것들", 22)); medals.Children.Add(achievements);

        var buttons = new WrapPanel(); coins.VerticalAlignment = VerticalAlignment.Center; coins.Margin = new Thickness(0, 0, 12, 0);
        buttons.Children.Add(coins); buttons.Children.Add(Pastel.Button("상점 · 옷장", buddy.OpenGame, true)); buttons.Children.Add(Pastel.Button("닫기", Close)); footer.Children.Add(buttons);
        buddy.Progress.Changed += Changed; buddy.Game.Changed += Changed; Closed += (_, _) => { buddy.Progress.Changed -= Changed; buddy.Game.Changed -= Changed; };
        Pastel.Window(this, "출석 · 목표 · 업적", root, 720, 800); Refresh();
    }
    bool queued;
    void Changed() { if (queued) return; queued = true; Dispatcher.BeginInvoke(() => { queued = false; Refresh(); }); }
    void Reset() { editing = ""; goalTitle.Clear(); goalSave.Content = "목표 추가"; Refresh(); }
    internal static Border Bar(double share, bool done)
    {
        var track = new Grid { Height = 10, Margin = new Thickness(0, 5, 0, 0) }; track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(share, 0, 1), GridUnitType.Star) }); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - Math.Clamp(share, 0, 1), GridUnitType.Star) });
        track.Children.Add(new Border { Background = done ? Pastel.Hot : Pastel.Teal, CornerRadius = new CornerRadius(5) });
        return new Border { Child = track, Background = Brushes.White, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(1), Margin = new Thickness(0, 4, 0, 0) };
    }
    void Refresh()
    {
        var state = buddy.Progress.State; var now = DateTimeOffset.Now; string today = DateTime.Now.ToString("yyyy-MM-dd");
        coins.Text = $"보유 {buddy.Game.State.Coins:N0}코인";
        attendance.Text = (state.LastDay == today ? "오늘 출석 완료 ♡" : "오늘은 아직 출석 전이에요 (키보드나 마우스를 쓰면 찍혀요).") + $"\n연속 {state.Streak}일 · 최고 연속 {state.BestStreak}일 · 지금까지 {state.TotalDays}일\n이번 주 출석 {state.WeekDays}일 · 내일 출석 보상 {ProgressBook.CheckInCoins(state.LastDay == today ? state.Streak + 1 : 1)}코인";
        events.IsChecked = state.Events;
        eventInfo.Text = !state.Events ? "미니 이벤트가 꺼져 있어요." : state.Event is { } running && now <= running.Ends ? "진행 중 · " + running.Title + "\n" + ProgressBook.Describe(running, now)
            : $"지금은 열린 이벤트가 없어요.\n오늘 {(state.EventDay == today ? state.EventsToday : 0)}/{ProgressBook.EventsPerDay}번 · 다음 이벤트까지 작업 약 {Math.Max(0, (state.NextEventMs - state.SinceEventMs) / 60000)}분 · 지금까지 성공 {buddy.Progress.Counter("events")}번";
        goals.Children.Clear();
        if (state.Goals.Count == 0) goals.Children.Add(Pastel.Text("아직 목표가 없어요. 아래에서 이번 주에 해 보고 싶은 것을 적어 보세요.", 13));
        foreach (var goal in state.Goals.ToArray())
        {
            var kind = GoalKinds.Find(goal.Kind); var body = new StackPanel(); var head = new DockPanel(); var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right);
            void Small(Button button) { button.Padding = new Thickness(8, 2, 8, 2); button.Margin = new Thickness(4, 0, 0, 0); button.FontSize = 11; actions.Children.Add(button); }
            if (goal.Kind == "manual" && !goal.Done) Small(Pastel.Button("+1", () => { buddy.TickGoal(goal.Id); buddy.Progress.Save(true); }));
            Small(Pastel.Button("고치기", () => { editing = goal.Id; goalTitle.Text = goal.Title; goalTarget.Text = goal.Target.ToString(); goalKind.SelectedIndex = Math.Max(0, Array.FindIndex(GoalKinds.All, k => k.Key == goal.Kind)); goalSave.Content = "고친 내용 저장"; status.Text = "'" + goal.Title + "' 목표를 고치는 중이에요."; }, true));
            Small(Pastel.Button("삭제", () => { if (MessageBox.Show(this, "'" + goal.Title + "' 목표를 삭제할까요?", "주간 목표", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; buddy.Progress.DeleteGoal(goal.Id); buddy.Progress.Save(true); if (editing == goal.Id) Reset(); }, true));
            head.Children.Add(actions); head.Children.Add(new TextBlock { Text = (goal.Done ? "✓ " : "") + goal.Title, Foreground = Pastel.Ink, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(head); body.Children.Add(new TextBlock { Text = $"{kind.Title} · {Math.Min(goal.Value, goal.Target)}/{goal.Target}{kind.Unit}" + (goal.Done ? " · 달성!" : ""), Foreground = Pastel.Ink, FontSize = 12, Margin = new Thickness(0, 3, 0, 0) });
            body.Children.Add(Bar(goal.Target == 0 ? 0 : (double)goal.Value / goal.Target, goal.Done));
            var card = Pastel.Card(body, goal.Done); card.Padding = new Thickness(12, 8, 12, 10); card.Margin = new Thickness(0, 0, 4, 6); goals.Children.Add(card);
        }
        achievements.Children.Clear(); int reached = ProgressBook.All.Count(a => state.Achievements.ContainsKey(a.Id));
        achievements.Children.Add(Pastel.Text($"달성 {reached} / {ProgressBook.All.Length}", 14));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var item in ProgressBook.All.OrderByDescending(a => state.Achievements.ContainsKey(a.Id)))
        {
            bool done = state.Achievements.TryGetValue(item.Id, out var day); var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = (done ? "★ " : "☆ ") + item.Title, Foreground = done ? Pastel.Hot : Pastel.Ink, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
            body.Children.Add(new TextBlock { Text = item.Description + " · " + item.Coins + "코인" + (done ? "\n" + day + " 달성" : ""), Foreground = Pastel.Ink, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Opacity = done ? 1 : .8 });
            var card = Pastel.Card(body, done); card.Padding = new Thickness(11, 7, 10, 8); card.Margin = new Thickness(0, 0, 6, 6); grid.Children.Add(card);
        }
        achievements.Children.Add(grid);
    }
}

// Looking through the saved chat and writing it to a file.
public sealed class ChatSearchWindow : Window
{
    readonly Buddy buddy; readonly TextBox search = new() { Padding = new Thickness(8, 5, 8, 5) }; readonly StackPanel list = new(); readonly TextBlock count = Pastel.Text("", 12), status = Pastel.Text("", 12);
    public ChatSearchWindow(Buddy buddy)
    {
        this.buddy = buddy; var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(Pastel.Text("대화 검색 · 내보내기", 22)); top.Children.Add(Pastel.Text("보관 중인 대화에서 낱말을 찾습니다. 띄어쓰기로 여러 낱말을 적으면 모두 들어 있는 대화만 보여요.", 12)); top.Children.Add(search); top.Children.Add(count);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(status);
        var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("찾은 대화 내보내기…", () => Export(Found())));
        buttons.Children.Add(Pastel.Button("전체 대화 내보내기…", () => Export(buddy.Chat.Turns.ToArray()), true));
        buttons.Children.Add(Pastel.Button("캐릭터 채팅 열기", buddy.OpenChat, true)); buttons.Children.Add(Pastel.Button("닫기", Close, true)); footer.Children.Add(buttons);
        root.Children.Add(Pastel.Scroll(list));
        search.TextChanged += (_, _) => Refresh(); Activated += (_, _) => Refresh();
        Pastel.Window(this, "대화 검색", root, 680, 780); Refresh();
    }
    // Every word must be in what the user said, in the answer, or in the character's name.
    public static ChatTurn[] Find(System.Collections.Generic.IEnumerable<ChatTurn> turns, string words)
    {
        var wanted = words.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return turns.Where(t => wanted.All(w => t.User.Contains(w, StringComparison.OrdinalIgnoreCase) || t.Assistant.Contains(w, StringComparison.OrdinalIgnoreCase) || t.CharacterName.Contains(w, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
    public static string Text(System.Collections.Generic.IEnumerable<ChatTurn> turns, bool markdown, string userName)
    {
        var text = new System.Text.StringBuilder(markdown ? "# Keyboard Worker 대화 기록\r\n\r\n" : "Keyboard Worker 대화 기록\r\n\r\n");
        foreach (var turn in turns)
        {
            string who = turn.CharacterName.Length > 0 ? turn.CharacterName : "캐릭터", at = turn.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            if (markdown) text.Append("### ").Append(at).Append(" · ").Append(turn.Mode).Append("\r\n\r\n**").Append(userName).Append("**: ").Append(turn.User.Replace("\n", "\r\n")).Append("\r\n\r\n**").Append(who).Append("**: ").Append(turn.Assistant.Replace("\n", "\r\n")).Append("\r\n\r\n");
            else text.Append('[').Append(at).Append("]\r\n").Append(userName).Append(": ").Append(turn.User.Replace("\n", "\r\n")).Append("\r\n").Append(who).Append(": ").Append(turn.Assistant.Replace("\n", "\r\n")).Append("\r\n\r\n");
        }
        return text.ToString();
    }
    ChatTurn[] Found() => Find(buddy.Chat.Turns, search.Text);
    void Export(ChatTurn[] turns)
    {
        if (turns.Length == 0) { status.Text = "내보낼 대화가 없어요."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "텍스트 (*.txt)|*.txt|Markdown (*.md)|*.md", FileName = "대화-" + DateTime.Now.ToString("yyyyMMdd") }; if (dialog.ShowDialog(this) != true) return;
        string user = string.IsNullOrWhiteSpace(buddy.Chat.Preferences.Profile.UserName) ? "나" : buddy.Chat.Preferences.Profile.UserName;
        try { System.IO.File.WriteAllText(dialog.FileName, Text(turns, dialog.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase), user), new System.Text.UTF8Encoding(true)); status.Text = turns.Length + "개 대화를 내보냈어요 · " + System.IO.Path.GetFileName(dialog.FileName); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { status.Text = "저장하지 못했습니다. " + e.Message; }
    }
    void Refresh()
    {
        list.Children.Clear(); var found = Found().Reverse().ToArray(); int total = buddy.Chat.Turns.Count;
        count.Text = search.Text.Trim().Length == 0 ? $"보관 중인 대화 {total}개 (최근 것부터)" : $"찾은 대화 {found.Length}개 / 전체 {total}개";
        if (found.Length == 0) { list.Children.Add(Pastel.Text(total == 0 ? "아직 보관된 대화가 없어요." : "맞는 대화가 없어요. 다른 낱말로 찾아보세요.", 13)); return; }
        foreach (var turn in found.Take(100))
        {
            var entry = new StackPanel(); entry.Children.Add(new TextBlock { Text = $"♡ {turn.At.ToLocalTime():yyyy-MM-dd HH:mm} · {(turn.CharacterName.Length > 0 ? turn.CharacterName : "캐릭터")} · {turn.Mode}", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 12 });
            TextBox Line(string text) => new() { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Pastel.Ink, FontSize = 13, Padding = new Thickness(0), Margin = new Thickness(0, 3, 0, 0), MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            entry.Children.Add(Line("나: " + turn.User)); entry.Children.Add(Line(turn.Assistant));
            var card = Pastel.Card(entry); card.Padding = new Thickness(12, 8, 12, 8); card.Margin = new Thickness(0, 0, 4, 6); list.Children.Add(card);
        }
        if (found.Length > 100) list.Children.Add(Pastel.Text("최근 100개만 보여줍니다. 낱말을 더 적어 좁히거나 파일로 내보내 보세요.", 12));
    }
}
