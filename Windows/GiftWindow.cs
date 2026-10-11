using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KeyboardWorker;

// Gifts: drawing them with coins, the bag of gifts not yet given, and the collection of every gift there is.
// Giving happens on a character's page (GiftPicker).
public sealed class GiftWindow : Window
{
    readonly Buddy buddy; readonly TabControl tabs = new() { Background = Pastel.Paper, BorderBrush = Pastel.Pink };
    readonly WrapPanel drawn = new() { HorizontalAlignment = HorizontalAlignment.Center }, bag = new(), all = new();
    readonly TextBlock coins = Pastel.Text("", 14), pity = Pastel.Text("", 12), status = Pastel.Text("", 13), bagNote = Pastel.Text("", 12), allNote = Pastel.Text("", 12), wait = new() { Text = "✦", FontSize = 64, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
    readonly ConfettiBurst confetti = new(); readonly DispatcherTimer reveal = new() { Interval = TimeSpan.FromMilliseconds(130) };
    readonly Button once, ten; List<GiftItem> pending = new(); int shown;
    public void ShowPage(int index) => tabs.SelectedIndex = index;
    public int Pages => tabs.Items.Count;
    public GiftWindow(Buddy buddy)
    {
        this.buddy = buddy; var root = new DockPanel { Margin = new Thickness(20) }; confetti.LowPower = buddy.Pref.LowPower; wait.Foreground = Pastel.Hot;
        StackPanel Tab(string title) { var p = new StackPanel { Margin = new Thickness(18) }; tabs.Items.Add(new TabItem { Header = title, Content = Pastel.Scroll(p), Padding = new Thickness(16, 8, 16, 8) }); return p; }

        var draw = Tab("선물 뽑기"); draw.Children.Add(Pastel.Text("두근두근 선물 뽑기 ♡", 22));
        draw.Children.Add(Pastel.Text("코인으로 선물을 뽑아 가방에 모았다가, 캐릭터 페이지에서 친구에게 건네요. 귀한 선물일수록 친밀도가 많이 오릅니다.", 12));
        coins.FontWeight = FontWeights.Bold; draw.Children.Add(coins);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        once = Pastel.Button($"1번 뽑기 · {GiftShop.DrawCost}코인", () => Draw(1)); ten = Pastel.Button($"10번 뽑기 · {GiftShop.TenCost}코인 (★★★ 이상 1개 보장)", () => Draw(10), true);
        once.Padding = ten.Padding = new Thickness(18, 9, 18, 10); once.FontSize = ten.FontSize = 15; buttons.Children.Add(once); buttons.Children.Add(ten); draw.Children.Add(buttons);
        status.HorizontalAlignment = HorizontalAlignment.Center; status.FontWeight = FontWeights.Bold; status.Foreground = Pastel.Hot; draw.Children.Add(status);
        // The stage of the draw: a star that trembles, then the cards, with confetti over them for the rarest.
        var stage = new Grid { MinHeight = 250 }; stage.Children.Add(wait); stage.Children.Add(drawn); stage.Children.Add(confetti); draw.Children.Add(stage);
        var rates = new StackPanel(); rates.Children.Add(Pastel.Text("나올 확률", 14));
        var chances = new WrapPanel();
        for (int stars = 5; stars >= 1; stars--) chances.Children.Add(new TextBlock { Text = $"{GiftShop.Stars(stars)} {GiftShop.Chances[stars - 1] / 10.0:0.#}%   ", Foreground = GiftParts.Tone(stars), FontWeight = FontWeights.Bold, FontSize = 13 });
        rates.Children.Add(chances); rates.Children.Add(pity);
        rates.Children.Add(Pastel.Text("✧ 가 붙은 선물은 특별한 힘이 있어요 — ★★★★: 받은 날 하루 그 친구의 친밀도 2배 · ★★★★★: 받았을 때의 한마디가 대사 도감에 저절로 담김.", 11));
        draw.Children.Add(Pastel.Card(rates, true));

        var mine = Tab("내 선물 가방"); mine.Children.Add(Pastel.Text("가방 속 선물", 22)); mine.Children.Add(bagNote); mine.Children.Add(bag);
        var bagButtons = new WrapPanel(); bagButtons.Children.Add(Pastel.Button("캐릭터 페이지에서 선물하기 ♡", () => buddy.OpenCharacter())); mine.Children.Add(bagButtons);
        mine.Children.Add(Pastel.Text("선물은 캐릭터 페이지의 '선물하기'에서 건넵니다. 친구마다 하루 " + GiftShop.PerDay + "개까지 받을 수 있고, 좋아하는 선물이면 친밀도가 2배로 올라요.", 12));

        var book = Tab("선물 도감"); book.Children.Add(Pastel.Text("세상의 모든 선물", 22)); book.Children.Add(allNote); book.Children.Add(all);

        var footer = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; footer.Children.Add(Pastel.Button("닫기", Close, true)); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(tabs);
        reveal.Tick += (_, _) => Reveal();
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, tabs)) Refresh(); };
        buddy.Game.Changed += Changed; buddy.Adventure.Changed += Changed; Closed += (_, _) => { buddy.Game.Changed -= Changed; buddy.Adventure.Changed -= Changed; reveal.Stop(); };
        Pastel.Window(this, "선물", root, 800, 820); Refresh();
    }
    bool queued;
    void Changed() { if (queued) return; queued = true; Dispatcher.BeginInvoke(() => { queued = false; if (IsLoaded) Refresh(); }); }

    // ---- drawing ----
    void Draw(int count)
    {
        if (reveal.IsEnabled) return;
        try { pending = buddy.Adventure.Draw(buddy.Game, count); }
        catch (InvalidOperationException e) { status.Text = e.Message; return; }
        drawn.Children.Clear(); shown = 0; once.IsEnabled = ten.IsEnabled = false; status.Text = "두근두근…";
        if (buddy.Pref.LowPower) { while (shown < pending.Count) Reveal(); return; }
        // A moment of suspense before the first card.
        wait.Visibility = Visibility.Visible; wait.RenderTransformOrigin = new Point(.5, .5); var spin = new RotateTransform(); wait.RenderTransform = spin;
        spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-14, 14, TimeSpan.FromMilliseconds(110)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
        var start = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(680) }; start.Tick += (_, _) => { start.Stop(); wait.Visibility = Visibility.Collapsed; reveal.Start(); }; start.Start();
    }
    void Reveal()
    {
        if (shown >= pending.Count) { Finish(); return; }
        var gift = pending[shown++]; var card = GiftParts.Card(gift); drawn.Children.Add(card); GiftParts.Pop(card, gift, !buddy.Pref.LowPower);
        if (shown >= pending.Count) Finish();
    }
    void Finish()
    {
        reveal.Stop(); once.IsEnabled = ten.IsEnabled = true; var best = pending.OrderByDescending(g => g.Stars).FirstOrDefault(); if (best == null) return;
        status.Text = best.Stars == 5 ? "✦ " + best.Name + "! 아주 귀한 선물이에요 ✦" : best.Stars == 4 ? "✧ " + best.Name + "! 귀한 선물이 나왔어요" : pending.Count == 1 ? best.Name + "을(를) 얻었어요" : "선물 " + pending.Count + "개를 가방에 넣었어요";
        if (best.Stars >= 4) confetti.Start();
        Refresh();
    }

    void Refresh()
    {
        var state = buddy.Adventure.State; coins.Text = $"● 보유 {buddy.Game.State.Coins:N0}코인 · 지금까지 {state.GiftDraws}번 뽑았어요";
        pity.Text = $"★★★★ 이상이 {GiftShop.Pity}번 안에 한 번은 꼭 나와요 (앞으로 {Math.Max(1, GiftShop.Pity - state.SinceRare)}번 안에 확정).";
        if (tabs.SelectedIndex == 1)
        {
            bag.Children.Clear(); int total = state.GiftBag.Values.Sum(); bagNote.Text = total == 0 ? "가방이 비어 있어요. '선물 뽑기'에서 뽑아 보세요." : $"모두 {total}개 · {state.GiftBag.Count}종류";
            foreach (var gift in GiftShop.All.Where(g => buddy.Adventure.Owned(g.Name) > 0).OrderByDescending(g => g.Stars)) bag.Children.Add(GiftParts.Card(gift, buddy.Adventure.Owned(gift.Name)));
        }
        if (tabs.SelectedIndex == 2)
        {
            all.Children.Clear(); int met = GiftShop.All.Count(g => state.GiftsMet.Contains(g.Name)); allNote.Text = $"만난 선물 {met} / {GiftShop.All.Length}종";
            foreach (var gift in GiftShop.All.OrderByDescending(g => g.Stars)) all.Children.Add(GiftParts.Card(gift, -1, !state.GiftsMet.Contains(gift.Name)));
        }
    }
}

// Choosing a gift from the bag for the character whose page is open.
public sealed class GiftPicker : Window
{
    public string? Chosen { get; private set; }
    public GiftPicker(Buddy buddy, Buddy.StageCharacter who)
    {
        var p = new StackPanel { Margin = new Thickness(22) }; var friend = buddy.Adventure.Friend(who.Id, who.Name); int left = GiftShop.PerDay - friend.Gifts;
        p.Children.Add(Pastel.Text(who.Name + "에게 줄 선물 ♡", 22));
        p.Children.Add(Pastel.Text((left > 0 ? $"오늘 {left}개 더 줄 수 있어요." : "오늘은 선물을 다 줬어요. 내일 또 줄 수 있어요.") + (friend.Discovered ? "  ♥ 좋아하는 선물: " + friend.Favorite : "  어떤 선물을 좋아하는지는 아직 몰라요."), 13));
        var cards = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; var owned = GiftShop.All.Where(g => buddy.Adventure.Owned(g.Name) > 0).OrderByDescending(g => g.Stars).ToArray();
        if (owned.Length == 0) p.Children.Add(Pastel.Text("가방에 선물이 없어요. 먼저 선물을 뽑아 주세요.", 13));
        foreach (var gift in owned)
        {
            var card = GiftParts.Card(gift, buddy.Adventure.Owned(gift.Name), false, friend.Discovered && gift.Name == friend.Favorite);
            if (left > 0) { card.Cursor = System.Windows.Input.Cursors.Hand; card.ToolTip = gift.Name + " 건네기" + (GiftShop.EffectNote(gift).Length > 0 ? "\n✧ " + GiftShop.EffectNote(gift) : ""); string name = gift.Name; card.MouseLeftButtonUp += (_, _) => { Chosen = name; DialogResult = true; }; }
            else card.Opacity = .55;
            cards.Children.Add(card);
        }
        p.Children.Add(cards);
        var buttons = new WrapPanel(); buttons.Children.Add(Pastel.Button("선물 뽑으러 가기", () => { DialogResult = false; buddy.OpenGifts(); }, true)); buttons.Children.Add(Pastel.Button("닫기", () => DialogResult = false, true)); p.Children.Add(buttons);
        Pastel.Window(this, "선물하기", Pastel.Scroll(p), 660, 640);
    }
}
