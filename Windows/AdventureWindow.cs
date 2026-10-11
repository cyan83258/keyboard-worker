using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace KeyboardWorker;

public sealed class AdventureWindow : Window
{
    sealed record FriendChoice(string Id, string Name);
    readonly Buddy buddy; readonly AdventureBook book;
    readonly ListBox stories = new() { DisplayMemberPath = "Title", Height = 170, Background = Pastel.Paper, Foreground = Pastel.Ink, BorderBrush = Pastel.Pink };
    readonly TextBox storyText = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock status = Pastel.Text(""), friendInfo = Pastel.Text(""), pairInfo = Pastel.Text(""), questInfo = Pastel.Text(""), collection = Pastel.Text("");
    readonly ComboBox friend = new() { DisplayMemberPath = "Name" };
    public AdventureWindow(Buddy buddy)
    {
        this.buddy = buddy;
        book = buddy.Adventure;
        var root = new DockPanel { Margin = new Thickness(18) };
        var footer = new StackPanel();
        footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var tabs = new TabControl { Background = Pastel.Paper, BorderBrush = Pastel.Pink }; root.Children.Add(tabs); pages = tabs;
        StackPanel Tab(string title) { var p = new StackPanel { Margin = new Thickness(16) }; tabs.Items.Add(new TabItem { Header = title, Content = Pastel.Scroll(p), Padding = new Thickness(12, 8, 12, 8) }); return p; }
        var friends = Tab("친구 카드"); friends.Children.Add(Pastel.Text("마음을 모으는 작은 선물 ♡", 23));
        var choices = buddy.Presets.Items.Select(p => new FriendChoice(p.Id, p.Profile.Name)).ToList();
        if (!choices.Any(c => c.Id == buddy.CharacterId)) choices.Insert(0, new(buddy.CharacterId, buddy.Chat.Preferences.Profile.Name));
        friend.ItemsSource = choices; friend.SelectedItem = choices.FirstOrDefault(c => c.Id == buddy.CharacterId); friends.Children.Add(friend); friends.Children.Add(Pastel.Card(friendInfo, true));
        var gifts = new ComboBox { ItemsSource = AdventureBook.Gifts, SelectedIndex = 0 }; friends.Children.Add(Pastel.Text("선물 1개 · 10코인 · 캐릭터별 하루 3개")); friends.Children.Add(gifts);
        friends.Children.Add(Pastel.Button("선물 전하기 ♡", () => Run(() =>
        {
            var selected = friend.SelectedItem as FriendChoice ?? throw new InvalidOperationException("친구를 선택해 주세요.");
            string gift = gifts.SelectedItem.ToString()!; bool loved = gift == book.Friend(selected.Id, selected.Name).Favorite;
            string result = book.Gift(selected.Id, selected.Name, gift, buddy.Game);
            buddy.NoteGift(); if (!buddy.Cared(selected.Id)) buddy.GiftReaction(selected.Id, gift, loved, result);
            status.Text = result;
        })));
        var favorite = new ComboBox { ItemsSource = AdventureBook.Gifts }; friends.Children.Add(Pastel.Text("선물 취향 직접 설정 (선택)")); friends.Children.Add(favorite);
        friends.Children.Add(Pastel.Button("취향 저장", () => Run(() =>
        {
            if (favorite.SelectedItem == null) return;
            var selected = friend.SelectedItem as FriendChoice ?? throw new InvalidOperationException("친구를 선택해 주세요.");
            var card = book.Friend(selected.Id, selected.Name);
            card.Favorite = favorite.SelectedItem.ToString()!;
            card.Discovered = true;
            book.Save(true);
            Refresh();
        }), true));
        friends.Children.Add(Pastel.Text("좋아하는 선물은 +12, 다른 선물은 +5 친밀도. 쓰다듬기·확인된 작업 시간으로도 각 친구의 친밀도가 쌓입니다. 쉬거나 접속하지 않아도 감소하지 않습니다.", 12));
        var pair = Tab("두 친구 관계"); pair.Children.Add(Pastel.Text("같이 쌓은 시간은 둘만의 이야기 ♡", 22)); pair.Children.Add(Pastel.Card(pairInfo, true));
        var style = new ComboBox { ItemsSource = new[] { "친근한 친구", "티격태격 라이벌", "다정한 보호자와 장난꾸러기", "조용히 서로를 챙기는 사이" }, SelectedIndex = 0 }; pair.Children.Add(style);
        pair.Children.Add(Pastel.Button("관계 설명 · 호칭 · 둘만의 장면 · 앨범 열기", buddy.OpenPair, true));
        pair.Children.Add(Pastel.Button("관계 분위기 저장", () => Run(() =>
        {
            if (buddy.SecondaryPreset == null) throw new InvalidOperationException("둘째 친구를 선택해 주세요.");
            book.Pair(buddy.CharacterId, buddy.SecondaryPreset.Id).Style = style.SelectedItem.ToString()!;
            book.Save(true);
            Refresh();
        }), true));
        var quests = Tab("이야기 퀘스트"); quests.Children.Add(Pastel.Text("오늘의 작은 모험", 23)); quests.Children.Add(Pastel.Card(questInfo, true));
        quests.Children.Add(Pastel.Button("AI 퀘스트 / 완료 이야기 생성", async () => { try
        {
            IsEnabled = false;
            await buddy.GenerateAdventure(true);
            status.Text = "이야기 작업 완료 ♡";
            Refresh();
        } catch (Exception e) { status.Text = e.Message; } finally { IsEnabled = true; } }));
        quests.Children.Add(Pastel.Text("목표는 실제 키보드 활동 시간입니다. 완료하면 AI가 모험의 이야기를 써 줍니다. 텍스트 이야기 중심이며 재료는 5%, 장식은 3% 확률로만 얻습니다. 자동 생성은 설정 탭에서 켜세요.", 12));
        var library = Tab("이야기책·도감"); library.Children.Add(Pastel.Text("우리의 이야기책 ♡", 23)); library.Children.Add(stories); library.Children.Add(storyText);
        var actions = new WrapPanel(); actions.Children.Add(Pastel.Button("♡ 보관 표시", () => Run(() => { if (stories.SelectedItem is StoryEntry s) book.Favorite(s.Id); }), true));
        actions.Children.Add(Pastel.Button("파일로 저장", () =>
        {
            if (stories.SelectedItem is not StoryEntry s) return;
            var dialog = new SaveFileDialog { Filter = "이야기 Markdown|*.md|텍스트|*.txt", FileName = "이야기-" + s.At.ToString("yyyyMMdd") };
            if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, "# " + s.Title + "\n\n" + s.Body + "\n\n" + s.Loot);
        }));
        actions.Children.Add(Pastel.Button("이야기 삭제", () => Run(() => { if (stories.SelectedItem is StoryEntry s && MessageBox.Show(this, "이 이야기를 삭제할까요?", "이야기책", MessageBoxButton.YesNo) == MessageBoxResult.Yes) book.DeleteStory(s.Id); })));
        library.Children.Add(actions);
        library.Children.Add(Pastel.Card(collection, true));
        var settings = Tab("자동 생성 설정"); var enabled = new CheckBox { Content = "AI가 퀘스트·완료 이야기를 자동 생성", IsChecked = book.State.Options.Enabled }; settings.Children.Add(enabled);
        TextBox Number(string label, int value) { settings.Children.Add(Pastel.Text(label)); var box = new TextBox { Text = value.ToString() }; settings.Children.Add(box); return box; }
        var keep = Number("이야기 보관 수 (1~300)", book.State.Options.KeepStories);
        var interval = Number("자동 생성 최소 간격 (5~1440분)", book.State.Options.MinAiMinutes);
        var tokens = Number("이야기 출력 한도 (1000~8000토큰)", book.State.Options.OutputTokens);
        settings.Children.Add(Pastel.Text("이야기 분위기·작성 지시"));
        var prompt = new TextBox { Text = book.State.Options.Prompt, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 160, MaxLength = 10000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        settings.Children.Add(prompt);
        settings.Children.Add(Pastel.Text("AI 사용량이 발생하며 하루 AI 상한·기존 자동 호출 간격을 지킵니다. 오래된 일반 이야기는 보관 수에 맞춰 정리하며 ♡ 표시한 이야기는 보호합니다. 자동 생성 중에는 직접 채팅과 휴식을 우선합니다.", 12));
        settings.Children.Add(Pastel.Button("설정 저장", () => Run(() =>
        {
            int Value(TextBox b, int min, int max) => int.TryParse(b.Text, out int n) && n >= min && n <= max ? n : throw new InvalidOperationException("숫자를 범위 안에 입력해 주세요.");
            book.SetOptions(new() { Enabled = enabled.IsChecked == true, KeepStories = Value(keep, 1, 300), MinAiMinutes = Value(interval, 5, 1440), OutputTokens = Value(tokens, 1000, 8000), Prompt = prompt.Text });
            status.Text = "저장했어요 ♡";
        })));
        friend.SelectionChanged += (_, _) => Refresh();
        stories.SelectionChanged += (_, _) =>
        {
            if (stories.SelectedItem is StoryEntry s) storyText.Text = s.Title + "\n" + s.At.ToString("yyyy-MM-dd HH:mm") + (s.Favorite ? " ♡ 보관" : "") + "\n\n" + s.Body + (s.Loot.Length > 0 ? "\n\n☆ " + s.Loot : "");
            else storyText.Clear();
        };
        book.Changed += Refresh; Closed += (_, _) => book.Changed -= Refresh; Pastel.Window(this, "친구와 작은 이야기책", root, 920, 830); Refresh();
    }
    readonly TabControl pages = null!;
    // 2 = story quests, 3 = storybook (opened on the newest story).
    public void ShowPage(int index) { pages.SelectedIndex = index; if (index == 3 && stories.Items.Count > 0) stories.SelectedIndex = 0; }
    void Run(Action action) { try { action(); Refresh(); } catch (Exception e) { status.Text = e.Message; } }
    public void CaptureTabs(string path)
    {
        var root = (DockPanel)((Grid)((Border)Content).Child).Children[1]; var tabs = root.Children.OfType<TabControl>().Single();
        for (int i = 0; i < tabs.Items.Count; i++) { tabs.SelectedIndex = i; if (i == 3 && stories.Items.Count > 0) stories.SelectedIndex = 0; UpdateLayout(); Pastel.Capture(this, Path.Combine(path, "ui-adventure-" + i + ".png")); }
    }
    void Refresh()
    {
        if (friend.SelectedItem is FriendChoice selected)
        {
            var f = book.Friend(selected.Id, selected.Name);
            friendInfo.Text = $"♡ {f.Name} · 친밀도 {f.Bond}/1000\n{Affection.Summary(f.Bond)} · 처음 만난 날 {f.Met}\n함께 작업 {TypingWorkClock.Format(f.WorkMs)}\n좋아하는 선물: {(f.Discovered ? f.Favorite : "아직 발견하지 못했어요")}\n오늘 쓰다듬기 {f.Pats}/10 · 선물 {f.Gifts}/3";
        }
        if (buddy.SecondaryPreset != null)
        {
            var p = book.Pair(buddy.CharacterId, buddy.SecondaryPreset.Id);
            pairInfo.Text = $"{buddy.Chat.Preferences.Profile.Name} ♡ {buddy.SecondaryPreset.Profile.Name}\n관계도 {p.Bond}/1000 · 상호작용 {p.Conversations}회\n함께 작업 {TypingWorkClock.Format(p.WorkMs)}\n{p.Style}";
        } else pairInfo.Text = "둘째 친구를 불러내면 두 친구의 관계를 볼 수 있어요.";
        questInfo.Text = book.State.Quests.Count == 0 ? "아직 퀘스트가 없어요. AI 생성 버튼이나 자동 생성 설정을 사용해 주세요." : string.Join("\n\n", book.State.Quests.TakeLast(5).Select(q => $"☆ {q.Title}\n{q.Premise}\n{q.ProgressMs / 60000.0:0.#}/{q.Minutes}분 · {(q.StoryId.Length > 0 ? "이야기 보관 완료" : q.Ready ? "완료 이야기 대기" : "모험 중")}"));
        string? selectedId = (stories.SelectedItem as StoryEntry)?.Id;
        stories.ItemsSource = book.State.Stories.AsEnumerable().Reverse().ToArray();
        stories.SelectedItem = book.State.Stories.FirstOrDefault(s => s.Id == selectedId);
        collection.Text = $"이야기 수집 {book.State.Stories.Count}/{book.State.Options.KeepStories}편\n희귀 재료: " + (book.State.Materials.Count == 0 ? "아직 없어요" : string.Join(", ", book.State.Materials.Select(p => p.Key + " ×" + p.Value))) + "\n희귀 장식은 기존 옷장에서 확인할 수 있어요.";
    }
}
