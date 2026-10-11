using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace KeyboardWorker;

// How the two pets relate: the relationship in the user's words, pictures of the two together, their album, and passing the pair on as one file.
public sealed class PairWindow : Window
{
    readonly Buddy buddy; readonly TabControl tabs = new() { Background = Pastel.Paper, BorderBrush = Pastel.Pink };
    readonly TextBlock status = Pastel.Text("", 12); readonly StackPanel scenes = new(), album = new(); readonly TextBlock mood = Pastel.Text("", 13);
    public void ShowPage(int index) => tabs.SelectedIndex = index;
    public int Pages => tabs.Items.Count;
    PairCard? Pair => buddy.SecondaryPreset is { } second ? buddy.Adventure.Pair(buddy.CharacterId, second.Id) : null;
    public PairWindow(Buddy buddy)
    {
        this.buddy = buddy; var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel(); footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(tabs);
        StackPanel Tab(string title) { var p = new StackPanel { Margin = new Thickness(18) }; tabs.Items.Add(new TabItem { Header = title, Content = Pastel.Scroll(p), Padding = new Thickness(14, 8, 14, 8) }); return p; }
        string first = buddy.Chat.Preferences.Profile.Name, second = buddy.SecondaryPreset?.Profile.Name ?? "둘째 친구"; var pair = Pair;

        var relation = Tab("관계 설정"); relation.Children.Add(Pastel.Text(first + " ♡ " + second, 22));
        if (pair == null) relation.Children.Add(Pastel.Text("둘째 친구가 정해져 있지 않아요. 먼저 '친구 배치 · 두 캐릭터'에서 둘째 친구를 골라 주세요.", 13));
        relation.Children.Add(Pastel.Text("여기에 적은 내용은 두 친구가 대사를 주고받거나 둘에게 함께 말을 걸 때 AI에게 전달됩니다.", 12));
        var style = new TextBox { Text = pair?.Style ?? "친근한 친구", MaxLength = 60 };
        var styles = new ComboBox { ItemsSource = new[] { "친근한 친구", "티격태격 라이벌", "다정한 보호자와 장난꾸러기", "조용히 서로를 챙기는 사이", "오래된 연인", "서로 좋아하지만 아직 말 못 한 사이", "주군과 충직한 벗" }, Margin = new Thickness(0, 4, 0, 0), ToolTip = "예시에서 고르기" };
        styles.SelectionChanged += (_, _) => { if (styles.SelectedItem is string chosen) style.Text = chosen; };
        TextBox Area(string label, string value, int height, int limit) { relation.Children.Add(Pastel.Text(label, 13)); var box = new TextBox { Text = value, AcceptsReturn = height > 40, TextWrapping = TextWrapping.Wrap, Height = height > 40 ? height : double.NaN, MaxLength = limit, VerticalScrollBarVisibility = height > 40 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled }; relation.Children.Add(box); return box; }
        relation.Children.Add(Pastel.Text("관계 분위기 (직접 적거나 아래 예시에서 고르기)", 13)); relation.Children.Add(style); relation.Children.Add(styles);
        var description = Area("둘의 관계 설명 · 함께 겪은 일, 서로를 대하는 태도", pair?.Description ?? "", 120, 4000);
        var firstCalls = Area(first + "이(가) " + second + "을(를) 부르는 말", pair?.FirstCalls ?? "", 0, 60);
        var secondCalls = Area(second + "이(가) " + first + "을(를) 부르는 말", pair?.SecondCalls ?? "", 0, 60);
        var rules = Area("꼭 지킬 것 · 하지 말아야 할 말이나 행동", pair?.Rules ?? "", 70, 2000);
        relation.Children.Add(Pastel.Text("한 번에 주고받는 대사 수 (AI 상호작용)", 13));
        var turns = new ComboBox { ItemsSource = new[] { "2마디 (한 번씩)", "3마디", "4마디" }, SelectedIndex = Math.Clamp(buddy.Pref.Duo.Turns, 2, 4) - 2 }; relation.Children.Add(turns);
        var moods = new CheckBox { Content = "질투·화해: 한 친구하고만 계속 이야기하면 다른 친구가 토라져요", IsChecked = pair?.Moods ?? true, Margin = new Thickness(0, 10, 0, 2) }; relation.Children.Add(moods);
        relation.Children.Add(Pastel.Text("한 친구에게만 " + PairMood.Limit + "번 연달아 말을 걸면 다른 친구가 토라집니다. 토라진 친구에게 말을 걸거나 쓰다듬거나 선물하면 풀려요. 하루에 한 번까지만 토라지고, 다음 날이면 잊어요. 친밀도가 깎이지는 않습니다.", 11));
        relation.Children.Add(Pastel.Card(mood, true));
        relation.Children.Add(Pastel.Button("관계 설정 저장", async () =>
        {
            var live = Pair; if (live == null) { status.Text = "둘째 친구를 먼저 골라 주세요."; return; }
            live.Style = string.IsNullOrWhiteSpace(style.Text) ? "친근한 친구" : style.Text.Trim()[..Math.Min(style.Text.Trim().Length, 60)];
            live.Description = description.Text.Trim(); live.FirstCalls = firstCalls.Text.Trim(); live.SecondCalls = secondCalls.Text.Trim(); live.Rules = rules.Text.Trim(); live.Moods = moods.IsChecked == true;
            if (!live.Moods) live.Mood = new();
            buddy.Adventure.Touch(); buddy.Pref.Duo = buddy.Pref.Duo with { Turns = turns.SelectedIndex + 2 }; await Storage.SaveAsync(buddy.Pref);
            status.Text = "관계 설정을 저장했어요 ♡ " + DateTime.Now.ToString("HH:mm:ss"); Refresh();
        }));

        var scenePage = Tab("둘만의 장면"); scenePage.Children.Add(Pastel.Text("둘이 함께 있는 그림 ♡", 22));
        scenePage.Children.Add(Pastel.Text("'둘이 이야기할 때' 자리는 처음부터 열려 있고, 나머지 자리는 코인 " + PairScenes.Price + "개로 한 번 열면 계속 쓸 수 있어요.", 12));
        scenePage.Children.Add(Pastel.Text("두 친구가 함께 그려진 그림(PNG / JPG)을 상황별로 등록하면, 그 순간에 캐릭터 위로 잠깐 떠올랐다 사라집니다. 같은 장면은 10분에 한 번, 가끔만 나와요. 둘째 친구가 화면에 나와 있을 때만 보여줍니다.", 12));
        scenePage.Children.Add(scenes);

        var albumPage = Tab("사건 기록"); albumPage.Children.Add(Pastel.Text("둘의 앨범 ♡", 22));
        albumPage.Children.Add(Pastel.Text("처음 나눈 대화, 관계도 달성, 기념일, 토라짐과 화해가 저절로 기록됩니다. 방금 둘이 주고받은 대화도 담아 둘 수 있어요.", 12));
        var albumActions = new WrapPanel();
        albumActions.Children.Add(Pastel.Button("방금 나눈 대화 담기", () => { status.Text = buddy.KeepLastExchange() ? "앨범에 담았어요 ♡" : "아직 둘이 주고받은 대화가 없어요."; Refresh(); }));
        albumActions.Children.Add(Pastel.Button("한 줄 직접 적기", () =>
        {
            var live = Pair; if (live == null) return; var box = new TextBox { MaxLength = 120, MinWidth = 320 };
            var dialog = new Window { Owner = this, ShowInTaskbar = false }; var body = new StackPanel { Margin = new Thickness(20) }; body.Children.Add(Pastel.Text("앨범에 남길 한 줄", 15)); body.Children.Add(box);
            body.Children.Add(Pastel.Button("남기기", () => { if (box.Text.Trim().Length > 0) { live.Record("메모", box.Text.Trim(), ""); buddy.Adventure.Touch(); } dialog.Close(); }));
            Pastel.Window(dialog, "앨범 메모", body, 440, 250); dialog.ShowDialog(); Refresh();
        }, true));
        albumActions.Children.Add(Pastel.Button("텍스트 파일로 내보내기", () =>
        {
            var live = Pair; if (live == null || live.Events.Count == 0) { status.Text = "내보낼 기록이 없어요."; return; }
            var dialog = new SaveFileDialog { Filter = "텍스트|*.txt", FileName = "둘의-앨범-" + DateTime.Now.ToString("yyyyMMdd") }; if (dialog.ShowDialog(this) != true) return;
            try { File.WriteAllText(dialog.FileName, string.Join("\r\n\r\n", live.Events.Select(e => $"[{e.At:yyyy-MM-dd HH:mm}] {e.Kind} · {e.Title}" + (e.Text.Length > 0 ? "\r\n" + e.Text.Replace("\n", "\r\n") : "")))); status.Text = "내보냈어요."; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "저장하지 못했습니다. " + e.Message; }
        }, true));
        albumPage.Children.Add(albumActions); albumPage.Children.Add(album);

        var share = Tab("커플 프리셋"); share.Children.Add(Pastel.Text("둘을 한 파일로 ♡", 22));
        share.Children.Add(Pastel.Text("두 캐릭터의 프리셋(이미지·프로필·대사·음성)과 관계 설정, 둘만의 장면 그림을 " + CouplePackage.Extension + " 파일 하나로 묶습니다. API 키·AI 연결·대화 기록·친밀도 수치·앨범은 들어가지 않아요.", 12));
        share.Children.Add(Pastel.Button("지금의 두 친구 내보내기…", async () =>
        {
            try
            {
                var live = Pair; var secondPreset = buddy.SecondaryPreset; if (live == null || secondPreset == null) throw new InvalidOperationException("둘째 친구를 먼저 골라 주세요.");
                var firstPreset = buddy.Presets.Items.FirstOrDefault(p => p.Id == buddy.Pref.ActivePresetId)?.Snapshot() ?? CharacterPreset.Capture(first, buddy.Pref, buddy.Chat.Preferences.Profile);
                var dialog = new SaveFileDialog { Filter = "Keyboard Worker 커플 프리셋|*" + CouplePackage.Extension, FileName = first + "+" + second + CouplePackage.Extension }; if (dialog.ShowDialog(this) != true) return;
                IsEnabled = false; status.Text = "내보내는 중…"; var card = live.Snapshot(); var other = secondPreset.Snapshot(); string target = dialog.FileName;
                await Task.Run(() => CouplePackage.Export(firstPreset, other, card, target)); status.Text = "내보냈어요 ♡ " + Path.GetFileName(target);
            }
            catch (Exception e) { status.Text = "내보내지 못했습니다. " + e.Message; }
            finally { IsEnabled = true; }
        }));
        share.Children.Add(Pastel.Button("커플 프리셋 가져오기…", async () =>
        {
            try
            {
                var dialog = new OpenFileDialog { Filter = "Keyboard Worker 커플 프리셋|*" + CouplePackage.Extension, CheckFileExists = true }; if (dialog.ShowDialog(this) != true) return;
                if (buddy.Presets.Items.Count > 48) throw new InvalidOperationException("프리셋 자리가 2개 필요합니다 (최대 50개). 쓰지 않는 프리셋을 먼저 정리해 주세요.");
                IsEnabled = false; status.Text = "가져오는 중…"; string file = dialog.FileName; var names = buddy.Presets.Items.Select(p => p.Name).ToArray();
                var result = await Task.Run(() => CouplePackage.Import(file, Storage.Root, names));
                buddy.Presets.AddImported(result.First); buddy.Presets.AddImported(result.Second);
                var card = buddy.Adventure.Pair(result.First.Id, result.Second.Id); card.Style = result.Pair.Style; card.Description = result.Pair.Description; card.FirstCalls = result.Pair.FirstCalls; card.SecondCalls = result.Pair.SecondCalls; card.Rules = result.Pair.Rules; card.Scenes = new(result.Pair.Scenes);
                buddy.Adventure.Touch();
                status.Text = $"'{result.First.Name}'와(과) '{result.Second.Name}' 프리셋을 추가했어요 ♡ 캐릭터 프리셋에서 대표로 적용하고, 친구 배치에서 둘째로 불러내면 관계 설정이 함께 따라옵니다.";
            }
            catch (Exception e) { status.Text = "가져오지 못했습니다. " + e.Message; }
            finally { IsEnabled = true; }
        }, true));
        share.Children.Add(Pastel.Text("받은 파일은 믿을 수 있는 사람이 준 것만 가져오세요. 가져온 프리셋은 새 프리셋으로 추가되고, 지금 쓰는 캐릭터는 바뀌지 않습니다.", 11));

        var buttons = new WrapPanel(); buttons.Children.Add(Pastel.Button("친구 배치 · 상호작용 설정", buddy.OpenDuo, true)); buttons.Children.Add(Pastel.Button("닫기", Close, true)); footer.Children.Add(buttons);
        buddy.Adventure.Changed += Changed; Closed += (_, _) => buddy.Adventure.Changed -= Changed;
        Pastel.Window(this, "두 친구의 관계", root, 760, 820); Refresh();
    }
    void Changed() => Dispatcher.BeginInvoke(Refresh);
    void Refresh()
    {
        var pair = Pair; scenes.Children.Clear(); album.Children.Clear();
        if (pair == null) { mood.Text = "둘째 친구가 없어요."; scenes.Children.Add(Pastel.Text("둘째 친구를 먼저 골라 주세요.", 13)); album.Children.Add(Pastel.Text("둘째 친구를 먼저 골라 주세요.", 13)); return; }
        string first = buddy.Chat.Preferences.Profile.Name, second = buddy.SecondaryPreset!.Profile.Name, today = DateTime.Now.ToString("yyyy-MM-dd");
        string sulking = pair.Mood.Sulking.Length > 0 && pair.Mood.SulkingDay == today ? (pair.Mood.Sulking == buddy.CharacterId ? first : second) : "";
        mood.Text = $"관계도 {pair.Bond}/1000 · 둘이 나눈 대화 {pair.Conversations}회 · 함께 작업 {TypingWorkClock.Format(pair.WorkMs)}\n" + (!pair.Moods ? "질투·화해는 꺼져 있어요." : sulking.Length > 0 ? "지금 " + sulking + "이(가) 살짝 토라져 있어요. 말을 걸거나 쓰다듬어 주세요." : pair.Mood.MadeUpDay == today ? "오늘은 한 번 토라졌다가 화해했어요 ♡" : "둘 다 기분이 좋아요.");
        foreach (var (key, title, help) in PairScenes.Slots)
        {
            pair.Scenes.TryGetValue(key, out var path); bool has = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            var row = new DockPanel(); var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(actions, Dock.Right);
            void Small(Button button) { button.Padding = new Thickness(9, 3, 9, 3); button.Margin = new Thickness(4, 0, 0, 0); button.FontSize = 12; actions.Children.Add(button); }
            bool open = key == PairScenes.Free || has || buddy.Progress.IsUnlocked(PairScenes.Unlock(key));
            if (!open) Small(Pastel.Button("열기 · " + PairScenes.Price + "코인", () =>
            {
                if (MessageBox.Show(this, "'" + title + "' 장면 자리를 " + PairScenes.Price + "코인으로 열까요? (보유 " + buddy.Game.State.Coins + "코인)", "둘만의 장면", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                try { buddy.Buy(PairScenes.Unlock(key), PairScenes.Price); status.Text = "'" + title + "' 자리를 열었어요 ♡ 이제 그림을 등록할 수 있어요."; } catch (Exception e) { status.Text = e.Message; }
                Refresh();
            }));
            else Small(Pastel.Button("그림 선택…", async () =>
            {
                var dialog = new OpenFileDialog { Title = title + " 그림 선택", Filter = "이미지 (PNG / JPG)|*.png;*.jpg;*.jpeg", CheckFileExists = true }; if (dialog.ShowDialog(this) != true) return;
                try { IsEnabled = false; string source = dialog.FileName; string stored = await Task.Run(() => ImageAssets.Import(source, Storage.Root)); pair.Scenes[key] = stored; buddy.Adventure.Touch(); status.Text = title + " 장면을 등록했어요 ♡"; }
                catch (Exception e) { status.Text = "그림을 등록하지 못했습니다. " + e.Message; }
                finally { IsEnabled = true; }
            }, true));
            if (has) { Small(Pastel.Button("미리 보기", () => { if (buddy.DisplayedCompanion == null) status.Text = "둘째 친구가 화면에 나와 있을 때 볼 수 있어요."; else buddy.PreviewScene(key); }, true)); Small(Pastel.Button("해제", () => { pair.Scenes.Remove(key); buddy.Adventure.Touch(); })); }
            row.Children.Add(actions);
            if (has) { try { var thumb = new Image { Source = ImageAssets.Read(path!, 120), Width = 64, Height = 48, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0) }; DockPanel.SetDock(thumb, Dock.Left); row.Children.Add(thumb); } catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException) { } }
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; words.Children.Add(new TextBlock { Text = (has ? "● " : open ? "○ " : "🔒 ") + title, Foreground = Pastel.Ink, FontWeight = FontWeights.Bold });
            words.Children.Add(new TextBlock { Text = help, Foreground = Pastel.Ink, FontSize = 12, Opacity = .85, TextWrapping = TextWrapping.Wrap }); row.Children.Add(words);
            var card = Pastel.Card(row); card.Padding = new Thickness(12, 8, 12, 8); card.Margin = new Thickness(0, 0, 4, 6); scenes.Children.Add(card);
        }
        if (pair.Events.Count == 0) album.Children.Add(Pastel.Text("아직 기록이 없어요. 둘이 이야기를 나누면 첫 장이 채워져요.", 13));
        foreach (var item in pair.Events.AsEnumerable().Reverse().Take(200).ToArray())
        {
            var entry = new StackPanel(); var head = new DockPanel(); var remove = Pastel.Button("지우기", () => { pair.Events.Remove(item); buddy.Adventure.Touch(); });
            remove.Padding = new Thickness(8, 2, 8, 2); remove.FontSize = 11; DockPanel.SetDock(remove, Dock.Right); head.Children.Add(remove);
            head.Children.Add(new TextBlock { Text = $"♡ {item.At:yyyy-MM-dd HH:mm} · {item.Kind}", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            entry.Children.Add(head); entry.Children.Add(new TextBlock { Text = item.Title, Foreground = Pastel.Ink, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            if (item.Text.Length > 0) entry.Children.Add(new TextBlock { Text = item.Text, Foreground = Pastel.Ink, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 2, 0, 0) });
            var card = Pastel.Card(entry); card.Padding = new Thickness(12, 8, 12, 8); card.Margin = new Thickness(0, 0, 4, 6); album.Children.Add(card);
        }
    }
}
