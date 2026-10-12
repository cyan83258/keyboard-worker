using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardWorker;

// The place for everything that is done or looked at rather than configured: today's progress, the friends,
// what has been collected, and the shop. Opened by double-clicking the pet. Settings live in SettingsWindow.
public sealed class HomeWindow : Window
{
    readonly Buddy buddy; readonly TabControl tabs = new() { Background = Pastel.Paper, BorderBrush = Pastel.Pink };
    readonly StackPanel today = new(), friends = new(), kept = new(), shop = new();
    readonly TextBox todo = new() { MaxLength = 120 }; readonly TextBlock status = Pastel.Text("", 12), coins = Pastel.Text("", 13); bool queued;
    public void ShowPage(int index) => tabs.SelectedIndex = index;
    public int Pages => tabs.Items.Count;
    public HomeWindow(Buddy buddy)
    {
        this.buddy = buddy; var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons);
        void Foot(string text, Action action, bool blue = true) { var button = Pastel.Button(text, action, blue); button.Margin = new Thickness(6, 0, 0, 0); buttons.Children.Add(button); }
        Foot("설정", buddy.OpenSettings); Foot("사용 설명서", buddy.OpenGuide); Foot("닫기", Close, false);
        coins.FontWeight = FontWeights.Bold; coins.VerticalAlignment = VerticalAlignment.Center; status.VerticalAlignment = VerticalAlignment.Center; status.Margin = new Thickness(14, 4, 0, 8);
        var left = new StackPanel { Orientation = Orientation.Horizontal }; left.Children.Add(coins); left.Children.Add(status); footer.Children.Add(left);
        root.Children.Add(tabs);
        void Tab(string title, StackPanel content) { content.Margin = new Thickness(16); tabs.Items.Add(new TabItem { Header = title, Content = Pastel.Scroll(content), Padding = new Thickness(18, 8, 18, 8) }); }
        Tab("오늘", today); Tab("친구", friends); Tab("수집", kept); Tab("상점", shop);
        todo.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { buddy.SetTodo(todo.Text); status.Text = "메모를 적어 뒀어요."; } };
        buddy.Progress.Changed += Changed; buddy.Game.Changed += Changed; buddy.Adventure.Changed += Changed; buddy.Collection.Changed += Changed; buddy.PartyChanged += Changed;
        Closed += (_, _) => { buddy.Progress.Changed -= Changed; buddy.Game.Changed -= Changed; buddy.Adventure.Changed -= Changed; buddy.Collection.Changed -= Changed; buddy.PartyChanged -= Changed; };
        Activated += (_, _) => Refresh(); tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, tabs)) Refresh(); };
        Pastel.Window(this, "홈", root, 780, 820); Refresh();
    }
    void Changed() { if (queued) return; queued = true; Dispatcher.BeginInvoke(() => { queued = false; if (IsLoaded) Refresh(); }); }

    // ---- building blocks ----
    static TextBlock Line(string text, double size = 13, bool bold = false) => new() { Text = text, Foreground = Pastel.Ink, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    static Button Small(string text, Action action, bool blue = true) { var button = Pastel.Button(text, action, blue); button.Padding = new Thickness(11, 4, 11, 4); button.Margin = new Thickness(0, 6, 6, 0); button.FontSize = 12; return button; }
    // A titled card; the returned panel takes the content, and buttons go in a row underneath.
    static StackPanel Box(Panel parent, string title, bool blue = false, params (string Text, Action Open)[] actions)
    {
        var outer = new StackPanel(); outer.Children.Add(new TextBlock { Text = title, Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 0, 0, 2) });
        var body = new StackPanel(); outer.Children.Add(body);
        if (actions.Length > 0) { var row = new WrapPanel(); foreach (var (text, open) in actions) row.Children.Add(Small(text, open)); outer.Children.Add(row); }
        var card = Pastel.Card(outer, blue); card.Padding = new Thickness(14, 10, 14, 12); card.Margin = new Thickness(0, 0, 4, 8); parent.Children.Add(card); return body;
    }
    static Grid Columns(Panel parent) { var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition()); parent.Children.Add(grid); return grid; }
    static StackPanel Column(Grid grid, int index) { var panel = new StackPanel { Margin = new Thickness(index == 0 ? 0 : 4, 0, index == 0 ? 4 : 0, 0) }; Grid.SetColumn(panel, index); grid.Children.Add(panel); return panel; }

    void Refresh()
    {
        coins.Text = $"● {buddy.Game.State.Coins:N0}코인 · Lv.{buddy.Game.Level}";
        // Only the tab on screen is built; the others are built when they are opened. Nothing is rebuilt while minimized.
        if (WindowState == WindowState.Minimized) return;
        switch (tabs.SelectedIndex) { case 0: Today(); break; case 1: Friends(); break; case 2: Kept(); break; case 3: Shop(); break; }
    }

    // ---- today ----
    void Today()
    {
        today.Children.Clear(); var state = buddy.Progress.State; var now = DateTimeOffset.Now; string day = DateTime.Now.ToString("yyyy-MM-dd");
        var top = Columns(today); var one = Column(top, 0); var two = Column(top, 1);
        var attend = Box(one, "출석", true, ("자세히", () => buddy.OpenProgress(0)));
        attend.Children.Add(Line(state.LastDay == day ? $"오늘 출석 완료 ♡ 연속 {state.Streak}일" : "오늘은 아직 출석 전이에요", 14, true));
        attend.Children.Add(Line($"최고 연속 {state.BestStreak}일 · 지금까지 {state.TotalDays}일 · 이번 주 {state.WeekDays}일"));
        var work = Box(two, "오늘 작업", false, ("타이머", buddy.OpenTimers), ("통계", buddy.OpenStats), ("휴식", buddy.OpenRest));
        work.Children.Add(Line(TypingWorkClock.Format(buddy.Work.TodayMs), 14, true));
        work.Children.Add(Line(buddy.RestSeconds > 0 ? $"휴식 중 · {buddy.RestSeconds / 60}분 {buddy.RestSeconds % 60}초 남음" : buddy.IsQuiet ? "조용한 작업 모드" : "함께 작업하는 중"));

        var memo = Box(today, "할 일 한 줄 메모");
        if (todo.Parent is Panel old) old.Children.Remove(todo);
        var row = new DockPanel(); var memoButtons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(memoButtons, Dock.Right);
        var write = Small("적기", () => { buddy.SetTodo(todo.Text); status.Text = buddy.Pref.Todo.Length > 0 ? "메모를 적어 뒀어요." : "메모를 비웠어요."; }, false); write.Margin = new Thickness(6, 0, 0, 0);
        var finish = Small("완료 ✓", () => { if (buddy.Pref.Todo.Length == 0) { status.Text = "완료할 메모가 없어요."; return; } buddy.CompleteTodo(); todo.Text = ""; status.Text = "해냈어요 ♡"; }); finish.Margin = new Thickness(6, 0, 0, 0);
        memoButtons.Children.Add(write); memoButtons.Children.Add(finish); row.Children.Add(memoButtons); row.Children.Add(todo); memo.Children.Add(row);
        if (!todo.IsKeyboardFocused) todo.Text = buddy.Pref.Todo;

        var goals = Box(today, "이번 주 목표", false, ("목표 만들기 · 고치기", () => buddy.OpenProgress(1)));
        if (state.Goals.Count == 0) goals.Children.Add(Line("아직 목표가 없어요. 이번 주에 해 보고 싶은 것을 적어 보세요."));
        foreach (var goal in state.Goals.ToArray())
        {
            var kind = GoalKinds.Find(goal.Kind); var head = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            if (goal.Kind == "manual" && !goal.Done) { var plus = Small("+1", () => { buddy.TickGoal(goal.Id); buddy.Progress.Save(true); }, false); plus.Margin = new Thickness(6, 0, 0, 0); plus.Padding = new Thickness(8, 1, 8, 1); DockPanel.SetDock(plus, Dock.Right); head.Children.Add(plus); }
            var amount = Line($"{Math.Min(goal.Value, goal.Target)}/{goal.Target}{kind.Unit}", 12); amount.Margin = new Thickness(8, 0, 0, 0); amount.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(amount, Dock.Right); head.Children.Add(amount);
            head.Children.Add(new TextBlock { Text = (goal.Done ? "✓ " : "") + goal.Title, Foreground = Pastel.Ink, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            goals.Children.Add(head); goals.Children.Add(ProgressWindow.Bar(goal.Target == 0 ? 0 : (double)goal.Value / goal.Target, goal.Done));
        }

        var bottom = Columns(today); var three = Column(bottom, 0); var four = Column(bottom, 1);
        var mini = Box(three, "미니 이벤트", true, ("자세히", () => buddy.OpenProgress(2)));
        mini.Children.Add(Line(!state.Events ? "꺼져 있어요." : state.Event is { } running && now <= running.Ends ? running.Title + " 진행 중\n" + ProgressBook.Describe(running, now)
            : $"열린 이벤트가 없어요. 오늘 {(state.EventDay == day ? state.EventsToday : 0)}/{ProgressBook.EventsPerDay}번 · 다음까지 작업 약 {Math.Max(0, (state.NextEventMs - state.SinceEventMs) / 60000)}분"));
        var dates = Box(four, "다가오는 기념일", false, ("기념일 · 디데이", buddy.OpenAnniversaries));
        var date = DateTime.Now.Date; var items = buddy.Adventure.State.Anniversaries;
        int Order(Anniversary item) => Anniversaries.Day(item) is not DateTime at ? int.MaxValue : item.Yearly ? (Anniversaries.Next(at, date) - date).Days : at > date ? (at - date).Days : 100 - ((date - at).Days + 1) % 100;
        if (items.Count == 0) dates.Children.Add(Line("등록한 기념일이 없어요."));
        foreach (var item in items.OrderBy(Order).Take(3)) dates.Children.Add(Line(Anniversaries.DDay(item, date) + " · " + item.Name));

        var talk = Box(today, "이야기하기", false, ("한마디 보내기", buddy.OpenQuickChat), ("캐릭터 채팅", buddy.OpenChat), ("대화 검색 · 내보내기", buddy.OpenChatSearch), ("지난 대사 다시 보기", buddy.OpenLines));
        talk.Children.Add(Line($"보관 중인 대화 {buddy.Chat.Turns.Count}개 · 이번 실행에 나온 대사 {buddy.SpokenLines.Count}개"));
    }

    // ---- friends ----
    // A small label beside a name ("대표", "둘째").
    static Border Chip(string text) => new() { Child = new TextBlock { Text = text, Foreground = Pastel.Ink, FontSize = 11, FontWeight = FontWeights.Bold }, Background = Pastel.Paper, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), VerticalAlignment = VerticalAlignment.Center };
    static TextBlock Heading(string text) => new() { Text = text, Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(2, 6, 0, 6) };
    // How close, at a glance: hearts, what the closeness is called, and the number small at the right. The rest is on hover.
    static DockPanel Closeness(int bond, string name, string detail)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 2), ToolTip = detail, Background = Brushes.Transparent };
        var number = new TextBlock { Text = bond + " / 1000", Foreground = Pastel.Ink, Opacity = .6, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(number, Dock.Right); row.Children.Add(number);
        var hearts = new HeartMeter(17) { Bond = bond, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(hearts, Dock.Left); row.Children.Add(hearts);
        row.Children.Add(new TextBlock { Text = name, Foreground = Pastel.Ink, FontWeight = FontWeights.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        return row;
    }
    // A card with a name, a chip beside it, and something at the right end of its top line.
    static StackPanel Person(Panel parent, string name, string chip, bool blue, UIElement? corner = null)
    {
        var outer = new StackPanel(); var head = new DockPanel();
        if (corner != null) { DockPanel.SetDock(corner, Dock.Right); head.Children.Add(corner); }
        var title = new TextBlock { Text = name, Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(title, Dock.Left); head.Children.Add(title); if (chip.Length > 0) { var label = Chip(chip); label.HorizontalAlignment = HorizontalAlignment.Left; head.Children.Add(label); } else head.Children.Add(new Border());
        outer.Children.Add(head); var card = Pastel.Card(outer, blue); card.Padding = new Thickness(16, 12, 16, 14); card.Margin = new Thickness(0, 0, 4, 8); parent.Children.Add(card); return outer;
    }
    static WrapPanel Buttons(params (string Text, Action Open, bool Main)[] actions) { var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) }; foreach (var (text, open, main) in actions) row.Children.Add(Small(text, open, !main)); return row; }
    void Friends()
    {
        friends.Children.Clear(); var book = buddy.Adventure; var second = buddy.SecondaryPreset; bool out2 = buddy.DisplayedCompanion != null;
        var people = new List<(string Id, string Name, string Role)> { (buddy.CharacterId, buddy.Chat.Preferences.Profile.Name, "대표") };
        if (second != null) people.Add((second.Id, second.Profile.Name, out2 ? "둘째" : "둘째 · 넣어 둠"));

        // Each friend: who, how close, what was done today - and the two ways to talk.
        friends.Children.Add(Heading("내 친구")); var grid = Columns(friends);
        for (int i = 0; i < people.Count; i++)
        {
            var (id, name, role) = people[i]; var friend = book.Friend(id, name); int stage = Affection.Stage(friend.Bond);
            var card = Person(Column(grid, i), name, role, i == 0);
            card.Children.Add(Closeness(friend.Bond, Affection.Name(stage), $"{stage}단계 · " + (Affection.ToNext(friend.Bond) is int left ? $"다음 단계까지 {left}" : "최고 단계") + $"\n처음 만난 날 {friend.Met} · 함께 작업 {TypingWorkClock.Format(friend.WorkMs)}\n좋아하는 선물: {(friend.Discovered ? friend.Favorite : "아직 몰라요")}"));
            card.Children.Add(Line($"오늘  쓰다듬기 {friend.Pats}/10  ·  선물 {friend.Gifts}/{GiftShop.PerDay}", 12));
            card.Children.Add(Buttons(("마주 보기", () => buddy.OpenCharacter(id), true), ("문자", () => buddy.OpenMessenger(id), false)));
        }
        if (second == null)
        {
            var alone = Person(friends, "두 번째 친구", "", false);
            alone.Children.Add(Line("프리셋 하나를 둘째 친구로 불러내면 둘이 대사를 주고받고, 둘만의 관계·장면·앨범이 생겨요.", 12)); alone.Children.Add(Buttons(("친구 배치 열기", buddy.OpenDuo, true)));
        }
        else
        {
            // The two of them: how they get on, and everything that belongs to the pair.
            friends.Children.Add(Heading("둘 사이"));
            var pair = book.Pair(buddy.CharacterId, second.Id); string day = DateTime.Now.ToString("yyyy-MM-dd");
            string sulking = pair.Mood.Sulking.Length > 0 && pair.Mood.SulkingDay == day ? (pair.Mood.Sulking == buddy.CharacterId ? people[0].Name : second.Profile.Name) : "";
            var call = Small(out2 ? "둘째 넣어 두기" : "둘째 불러내기", () => { buddy.SetCompanion(!out2); }); call.Margin = new Thickness(0); call.ToolTip = "둘째 친구를 바탕화면에 불러내거나 넣어 둬요";
            var card = Person(friends, people[0].Name + " ♡ " + second.Profile.Name, "", true, call); var last = pair.Events.LastOrDefault();
            card.Children.Add(Closeness(pair.Bond, pair.Style, $"둘이 나눈 대화 {pair.Conversations}회 · 함께 작업 {TypingWorkClock.Format(pair.WorkMs)}" + (last != null ? $"\n최근 기록 · {last.At:MM-dd} {last.Title}" : "") + (pair.Moods ? "" : "\n질투·화해는 꺼져 있어요.")));
            if (sulking.Length > 0 && pair.Moods) card.Children.Add(Line("지금 " + sulking + "이(가) 살짝 토라져 있어요. 말을 걸거나 쓰다듬어 주세요.", 12));
            card.Children.Add(Buttons(("둘만의 페이지", buddy.OpenPairPage, true), ("관계 설정", () => Pair(0), false), ("둘만의 장면", () => Pair(1), false), ("앨범", () => Pair(2), false)));
        }
        var others = buddy.StageCharacters().Where(c => people.All(p => p.Id != c.Id)).ToArray();
        if (others.Length > 0)
        {
            friends.Children.Add(Heading("다른 캐릭터")); var row = new WrapPanel { Margin = new Thickness(0, -6, 0, 8) };
            foreach (var other in others) { var chosen = other; var open = Small(other.Title, () => buddy.OpenCharacter(chosen.Id)); open.ToolTip = "저장해 둔 프리셋의 캐릭터와 마주 보고 이야기해요"; row.Children.Add(open); }
            friends.Children.Add(row);
        }
        friends.Children.Add(Heading("이야기 퀘스트"));
        var quests = book.State.Quests.Where(q => q.StoryId.Length == 0).ToArray();
        var story = Person(friends, quests.Length == 0 ? "진행 중인 퀘스트 없음" : quests.Length == 1 ? quests[0].Title : $"진행 중 {quests.Length}개", "", false);
        if (quests.Length == 0) story.Children.Add(Line("AI를 연결하면 작업 시간으로 완성하는 작은 이야기를 받을 수 있어요.", 12));
        foreach (var quest in quests)
        {
            var head = new DockPanel { Margin = new Thickness(0, 6, 0, 0) }; var amount = new TextBlock { Text = quest.Ready ? "완료 이야기 대기" : $"{quest.ProgressMs / 60000} / {quest.Minutes}분", Foreground = Pastel.Ink, Opacity = .6, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(amount, Dock.Right); head.Children.Add(amount); head.Children.Add(new TextBlock { Text = quests.Length == 1 ? "작업하는 동안 조금씩 채워져요" : quest.Title, Foreground = Pastel.Ink, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            story.Children.Add(head); story.Children.Add(ProgressWindow.Bar(quest.ProgressMs / (quest.Minutes * 60000.0), quest.Ready));
        }
        story.Children.Add(Buttons(("퀘스트 보기", () => buddy.OpenAdventure(2), true), ("이야기책", () => buddy.OpenAdventure(3), false)));
    }
    void Pair(int page) { buddy.OpenPair(); buddy.ShowPairPage(page); }

    // ---- collected ----
    void Kept()
    {
        kept.Children.Clear(); var state = buddy.Progress.State; var grid = Columns(kept); var one = Column(grid, 0); var two = Column(grid, 1);
        var lines = Box(one, $"대사 도감 · {buddy.Collection.Items.Count}개", true, ("도감 열기", buddy.OpenCollection), ("지난 대사에서 담기", buddy.OpenLines));
        if (buddy.Collection.Items.Count == 0) lines.Children.Add(Line("말풍선의 ♡를 누르면 그 대사가 여기에 모여요."));
        foreach (var line in buddy.Collection.Items.AsEnumerable().Reverse().Take(3)) lines.Children.Add(Line("♥ " + line.Speaker + ": " + (line.Text.Length > 60 ? line.Text[..60] + "…" : line.Text), 12));
        int reached = ProgressBook.All.Count(a => state.Achievements.ContainsKey(a.Id));
        var medals = Box(two, $"업적 · {reached}/{ProgressBook.All.Length}", false, ("업적 보기", () => buddy.OpenProgress(3)));
        medals.Children.Add(ProgressWindow.Bar((double)reached / ProgressBook.All.Length, reached == ProgressBook.All.Length));
        if (reached == 0) medals.Children.Add(Line("함께 지내다 보면 하나씩 채워져요."));
        foreach (var item in ProgressBook.All.Where(a => state.Achievements.ContainsKey(a.Id)).OrderByDescending(a => state.Achievements[a.Id]).Take(3)) medals.Children.Add(Line("★ " + item.Title + " · " + state.Achievements[item.Id], 12));
        var stories = buddy.Adventure.State.Stories;
        var library = Box(one, $"이야기책 · {stories.Count}편", false, ("이야기책 열기", () => buddy.OpenAdventure(3)));
        if (stories.Count == 0) library.Children.Add(Line("완료한 이야기 퀘스트가 여기에 쌓여요."));
        foreach (var story in stories.AsEnumerable().Reverse().Take(3)) library.Children.Add(Line((story.Favorite ? "♡ " : "· ") + story.Title, 12));
        var second = buddy.SecondaryPreset; var events = second == null ? new List<PairEvent>() : buddy.Adventure.Pair(buddy.CharacterId, second.Id).Events;
        var album = Box(two, $"둘의 앨범 · {events.Count}건", true, ("앨범 열기", () => Pair(2)));
        if (events.Count == 0) album.Children.Add(Line(second == null ? "둘째 친구가 생기면 둘의 기록이 쌓여요." : "둘이 이야기를 나누면 첫 장이 채워져요."));
        foreach (var item in events.AsEnumerable().Reverse().Take(3)) album.Children.Add(Line($"· {item.At:MM-dd} {item.Title}", 12));
        var owned = buddy.Game.State.Owned.Count; var materials = buddy.Adventure.State.Materials;
        var things = Box(kept, "모은 것들", false, ("옷장 열기", buddy.OpenGame));
        things.Children.Add(Line($"장식 아이템 {owned}/{buddy.Game.Catalog.Length}개 · 대사 팩 {LinePacks.All.Count(p => buddy.OwnsPack(p))}/{LinePacks.All.Length}개 · 기념일 {buddy.Adventure.State.Anniversaries.Count}개"
            + (materials.Count > 0 ? "\n희귀 재료: " + string.Join(", ", materials.Select(m => m.Key + " ×" + m.Value)) : "")));
    }

    // ---- the shop: every place coins are spent ----
    void Shop()
    {
        shop.Children.Clear();
        void Try(Action buy, string done) { try { buy(); status.Text = done; } catch (Exception e) { status.Text = e.Message; } }
        var items = Box(shop, "장식 아이템", true, ("장식 상점 · 옷장 열기", buddy.OpenGame));
        items.Children.Add(Line($"가진 것 {buddy.Game.State.Owned.Count}/{buddy.Game.Catalog.Length}개")); items.Children.Add(Pastel.Text("리본, 안경, 오라 같은 장식을 코인으로 사서 캐릭터에게 입혀요. 직접 만든 장비는 무료로 등록할 수 있어요.", 12));
        var bagged = buddy.Adventure.State.GiftBag;
        var gifts = Box(shop, "선물 뽑기", false, ($"뽑으러 가기 · 1번 {GiftShop.DrawCost}코인", () => buddy.OpenGifts(0)), ("내 선물 가방", () => buddy.OpenGifts(1)), ("선물 도감", () => buddy.OpenGifts(2)));
        gifts.Children.Add(Pastel.Text("코인으로 선물을 뽑아 가방에 모았다가 캐릭터 페이지에서 건네요. ★부터 ★★★★★까지, 귀할수록 친밀도가 많이 올라요.", 12)); gifts.Children.Add(Line($"가방에 {bagged.Values.Sum()}개 · 만난 선물 {buddy.Adventure.State.GiftsMet.Count}/{GiftShop.All.Length}종"));

        var packs = Box(shop, "대사 팩", false, ("대사집 열기 (산 팩 넣기)", buddy.OpenLineBook));
        packs.Children.Add(Pastel.Text("한 가지 말투로 묶은 대사 모음이에요. 한 번 사면 어느 캐릭터의 대사집에든 넣고 고칠 수 있어요.", 12));
        foreach (var pack in LinePacks.All)
        {
            bool owned = buddy.OwnsPack(pack); var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var action = owned ? Small("보유 중", () => { }) : Small(pack.Price + "코인", () => Try(() => buddy.Buy(LinePacks.Key(pack.Id), pack.Price), "'" + pack.Name + "'을(를) 샀어요 ♡ 대사집의 '대사 팩'에서 넣을 수 있어요."), false);
            action.IsEnabled = !owned; action.Margin = new Thickness(8, 0, 0, 0); action.MinWidth = 76; action.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(action, Dock.Right); row.Children.Add(action);
            var words = new StackPanel(); words.Children.Add(Line((owned ? "✓ " : "") + pack.Name + " · " + pack.Lines.Sum(l => l.Value.Split('\n').Length) + "줄", 13, true));
            words.Children.Add(Line(pack.Description + "  예) " + pack.Lines.Values.First().Split('\n')[0], 12)); row.Children.Add(words); packs.Children.Add(row);
        }

        var second = buddy.SecondaryPreset; var scenes = Box(shop, "2인 장면 자리", true, ("그림 등록하러 가기", () => Pair(1)));
        scenes.Children.Add(Line("둘이 함께 있는 그림을 걸어 둘 자리예요. '" + PairScenes.Title(PairScenes.Free) + "'는 처음부터 열려 있어요.", 12));
        if (second == null) scenes.Children.Add(Line("둘째 친구를 정하면 열 수 있어요."));
        else
        {
            var pair = buddy.Adventure.Pair(buddy.CharacterId, second.Id);
            foreach (var (key, title, help) in PairScenes.Slots)
            {
                bool has = pair.Scenes.TryGetValue(key, out var path) && File.Exists(path), open = key == PairScenes.Free || has || buddy.Progress.IsUnlocked(PairScenes.Unlock(key)); var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var action = open ? Small(has ? "그림 있음" : "열림", () => { }) : Small(PairScenes.Price + "코인", () => Try(() => buddy.Buy(PairScenes.Unlock(key), PairScenes.Price), "'" + title + "' 자리를 열었어요 ♡"), false);
                action.IsEnabled = !open; action.Margin = new Thickness(8, 0, 0, 0); action.MinWidth = 76; action.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(action, Dock.Right); row.Children.Add(action);
                var words = new StackPanel(); words.Children.Add(Line((open ? "" : "🔒 ") + title, 13, true)); words.Children.Add(Line(help, 12)); row.Children.Add(words); scenes.Children.Add(row);
            }
        }
        var earn = Box(shop, "코인 모으는 법");
        earn.Children.Add(Line("키보드 작업 1분마다 5코인 · 하루 목표 보상 · 출석 · 주간 목표 · 미니 이벤트 · 업적", 12));
    }
}
