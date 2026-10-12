using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardWorker;

// A character's own page: the character stands before a background and talks with the user, one line at a time (StageView).
// The talk is kept in chat rooms (PageFrame's list); what is said comes from Buddy.SendOnPage / StageTalk. Pictures, the
// prompt and the length are set in StageSettingsWindow. Text messages are another window (MessengerWindow) that shares nothing with this one.
public sealed class CharacterWindow : Window
{
    readonly Buddy buddy; readonly PageFrame frame; readonly StageView view = new(); readonly HeartMeter hearts = new(18);
    readonly ComboBox who = new() { DisplayMemberPath = "Label", MinWidth = 170 }, modes = new() { DisplayMemberPath = "Name", MinWidth = 110, Margin = new Thickness(8, 0, 0, 0), ToolTip = "페이지 모드 (추가 모드가 넣은 것)" };
    readonly TextBlock closeness = new() { FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 7, 1) }, status = Pastel.Text("", 12);
    readonly TextBox input; readonly Button send, again, gift;
    sealed record Choice(Buddy.StageCharacter Character, string Label);
    StageMode mode; CancellationTokenSource? request; ChatTurn[] turns = Array.Empty<ChatTurn>(); int cursor = -1; bool busy, filling;
    Buddy.StageCharacter Current => ((Choice)who.SelectedItem).Character;
    // The page (this character in this mode) and the thread of the chat room open on it.
    string Page => mode.Thread(Current.Id);
    string Thread => buddy.Rooms.Thread(Page);
    public CharacterWindow(Buddy buddy)
    {
        this.buddy = buddy; mode = buddy.StageModeNow; closeness.Foreground = Pastel.Hot; view.LowPower = buddy.Pref.LowPower;
        frame = new PageFrame(buddy, this, StageMode.Stage, () => busy, Enter, () => Current.Id); frame.Left(who); frame.Left(modes);
        frame.Tool("대화 기록", "여기서 나눈 대화를 다시 보고, 고치고, 모아 두고, 정리해요", () => { if (busy) return; new StageHistoryWindow(buddy, Thread) { Owner = this, ShowInTaskbar = false }.ShowDialog(); Enter(); });
        // Gifts are handed over here and answered here; the pets on the desktop take none.
        gift = frame.Tool("선물하기", "가방에 있는 선물을 건네요", async () => { if (busy) return; var picker = new GiftPicker(buddy, Current) { Owner = this, ShowInTaskbar = false }; if (picker.ShowDialog() == true && picker.Chosen != null) await Give(picker.Chosen); });
        frame.Tool("페이지 설정", "스탠딩 그림 · 배경 · 프롬프트와 길이", () => { new StageSettingsWindow(buddy, Current) { Owner = this, ShowInTaskbar = false }.ShowDialog(); view.Forget(); Enter(); });

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); frame.Root.Children.Add(bottom);
        var entry = PageFrame.Entry(async () => await Say(), () => request?.Cancel(), () => busy, 6000); input = entry.Input; send = entry.Send;
        again = Pastel.Button("↻ 다시", async () => await Again(), true); again.Padding = new Thickness(12, 6, 12, 6); again.Margin = new Thickness(6, 0, 0, 0); again.VerticalAlignment = VerticalAlignment.Bottom; again.ToolTip = "방금 답변이 마음에 들지 않으면 지우고 다시 받아요";
        DockPanel.SetDock(again, Dock.Right); entry.Row.Children.Add(again); entry.Row.Children.Add(input); bottom.Children.Add(entry.Row); status.Margin = new Thickness(2, 6, 0, 0); bottom.Children.Add(status);

        // Closeness as hearts, small, in the upper right corner of the stage.
        var paper = ((SolidColorBrush)Pastel.Paper).Color; var glass = new SolidColorBrush(Color.FromArgb(215, paper.R, paper.G, paper.B)); glass.Freeze();
        var badge = new StackPanel { Orientation = Orientation.Horizontal }; badge.Children.Add(closeness); badge.Children.Add(hearts);
        var corner = new Border { Child = badge, Background = glass, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(13), Padding = new Thickness(7, 3, 5, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 0) };
        var stage = new Grid(); stage.Children.Add(view); stage.Children.Add(corner);
        frame.Root.Children.Add(new Border { Child = stage, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 3, 3.5), CornerRadius = new CornerRadius(12), ClipToBounds = true });
        // Past the last page of a line a click goes on to the next exchange; before the first page, back to the one before.
        view.Ended += () => { if (!busy && cursor < turns.Length - 1) Turn(1); }; view.Began += () => { if (!busy && cursor > 0 && turns.Length > 0) { Turn(-1); view.ShowLastPage(); } };

        filling = true;
        who.ItemsSource = buddy.StageCharacters().Select(c => new Choice(c, c.Role + " · " + c.Title)).ToArray(); who.SelectedIndex = 0;
        var all = StageMode.Of(StageMode.Stage); modes.ItemsSource = all; modes.SelectedItem = mode; modes.Visibility = all.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        filling = false;
        who.SelectionChanged += (_, _) => { if (!filling && who.SelectedItem != null) Enter(); };
        modes.SelectionChanged += (_, _) => { if (filling || modes.SelectedItem is not StageMode chosen) return; mode = chosen; buddy.Stage.State.Mode = chosen.Id; buddy.Stage.Save(); Enter(); };
        buddy.Adventure.Changed += Closeness; Closed += (_, _) => { buddy.Adventure.Changed -= Closeness; request?.Cancel(); };
        Pastel.Window(this, "캐릭터 페이지", frame.Shell, 1120, 820, PageFrame.Sky); MinWidth = 900; MinHeight = 560; Fonts(); Enter();
        Loaded += (_, _) => input.Focus(); Activated += (_, _) => Fonts();
    }
    public void Select(string id) { foreach (Choice choice in who.Items) if (choice.Character.Id == id) { who.SelectedItem = choice; return; } }
    void Fonts() => view.SetFont(FontAssets.Get(buddy.Pref.StageFontPath), buddy.Pref.StageFontSize);

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
        view.Show(buddy.Stage.State.Backdrop, sprite?.Path ?? "");
        view.EmptyNote = sprites.Count == 0 ? "아직 스탠딩 그림이 없어요.\n오른쪽 위 '페이지 설정'에서 " + Current.Name + "의 그림을 넣어 보세요.\n파일 이름을 smile, sad, angry처럼 지으면 감정이 자동으로 인식돼요." : "";
    }
    void History() { turns = buddy.StageTalks.Turns(Thread).ToArray(); cursor = turns.Length - 1; again.IsEnabled = !busy && turns.Length > 0; }
    // A line on the stage, with whether a click may go on to a later exchange or back to an earlier one.
    void Put(string text, bool typed) { view.Earlier = cursor > 0 && turns.Length > 0; view.Continues = cursor < turns.Length - 1; view.Present(Current.Name, text, typed); }
    // Switching to a character, a mode or a room: its pictures, its own thread of talk, and a greeting it wrote (or its last line).
    void Enter()
    {
        var current = Current; string name = current.Name + (mode.Id == StageMode.BuiltIn[0].Id ? "" : " · " + mode.Name); Closeness(); Stand(""); frame.Rooms.Load(Page, name);
        Title = name + (buddy.Rooms.Rooms(Page).Count > 1 ? " · " + buddy.Rooms.Current(Page).Name : ""); History();
        string avoid = ""; int stage = Affection.Stage(buddy.Adventure.Friend(current.Id, current.Name).Bond);
        var values = new Dictionary<string, string> { ["user"] = current.Persona.UserName ?? "", ["name"] = current.Name, ["partner"] = "" };
        string? greeting = LineBook.Pick(LineBook.Text(current.Book, "", "", LineBook.Greeting(DateTime.Now.Hour)), stage, values, ref avoid);
        if (greeting != null) { cursor = turns.Length; Put(greeting, true); } else Put(turns.Length > 0 ? turns[^1].Assistant : "……", false);
        status.Text = "";
    }
    void Turn(int step)
    {
        int next = Math.Clamp(cursor + step, 0, turns.Length - 1); if (turns.Length == 0 || next == cursor) return; cursor = next;
        Put(turns[cursor].Assistant, false); status.Text = $"{turns[cursor].At.ToLocalTime():yyyy-MM-dd HH:mm} · 나: {SafeFiles.OneLine(turns[cursor].User, 80)}";
    }

    // ---- talking ----
    async Task Say() { string text = input.Text.Trim(); if (text.Length > 0 && await Talk(text, "", "나: " + SafeFiles.OneLine(text, 120))) input.Clear(); }
    // The last exchange is taken back and asked again; it returns if no new answer comes.
    async Task Again()
    {
        if (busy || turns.Length == 0) return; var last = turns[^1]; string thread = Thread; buddy.StageTalks.Remove(thread, last); History();
        if (!await Talk(last.User, "", "다시 받는 중 · 나: " + SafeFiles.OneLine(last.User, 100))) { buddy.StageTalks.Import(thread, new[] { last }); History(); Put(last.Assistant, false); }
    }
    async Task Give(string present)
    {
        if (busy) return; var current = Current; (GiftResult Result, string Note) given;
        try { given = buddy.GiveOnStage(current, present); } catch (Exception e) { status.Text = e.Message; return; }
        Closeness(); string notice = GiftShop.Stars(given.Result.Gift.Stars) + " " + given.Result.Notice;
        // Without an answer (no AI connection, or it failed) the gift still counts; the notice says so.
        if (!await Talk("(" + present + "을(를) 선물로 건넨다)", given.Note, notice)) { status.Text = notice + " · " + status.Text; return; }
        // The rarest gifts keep what was said on receiving them.
        if (given.Result.Gift.Effect == "keep" && turns.Length > 0 && StageScript.Speech(turns[^1].Assistant) is { Length: > 0 } line && buddy.Collection.Add(current.Id, current.Name, line)) status.Text = notice + "  ·  ✦ 이 한마디를 대사 도감에 담았어요";
    }
    void Busy(bool now) { busy = now; send.Content = now ? "중단" : "보내기"; who.IsEnabled = modes.IsEnabled = input.IsEnabled = gift.IsEnabled = !now; again.IsEnabled = !now && turns.Length > 0; }
    // One exchange: the message goes out, the answer comes in with its standing picture and perhaps a new background. False when no answer came.
    async Task<bool> Talk(string text, string note, string shown)
    {
        if (busy) return false; var current = Current; var sprites = buddy.Stage.Set(current.Id).Sprites;
        Busy(true); status.Text = shown; view.Asked(current.Name); bool streamed = false, answered = false; request = new CancellationTokenSource();
        try
        {
            var reply = await buddy.SendOnPage(current.Id, mode, text, request.Token, partial => { if (partial.Length > 0) { streamed = true; view.Streaming(current.Name, partial); } }, note);
            if (buddy.Stage.Move(reply.Backdrop, text + "\n" + reply.Turn.Assistant) is { } scene) status.Text = shown + "  ·  배경: " + scene.Title;
            Stand(StageTags.Choose(sprites, reply.Tag, reply.Mood)?.Path ?? "");
            History(); Put(reply.Turn.Assistant, !streamed); answered = true;
        }
        catch (OperationCanceledException) { status.Text = "답변 만들기를 멈췄어요. 저장하지 않았습니다."; Restore(); }
        catch (Exception e) { status.Text = "답변을 받지 못했어요. " + e.Message; Restore(); }
        finally { request?.Dispose(); request = null; Busy(false); if (IsVisible) input.Focus(); }
        return answered;
    }
    void Restore() => Put(cursor >= 0 && cursor < turns.Length ? turns[cursor].Assistant : "……", false);
}
