using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class GameWindow : Window
{
    readonly Buddy buddy;
    readonly PetGame game;
    readonly TextBlock summary = Pastel.Text("", 20), progress = Pastel.Text(""), goals = Pastel.Text(""), badges = Pastel.Text(""), status = Pastel.Text("");
    readonly ProgressBar xp = new() { Height = 12, Maximum = 1, Margin = new Thickness(0, 6, 0, 12), Foreground = Pastel.Pink };
    readonly StackPanel shop = new(), inventory = new();
    readonly LazyImageCache previewCache;
    readonly Dictionary<string, System.Windows.Controls.Image> previews = new();
    readonly Dictionary<string, Button> questButtons = new();
    readonly Dictionary<string, CheckBox> toggles = new();
    readonly Dictionary<string, Slider> anchors = new();
    readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public GameWindow(Buddy buddy)
    {
        this.buddy = buddy; game = buddy.Game; previewCache = new(Dispatcher, UpdatePreviews);
        var root = new DockPanel { Margin = new Thickness(18) }; var footer = new StackPanel(); footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var header = new StackPanel(); header.Children.Add(summary); header.Children.Add(xp); header.Children.Add(progress); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var tabs = new TabControl { Background = Pastel.Paper, BorderBrush = Pastel.Pink }; root.Children.Add(tabs);
        StackPanel Tab(string name) { var p = new StackPanel { Margin = new Thickness(16) }; tabs.Items.Add(new TabItem { Header = name, Content = Pastel.Scroll(p), Padding = new Thickness(13, 8, 13, 8) }); return p; }
        var growth = Tab("성장·목표"); growth.Children.Add(Pastel.Text("작은 작업이 작은 모험이 돼요 ♡", 21));
        growth.Children.Add(Pastel.Text("처음 시작하면 20코인을 선물합니다. 키보드 활동 1분마다 10XP와 5코인, 레벨업마다 10코인을 얻어요. 기념 배지는 처음 얻을 때 5코인을 더 줍니다. 실제 시간 측정에 포함된 구간만 계산하며 유료 결제는 없습니다. 이전 작업 시간은 소급 보상하지 않습니다.", 13));
        growth.Children.Add(Pastel.Card(goals, true));
        foreach (var quest in PetGame.Quests)
        {
            var p = new StackPanel(); p.Children.Add(Pastel.Text($"{quest.Name} · {quest.Coins}코인 + {quest.Xp}XP", 16));
            var button = Pastel.Button("보상 받기", () => Run(() => { var reward = game.Claim(quest.Id, WorkTimeStore.Today); buddy.GameNotice(reward.Message); }), true);
            questButtons[quest.Id] = button;
            p.Children.Add(button);
            growth.Children.Add(Pastel.Card(p));
        }
        growth.Children.Add(Pastel.Text("기념 배지", 19)); growth.Children.Add(badges);
        growth.Children.Add(Pastel.Text("목표는 날짜가 바뀌면 새로 시작하지만 레벨·코인·아이템·친밀도는 유지됩니다. 쉬어도 벌점이나 감소는 없어요. 성장 기록은 캐릭터 프리셋을 바꿔도 이어집니다.", 12));
        var store = Tab("상점"); store.Children.Add(shop);
        var closet = Tab("옷장"); closet.Children.Add(Pastel.Text("머리·얼굴·목·몸·오라를 한 개씩 장착할 수 있어요. PNG/JPG 캐릭터에도 장식이 겹쳐 표시됩니다."));
        closet.Children.Add(Pastel.Button("+ 내 장비 등록 (PNG / JPG)", () => new CustomGearWindow(game) { Owner = this, ShowInTaskbar = false }.ShowDialog(), true)); closet.Children.Add(inventory);
        inventory.IsVisibleChanged += (_, _) => UpdatePreviews();
        var options = Tab("꾸미기·설정");
        Toggle(options, "Enabled", "게임 성장·보상 기능 사용", game.State.Enabled); Toggle(options, "ShowBadge", "캐릭터 아래 레벨·코인 버튼 표시", game.State.ShowBadge);
        Toggle(options, "ShowAccessories", "장착한 액세서리 표시", game.State.ShowAccessories); Toggle(options, "Announcements", "레벨업·목표 보상 말풍선 안내", game.State.Announcements);
        Toggle(options, "ShareWithAi", "채팅에 현재 레벨·코인·친밀도·장착 상태 반영", game.State.ShareWithAi);
        options.Children.Add(Pastel.Text("커스텀 이미지에서 위치가 맞지 않으면 아래 값을 조절하세요. 이미지는 그대로 두고 장식을 겹쳐 표시합니다."));
        Slider(options, "HeadX", "머리 위치 · 가로 %", game.State.HeadX, 0, 100); Slider(options, "HeadY", "머리 위치 · 세로 %", game.State.HeadY, 0, 100);
        Slider(options, "FaceX", "안경 위치 · 가로 %", game.State.FaceX, 0, 100); Slider(options, "FaceY", "안경 위치 · 세로 %", game.State.FaceY, 0, 100);
        Slider(options, "NeckX", "목 위치 · 가로 %", game.State.NeckX, 0, 100); Slider(options, "NeckY", "목 위치 · 세로 %", game.State.NeckY, 0, 100);
        Slider(options, "AccessoryScale", "장식 크기", game.State.AccessoryScale, .5, 2);
        options.Children.Add(Pastel.Button("위치 기본값", () =>
        {
            anchors["HeadX"].Value = 50;
            anchors["HeadY"].Value = 15;
            anchors["FaceX"].Value = 50;
            anchors["FaceY"].Value = 40;
            anchors["NeckX"].Value = 50;
            anchors["NeckY"].Value = 61;
            anchors["AccessoryScale"].Value = 1;
        }, true));
        options.Children.Add(Pastel.Button("꾸미기·설정 저장", () => Run(() =>
        {
            var next = game.State.Snapshot(); foreach (var pair in toggles) typeof(GameState).GetProperty(pair.Key)!.SetValue(next, pair.Value.IsChecked == true);
            foreach (var pair in anchors) typeof(GameState).GetProperty(pair.Key)!.SetValue(next, pair.Value.Value); game.Options(next); status.Text = "저장했어요 ♡";
        })));
        Pastel.Window(this, "친구의 작은 모험", root, 780, 800); game.Changed += Rebuild; timer.Tick += (_, _) => Refresh(); timer.Start();
        Closed += (_, _) => { timer.Stop(); game.Changed -= Rebuild; previewCache.Dispose(); game.Save(); }; Rebuild();
    }
    void Toggle(StackPanel p, string key, string text, bool value) { var c = new CheckBox { Content = text, IsChecked = value }; toggles[key] = c; p.Children.Add(c); }
    void Slider(StackPanel p, string key, string text, double value, double min, double max)
    {
        var label = Pastel.Text(text + ": " + value.ToString("0.##")); p.Children.Add(label); var s = new Slider { Minimum = min, Maximum = max, Value = value, Margin = new Thickness(0, 3, 0, 12) };
        s.ValueChanged += (_, _) => label.Text = text + ": " + s.Value.ToString("0.##"); anchors[key] = s; p.Children.Add(s);
    }
    void Run(Action action) { try { action(); Refresh(); } catch (Exception e) { status.Text = e.Message; } }
    void Refresh()
    {
        summary.Text = $"♡ {buddy.Chat.Preferences.Profile.Name} · Lv.{game.Level}   |   {game.State.Coins:N0}코인";
        xp.Value = game.Level == 100 ? 1 : (double)game.XpAtLevel / game.NextXp;
        progress.Text = game.Level == 100 ? "최고 레벨 달성 ♡" : $"다음 레벨까지 {game.NextXp - game.XpAtLevel}XP · 현재 {game.XpAtLevel}/{game.NextXp}XP";
        goals.Text = $"오늘 키보드 활동   {TypingWorkClock.Format(game.State.DailyWorkMs)}\n누적 동행   {TypingWorkClock.Format(game.State.LifetimeWorkMs)}\n이 친구 친밀도 {buddy.Adventure.Friend(buddy.CharacterId, buddy.Chat.Preferences.Profile.Name).Bond}/1000 · 오늘 공동 쓰다듬기 {game.State.DailyPats}/10";
        if (!buddy.Pref.Desktop.WorkEnabled) goals.Text += "\n작업 시간 측정을 켜야 경험치와 코인이 쌓여요.";
        foreach (var quest in PetGame.Quests)
        {
            bool claimed = game.State.ClaimedQuests.Contains(quest.Id); var button = questButtons[quest.Id]; button.IsEnabled = !claimed && game.State.Enabled && game.QuestReady(quest);
            button.Content = claimed ? "오늘 보상 완료" : game.QuestReady(quest) ? "보상 받기" : quest.Kind == "work" ? $"진행 {Math.Min(quest.Target, game.State.DailyWorkMs) / 60000.0:0.#}/{quest.Target / 60000}분" : $"진행 {game.State.DailyPats}/{quest.Target}회";
        }
        badges.Text = game.State.Badges.Count == 0 ? "첫 작업, 첫 쇼핑, 목표 달성으로 기념 배지를 모아 보세요." : string.Join("\n", game.State.Badges.Select(id => "☆ " + PetGame.BadgeNames[id]));
    }
    void Rebuild()
    {
        Refresh(); shop.Children.Clear(); inventory.Children.Clear(); previews.Clear();
        previewCache.Configure(new Preferences { ImagePaths = game.State.CustomItems.ToDictionary(c => c.Id, c => c.ImagePath) }, 120);
        // What the program draws itself, then the accessories that came with it as pictures.
        foreach (var item in PetGame.Items.Concat(ShopGear.Catalog))
        {
            var content = new DockPanel();
            var shipped = ShopGear.Items.FirstOrDefault(g => g.Id == item.Id); FrameworkElement icon = shipped != null ? StageParts.Picture(shipped.ImagePath, 70) : new ItemPreview { ItemId = item.Id, Width = 80, Height = 70 }; icon.Margin = new Thickness(shipped == null ? 0 : 5, 0, shipped == null ? 12 : 17, 0);
            DockPanel.SetDock(icon, Dock.Left);
            content.Children.Add(icon);
            var p = new StackPanel(); p.Children.Add(Pastel.Text($"{item.Name} · {item.Price}코인 · Lv.{item.Level}", 17)); p.Children.Add(Pastel.Text(item.Description, 12));
            bool owned = game.State.Owned.Contains(item.Id);
            var button = Pastel.Button(owned ? "보유 중 · 장착" : "구매하기", () => Run(() => { if (!game.State.Owned.Contains(item.Id)) game.Buy(item.Id); game.Equip(item.Id); status.Text = item.Name + " 장착했어요 ♡"; }), true);
            button.IsEnabled = owned || (game.Level >= item.Level && game.State.Coins >= item.Price); p.Children.Add(button); content.Children.Add(p); shop.Children.Add(Pastel.Card(content));
            if (owned)
            {
                var row = new StackPanel(); bool equipped = game.State.Equipped.GetValueOrDefault(item.Slot, "") == item.Id;
                row.Children.Add(Pastel.Text(item.Name + (equipped ? " · 장착 중" : ""), 17));
                row.Children.Add(Pastel.Button(equipped ? "벗기" : "장착", () => Run(() => { if (game.State.Equipped.GetValueOrDefault(item.Slot, "") == item.Id) game.Remove(item.Slot); else game.Equip(item.Id); }), true));
                inventory.Children.Add(Pastel.Card(row));
                row.Children.Add(Pastel.Button("옷장에서 삭제", () => DeleteItem(item.Id, item.Name, false)));
            }
        }
        foreach (var custom in game.State.CustomItems)
        {
            var row = new StackPanel(); bool equipped = game.State.Equipped.GetValueOrDefault(custom.Slot, "") == custom.Id;
            row.Children.Add(Pastel.Text(custom.Name + " · 내 장비" + (equipped ? " · 장착 중" : ""), 17));
            var picture = new Image { Width = 90, Height = 70, Stretch = System.Windows.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
            previews[custom.Id] = picture;
            row.Children.Add(picture);
            var actions = new WrapPanel();
            actions.Children.Add(Pastel.Button(equipped ? "벗기" : "장착", () => Run(() => { if (game.State.Equipped.GetValueOrDefault(custom.Slot, "") == custom.Id) game.Remove(custom.Slot); else game.Equip(custom.Id); }), true));
            actions.Children.Add(Pastel.Button("수정", () => new CustomGearWindow(game, custom) { Owner = this, ShowInTaskbar = false }.ShowDialog()));
            actions.Children.Add(Pastel.Button("옷장에서 삭제", () => DeleteItem(custom.Id, custom.Name, true))); row.Children.Add(actions); inventory.Children.Add(Pastel.Card(row));
        }
        UpdatePreviews();
        if (game.State.Owned.Count == 0) inventory.Children.Add(Pastel.Text("아직 아이템이 없어요. 시작 선물로 상점에서 작은 장식을 골라 보세요 ♡"));
    }
    void UpdatePreviews()
    {
        if (!inventory.IsVisible) return;
        foreach (var pair in previews) { var image = previewCache.Get(pair.Key); if (image != null && !ReferenceEquals(pair.Value.Source, image)) pair.Value.Source = image; }
    }
    void DeleteItem(string id, string name, bool custom) => Run(() =>
    {
        string detail = custom ? "장비 등록과 착용이 해제됩니다. 원본 이미지 파일은 유지됩니다." : "보유와 착용이 해제됩니다. 코인은 환불되지 않으며 상점에서 다시 구매할 수 있습니다.";
        if (MessageBox.Show(this, name + " 장비를 옷장에서 삭제할까요?\n" + detail, "장비 삭제", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        game.DeleteOwned(id); status.Text = name + " 장비를 삭제했어요.";
    });
    public void CaptureTabs(string path)
    {
        var grid = (Grid)((Border)Content).Child; var dock = (DockPanel)grid.Children[1]; var tabs = dock.Children.OfType<TabControl>().Single();
        for (int n = 0; n < tabs.Items.Count; n++) { tabs.SelectedIndex = n; UpdateLayout(); Pastel.Capture(this, System.IO.Path.Combine(path, "ui-game-tab-" + n + ".png")); }
    }
}
