using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KeyboardWorker;

// A character's own page: how close the two of you are (hearts), and a talk with the character. How the talk looks is up to
// the surface of the page mode in use (StageView, MessengerView); what is said comes from Buddy.SendOnStage / StageTalk.
// Pictures, the prompt and the length are set in StageSettingsWindow.
public sealed class CharacterWindow : Window
{
    readonly Buddy buddy; readonly ComboBox who = new() { DisplayMemberPath = "Label", MinWidth = 160 }, modes = new() { DisplayMemberPath = "Name", MinWidth = 110, Margin = new Thickness(8, 0, 0, 0), ToolTip = "페이지 모드" }, rooms = new() { DisplayMemberPath = "Name", MinWidth = 110, Margin = new Thickness(8, 0, 0, 0), ToolTip = "채팅방 (방마다 대화가 따로 이어져요)" };
    readonly TextBlock closeness = new() { FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 7, 1) }, status = Pastel.Text("", 12);
    readonly TextBox input = new() { MaxLength = 6000, Padding = new Thickness(10, 7, 10, 7), FontSize = 15 };
    readonly HeartMeter hearts = new(18); readonly Grid holder = new();
    readonly Button send, stop, older, newer, gift;
    sealed record Choice(Buddy.StageCharacter Character, string Label);
    IStageSurface surface = null!; StageMode mode = StageMode.BuiltIn[0];
    CancellationTokenSource? request; ChatTurn[] turns = Array.Empty<ChatTurn>(); int cursor = -1; string picture = ""; bool busy, filling;
    Buddy.StageCharacter Current => ((Choice)who.SelectedItem).Character;
    // The page (this character in this mode) and the thread of the chat room open on it.
    string Page => mode.Thread(Current.Id);
    string Thread => buddy.Rooms.Thread(Page);
    public CharacterWindow(Buddy buddy)
    {
        this.buddy = buddy; closeness.Foreground = Pastel.Hot;
        var root = new DockPanel { Margin = new Thickness(16) };

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(actions, Dock.Right); top.Children.Add(actions);
        // Left: whose page, how, and which room. Right: what belongs to the room (its settings, its record), then the rest.
        void Tool(string text, string tip, Action action) => actions.Children.Add(StageParts.Tool(text, tip, action));
        Tool("채팅방", "방 만들기 · 작가 노트 · 기억 요약", () => { if (busy) return; new StageRoomWindow(buddy, Page, Current.Name + " · " + mode.Name) { Owner = this, ShowInTaskbar = false }.ShowDialog(); Enter(); });
        Tool("대화 기록", "여기서 나눈 대화를 다시 보고, 고치고, 모아 두고, 정리해요", () => { if (busy) return; new StageHistoryWindow(buddy, Thread) { Owner = this, ShowInTaskbar = false }.ShowDialog(); Enter(); });
        // Gifts are handed over here and answered here; the pets on the desktop take none.
        gift = StageParts.Tool("선물하기", "가방에 있는 선물을 건네요", async () => { if (busy) return; var picker = new GiftPicker(buddy, Current) { Owner = this, ShowInTaskbar = false }; if (picker.ShowDialog() == true && picker.Chosen != null) await Give(picker.Chosen); }); actions.Children.Add(gift);
        Tool("둘만의 페이지", "두 캐릭터가 나 없이 둘이서 이야기하는 페이지", buddy.OpenPairPage);
        Tool("페이지 설정", "스탠딩 그림 · 배경 · 프롬프트와 길이", () => { new StageSettingsWindow(buddy, Current) { Owner = this, ShowInTaskbar = false }.ShowDialog(); surface.Forget(); Enter(); });
        foreach (var box in new[] { who, modes, rooms }) { box.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(box, Dock.Left); top.Children.Add(box); }
        top.Children.Add(new Border());   // the space between the two sides

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var row = new DockPanel(); var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right); row.Children.Add(buttons);
        var pages = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(pages, Dock.Left); row.Children.Add(pages);
        Button Small(Panel panel, string text, Action action, bool blue, string tip) { var button = Pastel.Button(text, action, blue); button.Padding = new Thickness(12, 6, 12, 6); button.Margin = new Thickness(panel == pages ? 0 : 6, 0, panel == pages ? 6 : 0, 0); button.ToolTip = tip; panel.Children.Add(button); return button; }
        older = Small(pages, "◀", () => Turn(-1), true, "이전 대화"); newer = Small(pages, "▶", () => Turn(1), true, "다음 대화");
        send = Small(buttons, "보내기", async () => await Say(), false, "Enter"); stop = Small(buttons, "중단", () => request?.Cancel(), true, "답변 만들기를 멈춰요"); stop.IsEnabled = false;
        row.Children.Add(input); bottom.Children.Add(row); status.Margin = new Thickness(2, 6, 0, 0); bottom.Children.Add(status);
        input.KeyDown += async (_, e) => { if (e.Key == Key.Enter && !busy) { e.Handled = true; await Say(); } };

        // Closeness as hearts, small, in the upper right corner of the page whatever the surface.
        var paper = ((SolidColorBrush)Pastel.Paper).Color; var glass = new SolidColorBrush(Color.FromArgb(215, paper.R, paper.G, paper.B)); glass.Freeze();
        var badge = new StackPanel { Orientation = Orientation.Horizontal }; badge.Children.Add(closeness); badge.Children.Add(hearts);
        var corner = new Border { Child = badge, Background = glass, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(13), Padding = new Thickness(7, 3, 5, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 0) };
        var frame = new Grid(); frame.Children.Add(holder); frame.Children.Add(corner);
        root.Children.Add(new Border { Child = frame, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 3, 3.5), CornerRadius = new CornerRadius(12), ClipToBounds = true });

        filling = true;
        who.ItemsSource = buddy.StageCharacters().Select(c => new Choice(c, c.Role + " · " + c.Title)).ToArray(); who.SelectedIndex = 0;
        modes.ItemsSource = StageMode.All; modes.SelectedItem = StageMode.All.FirstOrDefault(m => m.Id == buddy.PageMode.Id) ?? StageMode.All[0];
        filling = false;
        who.SelectionChanged += (_, _) => { if (!filling && who.SelectedItem != null) Enter(); };
        rooms.SelectionChanged += (_, _) => { if (!filling && rooms.SelectedItem is StageRoom room) { buddy.Rooms.Select(Page, room); Enter(); } };
        modes.SelectionChanged += (_, _) => { if (filling || modes.SelectedItem is not StageMode chosen) return; buddy.Stage.State.Mode = chosen.Id; buddy.Stage.Save(); Surface(); Enter(); };
        buddy.Adventure.Changed += Closeness; Closed += (_, _) => { buddy.Adventure.Changed -= Closeness; request?.Cancel(); };
        Pastel.Window(this, "캐릭터 페이지", root, 920, 820); MinWidth = 880; MinHeight = 560; Surface(); Enter();
        Loaded += (_, _) => input.Focus(); Activated += (_, _) => Fonts();
    }
    public void Select(string id) { foreach (Choice choice in who.Items) if (choice.Character.Id == id) { who.SelectedItem = choice; return; } }
    // The surface of the mode in use; a mode with another layout gets a new one.
    void Surface()
    {
        var next = buddy.PageMode; bool same = surface != null && next.Layout == mode.Layout; mode = next; if (same) return;
        surface = StageSurfaces.For(mode.Layout); surface.LowPower = buddy.Pref.LowPower; holder.Children.Clear(); holder.Children.Add(surface.Element); Fonts();
        older.Visibility = newer.Visibility = surface.Scrolls ? Visibility.Collapsed : Visibility.Visible;
        // Past the last page of a line a click goes on to the next exchange; before the first page, back to the one before.
        if (surface is StageView view) { view.Ended += () => { if (!busy && cursor < turns.Length - 1) Turn(1); }; view.Began += () => { if (!busy && cursor > 0 && turns.Length > 0) { Turn(-1); view.ShowLastPage(); } }; }
    }
    void Put(string text, bool typed)
    {
        if (surface is StageView view) { view.Earlier = cursor > 0 && turns.Length > 0; view.Continues = cursor < turns.Length - 1; }
        surface.Present(Current.Name, text, typed);
    }
    void Fonts() => surface.SetFont(FontAssets.Get(buddy.Pref.StageFontPath), buddy.Pref.StageFontSize);

    // ---- the character ----
    void Closeness()
    {
        if (who.SelectedItem == null) return; var friend = buddy.Adventure.Friend(Current.Id, Current.Name); int stage = Affection.Stage(friend.Bond);
        closeness.Text = Affection.Name(stage); closeness.ToolTip = stage + "단계"; hearts.Bond = friend.Bond;
        hearts.ToolTip = $"친밀도 {friend.Bond}/1000 · 하트 {HeartMeter.Filled(friend.Bond):0.0}/5" + (Affection.ToNext(friend.Bond) is int left ? $"\n다음 단계까지 {left}" : "\n최고 단계") + $"\n처음 만난 날 {friend.Met}";
    }
    void Stand(string wanted)
    {
        var sprites = buddy.Stage.Set(Current.Id).Sprites; var sprite = sprites.FirstOrDefault(s => s.Path == wanted) ?? StageTags.Choose(sprites, StageTags.Default, "neutral");
        picture = sprite?.Path ?? ""; surface.Show(buddy.Stage.State.Backdrop, picture);
        surface.EmptyNote = sprites.Count == 0 ? "아직 스탠딩 그림이 없어요.\n오른쪽 위 '페이지 설정'에서 " + Current.Name + "의 그림을 넣어 보세요.\n파일 이름을 smile, sad, angry처럼 지으면 감정이 자동으로 인식돼요." : "";
    }
    void History() { turns = buddy.StageTalks.Turns(Thread).ToArray(); cursor = turns.Length - 1; }
    // Switching to a character (or a mode): its pictures, its own thread of talk, and a greeting it wrote (or its last line).
    void Enter()
    {
        var current = Current; Closeness(); Stand("");
        filling = true; var all = buddy.Rooms.Rooms(Page).ToArray(); var open = buddy.Rooms.Current(Page); rooms.ItemsSource = all; rooms.SelectedItem = open; filling = false;
        Title = current.Name + " · " + mode.Name + (all.Length > 1 ? " · " + open.Name : ""); History(); surface.Load(current.Name, turns);
        if (!surface.Scrolls)
        {
            string avoid = ""; int stage = Affection.Stage(buddy.Adventure.Friend(current.Id, current.Name).Bond);
            var values = new Dictionary<string, string> { ["user"] = current.Persona.UserName ?? "", ["name"] = current.Name, ["partner"] = "" };
            string? greeting = LineBook.Pick(LineBook.Text(current.Book, "", "", LineBook.Greeting(DateTime.Now.Hour)), stage, values, ref avoid);
            if (greeting != null) { cursor = turns.Length; Put(greeting, true); }
            else Put(turns.Length > 0 ? turns[^1].Assistant : "……", false);
        }
        status.Text = turns.Length > 0 ? $"이 캐릭터와 여기서 나눈 대화 {turns.Length}개가 이어집니다." + (surface.Scrolls ? "" : " 화면을 누르면 다음, 오른쪽 클릭이나 '◀ 이전'으로 돌아가요.") : "아래에 말을 걸어 보세요. AI 연결이 필요해요 (설정 → 말 걸기·AI → AI 연결).";
        Pages();
    }
    void Pages() { older.IsEnabled = !busy && cursor > 0; newer.IsEnabled = !busy && cursor < turns.Length - 1; }
    void Turn(int step)
    {
        int next = Math.Clamp(cursor + step, 0, turns.Length - 1); if (turns.Length == 0 || next == cursor) return; cursor = next;
        Put(turns[cursor].Assistant, false); status.Text = $"{turns[cursor].At.ToLocalTime():yyyy-MM-dd HH:mm} · 나: {SafeFiles.OneLine(turns[cursor].User, 80)}"; Pages();
    }

    // ---- talking ----
    async Task Say() { string text = input.Text.Trim(); if (text.Length > 0 && await Talk(text, "", "나: " + SafeFiles.OneLine(text, 120))) input.Clear(); }
    async Task Give(string present)
    {
        if (busy) return; var current = Current; (GiftResult Result, string Note) given;
        try { given = buddy.GiveOnStage(current, present); } catch (Exception e) { status.Text = e.Message; return; }
        Closeness(); string notice = GiftShop.Stars(given.Result.Gift.Stars) + " " + given.Result.Notice;
        // Without an answer (no AI connection, or it failed) the gift still counts; the notice says so.
        if (!await Talk("(" + present + "을(를) 선물로 건넨다)", given.Note, notice)) { status.Text = notice + " · " + status.Text; return; }
        // The rarest gifts keep what was said on receiving them.
        if (given.Result.Gift.Effect == "keep" && turns.Length > 0 && StageScript.Speech(turns[^1].Assistant) is { Length: > 0 } line && buddy.Collection.Add(current.Id, current.Name, line)) status.Text = notice + "  ·  ✦ 이 한마디를 대사 도감에 담았어요";
    }    // One exchange: the message goes out, the answer comes in with its standing picture and perhaps a new background. False when no answer came.
    async Task<bool> Talk(string text, string note, string shown)
    {
        if (busy) return false; var current = Current; var sprites = buddy.Stage.Set(current.Id).Sprites;
        busy = true; send.IsEnabled = false; stop.IsEnabled = true; who.IsEnabled = false; modes.IsEnabled = false; rooms.IsEnabled = false; input.IsEnabled = false; gift.IsEnabled = false; Pages(); status.Text = shown;
        surface.Asked(current.Name, text); bool streamed = false, answered = false; request = new CancellationTokenSource();
        try
        {
            var reply = await buddy.SendOnStage(current.Id, text, request.Token, partial => { if (partial.Length > 0) { streamed = true; surface.Streaming(current.Name, partial); } }, note);
            if (buddy.Stage.Move(reply.Backdrop, text + "\n" + reply.Turn.Assistant) is { } scene) status.Text = shown + "  ·  배경: " + scene.Title;
            Stand(StageTags.Choose(sprites, reply.Tag, reply.Mood)?.Path ?? "");
            History(); Put(reply.Turn.Assistant, !streamed); answered = true;
        }
        catch (OperationCanceledException) { status.Text = "답변 만들기를 멈췄어요. 저장하지 않았습니다."; Restore(); }
        catch (Exception e) { status.Text = "답변을 받지 못했어요. " + e.Message; Restore(); }
        finally
        {
            request?.Dispose(); request = null; busy = false; send.IsEnabled = true; stop.IsEnabled = false; who.IsEnabled = true; modes.IsEnabled = true; rooms.IsEnabled = true; input.IsEnabled = true; gift.IsEnabled = true; Pages();
            if (IsVisible) input.Focus();
        }
        return answered;
    }
    void Restore() { surface.Failed(); if (!surface.Scrolls) Put(cursor >= 0 && cursor < turns.Length ? turns[cursor].Assistant : "……", false); }
}
