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

// The page for two: two characters talk to each other and the user only watches or directs - by doing nothing (go on),
// by setting a situation, or by writing a line for one of them. A scene arrives whole and is played a line at a time;
// a click on the dialogue box brings the next line. The talking is in Buddy.SendPairOnStage / PairStageTalk.
public sealed class PairStageWindow : Window
{
    readonly Buddy buddy; readonly StageView view = new(); readonly HeartMeter hearts = new(18);
    readonly ComboBox first = new() { DisplayMemberPath = "Label", MinWidth = 150 }, second = new() { DisplayMemberPath = "Label", MinWidth = 150 }, kind = new() { DisplayMemberPath = "Label", MinWidth = 150, Margin = new Thickness(0, 0, 6, 0) };
    readonly TextBlock bond = new() { FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 7, 1) }, status = Pastel.Text("", 12);
    readonly TextBox input = new() { MaxLength = 2000, Padding = new Thickness(10, 7, 10, 7), FontSize = 15 }; readonly Button go, stop, again; readonly StageRoomPanel rooms; (string Kind, string Text) asked = (PairStageTalk.Continue, "");
    sealed record Choice(Buddy.StageCharacter Character, string Label); sealed record Kind(string Key, string Label);
    CancellationTokenSource? request; List<PairLine> lines = new(); int at; bool busy, filling;
    Buddy.StageCharacter A => ((Choice)first.SelectedItem).Character; Buddy.StageCharacter B => ((Choice)second.SelectedItem).Character;
    public PairStageWindow(Buddy buddy)
    {
        this.buddy = buddy; view.LowPower = buddy.Pref.LowPower; bond.Foreground = Pastel.Hot;
        var page = new PageFrame(buddy, this, StageMode.Pair, () => busy, Enter, () => first.SelectedItem == null ? null : A.Id); rooms = page.Rooms; var root = page.Root;
        page.Tool("대화 기록", "둘이 나눈 장면을 다시 보고, 고치고, 모아 두고, 정리해요", () => { if (busy || !Ready) return; new StageHistoryWindow(buddy, Thread) { Owner = this, ShowInTaskbar = false }.ShowDialog(); Enter(); });
        page.Tool("페이지 설정", "스탠딩 그림 · 배경 · 프롬프트와 길이", () => { new StageSettingsWindow(buddy, A) { Owner = this, ShowInTaskbar = false }.ShowDialog(); view.Forget(); Enter(); });
        var heart = new TextBlock { Text = "♡", Foreground = Pastel.Hot, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 0, 8, 0) };
        page.Left(first); page.Left(heart); page.Left(second);

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var row = new DockPanel(); var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right); row.Children.Add(buttons);
        Button Small(string text, Action action, bool blue, string tip) { var button = Pastel.Button(text, action, blue); button.Padding = new Thickness(14, 6, 14, 6); button.Margin = new Thickness(6, 0, 0, 0); button.ToolTip = tip; buttons.Children.Add(button); return button; }
        go = Small("▶ 진행", async () => await Play(), false, "둘의 장면을 이어 갑니다 (Enter)"); stop = Small("중단", () => request?.Cancel(), true, "장면 만들기를 멈춰요"); stop.IsEnabled = false; again = Small("↻ 다시", async () => await Again(), true, "방금 장면이 마음에 들지 않으면 지우고 다시 받아요");
        kind.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(kind, Dock.Left); row.Children.Add(kind); row.Children.Add(input); bottom.Children.Add(row); status.Margin = new Thickness(2, 6, 0, 0); bottom.Children.Add(status);
        input.KeyDown += async (_, e) => { if (e.Key == Key.Enter && !busy) { e.Handled = true; await Play(); } };
        kind.SelectionChanged += (_, _) => Hint();

        var paper = ((SolidColorBrush)Pastel.Paper).Color; var glass = new SolidColorBrush(Color.FromArgb(215, paper.R, paper.G, paper.B)); glass.Freeze();
        var badge = new StackPanel { Orientation = Orientation.Horizontal }; badge.Children.Add(bond); badge.Children.Add(hearts);
        var corner = new Border { Child = badge, Background = glass, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(13), Padding = new Thickness(7, 3, 5, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 0) };
        var frame = new Grid(); frame.Children.Add(view); frame.Children.Add(corner);
        root.Children.Add(new Border { Child = frame, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 3, 3.5), CornerRadius = new CornerRadius(12), ClipToBounds = true });
        view.Ended += Next; view.Began += Previous;

        var people = buddy.StageCharacters().Select(c => new Choice(c, c.Role + " · " + c.Title)).ToArray();
        filling = true; first.ItemsSource = people; second.ItemsSource = people; first.SelectedIndex = 0; second.SelectedIndex = people.Length > 1 ? 1 : 0; filling = false;
        first.SelectionChanged += (_, _) => { if (!filling) Enter(); }; second.SelectionChanged += (_, _) => { if (!filling) Enter(); };
        buddy.Adventure.Changed += Closeness; Closed += (_, _) => { buddy.Adventure.Changed -= Closeness; request?.Cancel(); };
        Pastel.Window(this, "둘만의 페이지", page.Shell, 1120, 820, PageFrame.Sky); MinWidth = 900; MinHeight = 560;
        view.SetFont(FontAssets.Get(buddy.Pref.StageFontPath), buddy.Pref.StageFontSize); Activated += (_, _) => view.SetFont(FontAssets.Get(buddy.Pref.StageFontPath), buddy.Pref.StageFontSize);
        Enter(); Loaded += (_, _) => input.Focus();
    }
    bool Ready => first.SelectedItem != null && second.SelectedItem != null && A.Id != B.Id;
    // The page (these two) and the thread of the chat room open on it.
    string Page => Buddy.PairThread(A.Id, B.Id);
    string Thread => buddy.Rooms.Thread(Page);
    void Hint() => input.ToolTip = (kind.SelectedItem as Kind)?.Key switch { PairStageTalk.Situation => "장소나 사건을 적어 주세요. 예: 비 오는 밤, 둘이 한 우산을 쓰고 걷는다", PairStageTalk.AsFirst or PairStageTalk.AsSecond => "그 사람의 대사나 (행동)을 직접 적어 주세요", _ => "비워 두고 진행을 누르면 둘이 알아서 이어 가요" };
    void Closeness()
    {
        if (!Ready) return; var pair = buddy.Adventure.Pair(A.Id, B.Id); hearts.Bond = pair.Bond; bond.Text = pair.Style;
        hearts.ToolTip = $"두 사람의 관계도 {pair.Bond}/1000 · 하트 {HeartMeter.Filled(pair.Bond):0.0}/5\n둘이 나눈 대화 {pair.Conversations}회";
    }
    // The pictures of both, the scene they last played, and who the user may speak for.
    void Enter()
    {
        if (first.SelectedItem == null || second.SelectedItem == null) return;
        if (!Ready) { rooms.Load("", ""); lines.Clear(); view.Show(buddy.Stage.State.Backdrop, ""); view.EmptyNote = buddy.StageCharacters().Count < 2 ? "둘만의 페이지에는 캐릭터가 두 명 필요해요.\n캐릭터 프리셋을 하나 더 만들어 주세요." : "서로 다른 두 캐릭터를 골라 주세요."; view.Present("", "……", false); go.IsEnabled = false; return; }
        rooms.Load(Page, A.Name + " ♡ " + B.Name);
        go.IsEnabled = !busy; Title = A.Name + " ♡ " + B.Name + (buddy.Rooms.Rooms(Page).Count > 1 ? " · " + buddy.Rooms.Current(Page).Name : ""); Closeness();
        string chosen = (kind.SelectedItem as Kind)?.Key ?? PairStageTalk.Continue;
        var kinds = new[] { new Kind(PairStageTalk.Continue, "둘이 알아서"), new Kind(PairStageTalk.Situation, "상황 제시"), new Kind(PairStageTalk.AsFirst, A.Name + "(으)로서"), new Kind(PairStageTalk.AsSecond, B.Name + "(으)로서") };
        kind.ItemsSource = kinds; kind.SelectedItem = kinds.FirstOrDefault(k => k.Key == chosen) ?? kinds[0]; Hint();
        bool none = buddy.Stage.Set(A.Id).Sprites.Count == 0 && buddy.Stage.Set(B.Id).Sprites.Count == 0;
        view.EmptyNote = none ? "두 사람의 스탠딩 그림이 아직 없어요.\n각자의 캐릭터 페이지 → 페이지 설정에서 넣을 수 있어요." : "";
        var last = buddy.StageTalks.Turns(Thread).LastOrDefault(); lines = last == null ? new() : PairStageTalk.FromScript(last.Assistant, A.Name, B.Name);
        status.Text = ""; again.IsEnabled = !busy && last != null; if (lines.Count > 0) { at = lines.Count - 1; Show(false); }
        else { Stand(new PairLine(false, ""), true); view.Present(A.Name, "……", false); }
    }
    string Picture(Buddy.StageCharacter who, PairLine line, bool speaking)
    {
        var sprites = buddy.Stage.Set(who.Id).Sprites;
        return (speaking ? StageTags.Choose(sprites, line.Sprite, PetMood.Guess(StageScript.Speech(line.Text))) : StageTags.Choose(sprites, StageTags.Default, "neutral"))?.Path ?? "";
    }
    // Who stands on the stage for a line: both, or only the speaker. The one not speaking keeps the picture it last had.
    string firstPicture = "", secondPicture = "";
    void Stand(PairLine line, bool reset = false)
    {
        if (reset) { firstPicture = Picture(A, line, false); secondPicture = Picture(B, line, false); }
        else if (line.Second) secondPicture = Picture(B, line, true); else firstPicture = Picture(A, line, true);
        // With no picture for the one who should stand alone, both are shown rather than an empty stage.
        bool both = line.Both || (line.Second ? secondPicture : firstPicture).Length == 0;
        view.ShowTwo(buddy.Stage.State.Backdrop, firstPicture, secondPicture, both, line.Second);
    }
    void Show(bool typed)
    {
        var line = lines[at]; Stand(line); view.Continues = at < lines.Count - 1; view.Earlier = at > 0; view.Present(line.Second ? B.Name : A.Name, line.Text, typed);
    }
    // A click past the end of a line: the next line of the scene, if there is one.
    void Next() { if (busy || at >= lines.Count - 1) return; at++; Show(true); }

    void Previous() { if (busy || at <= 0 || lines.Count == 0) return; at--; Show(false); view.ShowLastPage(); }

    // The scene just played is taken back and asked for again, with what was asked the first time; it returns if no new one comes.
    async Task Again()
    {
        string thread = Thread; var last = buddy.StageTalks.Turns(thread).LastOrDefault(); if (busy || !Ready || last == null) return;
        buddy.StageTalks.Remove(thread, last);
        if (!await Play(asked)) { buddy.StageTalks.Import(thread, new[] { last }); Enter(); }
    }
    // One scene. repeat: what to ask instead of what is typed (answering again). False when no scene came.
    async Task<bool> Play((string Kind, string Text)? repeat = null)
    {
        if (busy || !Ready) return false; var a = A; var b = B; string choice = repeat?.Kind ?? (kind.SelectedItem as Kind)?.Key ?? PairStageTalk.Continue, text = repeat?.Text ?? input.Text.Trim(); bool played = false;
        if (choice != PairStageTalk.Continue && text.Length == 0) choice = PairStageTalk.Continue;
        busy = true; go.IsEnabled = again.IsEnabled = false; stop.IsEnabled = true; first.IsEnabled = second.IsEnabled = kind.IsEnabled = input.IsEnabled = false; request = new CancellationTokenSource();
        status.Text = choice == PairStageTalk.Situation ? "상황: " + SafeFiles.OneLine(text, 100) : choice == PairStageTalk.Continue ? "두 사람이 이야기하는 중…" : (choice == PairStageTalk.AsFirst ? a.Name : b.Name) + ": " + SafeFiles.OneLine(text, 100);
        view.Continues = view.Earlier = false; view.Present(lines.Count > 0 ? (lines[^1].Second ? b.Name : a.Name) : a.Name, "…", false);
        try
        {
            var scene = await buddy.SendPairOnStage(a, b, choice, text, request.Token); if (repeat == null) input.Clear(); asked = (choice, text); played = true;
            buddy.Stage.Move(scene.Backdrop, text + "\n" + string.Join("\n", scene.Lines.Select(l => l.Text)));
            lines = scene.Lines.ToList(); at = 0; Stand(lines[0], true); Show(true); status.Text = "";
        }
        catch (OperationCanceledException) { status.Text = "장면 만들기를 멈췄어요. 저장하지 않았습니다."; Restore(); }
        catch (Exception e) { status.Text = "장면을 받지 못했어요. " + e.Message; Restore(); }
        finally
        {
            request?.Dispose(); request = null; busy = false; go.IsEnabled = Ready; again.IsEnabled = Ready && buddy.StageTalks.Turns(Thread).Count > 0; stop.IsEnabled = false; first.IsEnabled = second.IsEnabled = kind.IsEnabled = input.IsEnabled = true;
            if (IsVisible) input.Focus();
        }
        return played;
    }
    void Restore() { if (lines.Count > 0) { at = Math.Clamp(at, 0, lines.Count - 1); Show(false); } else view.Present(A.Name, "……", false); }
}
