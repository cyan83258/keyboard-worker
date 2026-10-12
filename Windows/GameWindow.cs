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
    readonly GearShelf shop, inventory; readonly WrapPanel who = new() { Margin = new Thickness(0, 0, 0, 8) };
    // Whose outfit the shop, the wardrobe, the placement window and the anchors below are about: the main pet's or the second pet's.
    bool second; GameState wear;
    readonly Dictionary<string, Button> questButtons = new();
    readonly Dictionary<string, CheckBox> toggles = new();
    readonly Dictionary<string, Slider> anchors = new();
    readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public GameWindow(Buddy buddy)
    {
        this.buddy = buddy; game = buddy.Game; wear = game.State.For(false); shop = new GearShelf(Bought, _ => null); inventory = new GearShelf(tile => Wear(tile.Id, tile.Name), Options);
        var root = new DockPanel { Margin = new Thickness(18) }; var footer = new StackPanel(); footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var header = new StackPanel(); header.Children.Add(summary); header.Children.Add(xp); header.Children.Add(progress); header.Children.Add(who); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
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
        store.Children.Insert(0, new TextBlock { Text = "누르면 사서 바로 입혀요. 이미 가진 것은 누르면 입고 벗어요.", Foreground = Pastel.Ink, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
        var closet = Tab("옷장"); closet.Children.Add(new TextBlock { Text = "누르면 입고, 다시 누르면 벗어요. 오른쪽 클릭: 수정 · 삭제. 자리마다 하나씩 입을 수 있어요.", Foreground = Pastel.Ink, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        tools.Children.Add(Pastel.Button("+ 내 장비 등록 (PNG / JPG)", () => new CustomGearWindow(game) { Owner = this, ShowInTaskbar = false }.ShowDialog(), true));
        var place = Pastel.Button("악세서리 위치 편집", Place, true); place.ToolTip = "입고 있는 악세서리를 끌어서 옮기고, 휠로 크기를 바꿔요"; tools.Children.Add(place); closet.Children.Add(tools); closet.Children.Add(inventory);
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
            var next = game.State.For(second); foreach (var pair in toggles) typeof(GameState).GetProperty(pair.Key)!.SetValue(next, pair.Value.IsChecked == true);
            foreach (var pair in anchors) typeof(GameState).GetProperty(pair.Key)!.SetValue(next, pair.Value.Value); game.Options(next, second); status.Text = "저장했어요 ♡";
        })));
        Pastel.Window(this, "친구의 작은 모험", root, 780, 800); game.Changed += Rebuild; timer.Tick += (_, _) => Refresh(); timer.Start();
        Closed += (_, _) => { timer.Stop(); game.Changed -= Rebuild; game.Save(); }; Rebuild();
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
    void Place() => new GearPlaceWindow(buddy, second) { Owner = this, ShowInTaskbar = false }.ShowDialog();
    bool Worn(string slot, string id) => wear.Equipped.GetValueOrDefault(slot, "") == id;
    // With two pets there is a choice of whom to dress; the anchors shown are that pet's.
    void Who()
    {
        who.Children.Clear(); var partner = buddy.SecondaryPreset; if (partner == null) { second = false; return; }
        who.Children.Add(new TextBlock { Text = "누구에게 입힐까요", Foreground = Pastel.Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        foreach (var (isSecond, name) in new[] { (false, "대표 · " + buddy.Chat.Preferences.Profile.Name), (true, "둘째 · " + partner.Profile.Name) })
        {
            var choice = Pastel.Button(name, () => { if (second == isSecond) return; second = isSecond; var now = game.State.For(second); foreach (var pair in anchors) pair.Value.Value = (double)typeof(GameState).GetProperty(pair.Key)!.GetValue(now)!; Rebuild(); }, true);
            choice.Padding = new Thickness(12, 4, 12, 4); choice.FontSize = 12; choice.Margin = new Thickness(0, 0, 6, 0); Pastel.Select(choice, second == isSecond); who.Children.Add(choice);
        }
    }
    // The shop sells the accessories that came with the program; the wardrobe holds what was bought and the user's own gear.
    void Rebuild()
    {
        Who(); wear = game.State.For(second); Refresh(); var owned = game.State.Owned;
        shop.Show(ShopGear.Catalog.Select(item =>
        {
            bool mine = owned.Contains(item.Id), worn = Worn(item.Slot, item.Id), can = mine || (game.Level >= item.Level && game.State.Coins >= item.Price);
            string note = worn ? "장착 중" : mine ? "보유 중" : item.Price + "코인" + (item.Level > 1 ? " · Lv." + item.Level : "");
            return new GearShelf.Tile(item.Id, item.Name, item.Slot, ShopGear.Items.First(g => g.Id == item.Id).ImagePath, note, worn, !can, item.Description + (mine ? "" : game.Level < item.Level ? $"\nLv.{item.Level}부터 살 수 있어요" : game.State.Coins < item.Price ? "\n코인이 부족해요" : "\n누르면 사서 입혀요"));
        }), "아직 파는 악세서리가 없어요. 내 그림은 옷장의 '+ 내 장비 등록'으로 바로 입힐 수 있어요.");
        inventory.Show(wear.Gear.Where(g => owned.Contains(g.Id)).Select(g => new GearShelf.Tile(g.Id, g.Name, g.Slot, g.ImagePath, Worn(g.Slot, g.Id) ? "장착 중" : ShopGear.Has(g.Id) ? "" : "내 장비", Worn(g.Slot, g.Id), false, "누르면 입고 벗어요 · 오른쪽 클릭: 더 보기")),
            "아직 아이템이 없어요. 상점에서 골라 보거나 '+ 내 장비 등록'으로 내 그림을 넣어 보세요 ♡");
    }
    // A click in the shop: what is owned is put on or taken off; what is not is bought (after asking) and put on.
    void Bought(GearShelf.Tile tile)
    {
        if (game.State.Owned.Contains(tile.Id)) { Wear(tile.Id, tile.Name); return; }
        var item = ShopGear.Catalog.First(i => i.Id == tile.Id);
        if (game.Level < item.Level) { status.Text = $"{item.Name}은(는) Lv.{item.Level}부터 살 수 있어요."; return; }
        if (game.State.Coins < item.Price) { status.Text = $"코인이 부족해요. ({item.Price}코인 필요 · 보유 {game.State.Coins:N0}코인)"; return; }
        if (MessageBox.Show(this, $"{item.Name}을(를) {item.Price}코인에 살까요?", "상점", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Run(() => { game.Buy(item.Id); game.Equip(item.Id, second); status.Text = item.Name + " 장착했어요 ♡"; });
    }
    void Wear(string id, string name) => Run(() =>
    {
        var gear = wear.Wearing(id); if (gear == null) return;
        if (Worn(gear.Slot, id)) { game.Remove(gear.Slot, second); status.Text = name + " 벗었어요."; } else { game.Equip(id, second); status.Text = name + " 장착했어요 ♡"; }
    });
    // The right-click menu of the wardrobe.
    ContextMenu Options(GearShelf.Tile tile)
    {
        var menu = new ContextMenu(); void Add(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        var custom = game.State.CustomItems.Find(c => c.Id == tile.Id);
        Add(Worn(tile.Slot, tile.Id) ? "벗기" : "장착", () => Wear(tile.Id, tile.Name));
        if (custom != null) Add("수정…", () => new CustomGearWindow(game, custom) { Owner = this, ShowInTaskbar = false }.ShowDialog());
        Add("위치 편집… (입고 있는 것)", Place);
        Add("옷장에서 삭제", () => DeleteItem(tile.Id, tile.Name, custom != null));
        return menu;
    }
    void DeleteItem(string id, string name, bool custom) => Run(() =>
    {
        string detail = custom ? "장비 등록과 착용이 해제됩니다. 원본 이미지 파일은 유지됩니다." : "보유와 착용이 해제됩니다. 코인은 환불되지 않으며 상점에서 다시 구매할 수 있습니다.";
        if (MessageBox.Show(this, name + " 장비를 옷장에서 삭제할까요?\n" + detail, "장비 삭제", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        game.DeleteOwned(id); status.Text = name + " 장비를 삭제했어요.";
    });    public void CaptureTabs(string path)
    {
        var grid = (Grid)((Border)Content).Child; var dock = (DockPanel)grid.Children[1]; var tabs = dock.Children.OfType<TabControl>().Single();
        for (int n = 0; n < tabs.Items.Count; n++) { tabs.SelectedIndex = n; UpdateLayout(); Pastel.Capture(this, System.IO.Path.Combine(path, "ui-game-tab-" + n + ".png")); }
    }
}
