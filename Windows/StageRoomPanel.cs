using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardWorker;

// The chat rooms of the page on show, as a list down the left side of a page window: a click opens a room, "＋" makes one,
// the gear opens its settings (name, author's note, memory, delete). Both pages use it; it can be folded away.
public sealed class StageRoomPanel : Border
{
    public const double Wide = 214;
    readonly Buddy buddy; readonly Window owner; readonly Func<bool> busy; readonly Action changed;
    readonly StackPanel list = new(); readonly TextBox filter = new() { Padding = new Thickness(8, 4, 8, 4), FontSize = 12, Margin = new Thickness(0, 0, 0, 8), ToolTip = "방 이름으로 찾기" }; readonly Button add;
    string page = "", title = "";
    // busy: an answer is being made, so rooms stay as they are. changed: another room is open now, or the rooms themselves changed.
    public StageRoomPanel(Buddy buddy, Window owner, Func<bool> busy, Action changed)
    {
        this.buddy = buddy; this.owner = owner; this.busy = busy; this.changed = changed;
        Width = Wide; Margin = new Thickness(16, 16, 0, 16); Padding = new Thickness(10, 10, 6, 10); CornerRadius = new CornerRadius(12); Background = Pastel.CardBlue; BorderBrush = Pastel.Line; BorderThickness = new Thickness(1.5, 1.5, 3, 3.5);
        var body = new DockPanel(); Child = body;
        var head = new DockPanel { Margin = new Thickness(2, 0, 4, 8) }; DockPanel.SetDock(head, Dock.Top); body.Children.Add(head);
        add = StageParts.Tool("＋ 새 방", "새 채팅방을 만들어요. 방마다 대화, 작가 노트, 기억이 따로예요.", Add); add.Margin = new Thickness(0); DockPanel.SetDock(add, Dock.Right); head.Children.Add(add);
        head.Children.Add(new TextBlock { Text = "채팅방", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(filter, Dock.Top); filter.Margin = new Thickness(0, 0, 4, 8); body.Children.Add(filter); filter.TextChanged += (_, _) => Build();
        body.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 4, 0) });
        Visibility = buddy.Rooms.State.HidePanel ? Visibility.Collapsed : Visibility.Visible;
        // A new exchange changes a room's count and date; a summary or a rename changes its line.
        Loaded += (_, _) => { buddy.Rooms.Changed += Build; buddy.StageTalks.Changed += Build; Build(); };
        Unloaded += (_, _) => { buddy.Rooms.Changed -= Build; buddy.StageTalks.Changed -= Build; };
    }
    public bool Open
    {
        get => Visibility == Visibility.Visible;
        set { if (value == Open) return; Visibility = value ? Visibility.Visible : Visibility.Collapsed; buddy.Rooms.State.HidePanel = !value; buddy.Rooms.Save(); }
    }
    // The page whose rooms are listed ("" = none yet) and what the settings window calls it.
    public void Load(string page, string title) { this.page = page; this.title = title; Build(); }
    // "오늘 14:05", "어제", "3월 2일", "2025. 3. 2."
    public static string When(DateTimeOffset at, DateTime now)
    {
        var day = at.ToLocalTime().Date; int ago = (now.Date - day).Days;
        return ago <= 0 ? "오늘 " + at.ToLocalTime().ToString("HH:mm") : ago == 1 ? "어제" : day.Year == now.Year ? $"{day.Month}월 {day.Day}일" : $"{day.Year}. {day.Month}. {day.Day}.";
    }
    void Build()
    {
        list.Children.Clear(); add.IsEnabled = page.Length > 0;
        if (page.Length == 0) { filter.Visibility = Visibility.Collapsed; list.Children.Add(Pastel.Text("서로 다른 두 캐릭터를 고르면 방이 보여요.", 12)); return; }
        var rooms = buddy.Rooms.Rooms(page); var open = buddy.Rooms.Current(page); string words = filter.Text.Trim();
        filter.Visibility = rooms.Count >= 6 || words.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var room in rooms) if (words.Length == 0 || room.Name.Contains(words, StringComparison.OrdinalIgnoreCase)) list.Children.Add(Card(room, ReferenceEquals(room, open)));
    }
    Border Card(StageRoom room, bool open)
    {
        var turns = buddy.StageTalks.Turns(StageRooms.Thread(page, room));
        string about = (turns.Count == 0 ? "아직 대화 없음" : $"대화 {turns.Count}개 · {When(turns[^1].At, DateTime.Now)}") + (room.Note.Length > 0 ? " · 노트" : "") + (room.Summary.Length > 0 ? " · 기억" : "");
        var gear = new Button { Content = "⚙", FontSize = 14, Padding = new Thickness(6, 2, 6, 3), Foreground = Pastel.Ink, Opacity = .7, VerticalAlignment = VerticalAlignment.Center, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "방 설정: 이름 · 작가 노트 · 기억 요약 · 삭제" };
        Pastel.Ghost(gear); gear.Click += (_, _) => Settings(room); DockPanel.SetDock(gear, Dock.Right);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = room.Name, Foreground = Pastel.Ink, FontWeight = FontWeights.Bold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = about, Foreground = Pastel.Ink, Opacity = .72, FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        var inside = new DockPanel(); inside.Children.Add(gear); inside.Children.Add(text);
        var card = new Border
        {
            Child = inside, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(open ? 8 : 10, 7, 2, 8), CornerRadius = new CornerRadius(10), ToolTip = room.Name,
            Background = open ? Pastel.CardPink : Pastel.Paper, BorderBrush = open ? Pastel.Hot : Pastel.Line, BorderThickness = open ? new Thickness(4, 1.2, 1.2, 1.2) : new Thickness(1.2)
        };
        card.MouseLeftButtonUp += (_, _) => Pick(room);
        return card;
    }
    bool Pick(StageRoom room)
    {
        if (busy()) return false;
        if (!ReferenceEquals(buddy.Rooms.Current(page), room)) { buddy.Rooms.Select(page, room); changed(); }
        return true;
    }
    // The settings are of the room on show, so the room is opened first.
    void Settings(StageRoom room)
    {
        if (!Pick(room)) return;
        new StageRoomWindow(buddy, page, title) { Owner = owner, ShowInTaskbar = false }.ShowDialog(); changed();
    }
    void Add()
    {
        if (busy() || page.Length == 0) return;
        try { Settings(buddy.Rooms.Add(page, "채팅방 " + (buddy.Rooms.Rooms(page).Count + 1))); }
        catch (InvalidOperationException e) { MessageBox.Show(owner, e.Message, "채팅방", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
}
