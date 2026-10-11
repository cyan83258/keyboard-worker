using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// The chat rooms of one page (a character in a mode, or a pair): make, rename, delete and switch rooms, and for the room
// on show its author's note and its memory. What is typed is kept when the room is switched or the window is closed.
public sealed class StageRoomWindow : Window
{
    readonly Buddy buddy; readonly string key;
    readonly ComboBox rooms = new() { DisplayMemberPath = "Name", MinWidth = 200, VerticalAlignment = VerticalAlignment.Center }, writer = new() { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBox name = new() { MaxLength = 24, Padding = new Thickness(8, 5, 8, 5) }, note = Area(130, StageRoom.MaxNote), memory = Area(210, StageRoom.MaxSummary);
    readonly CheckBox auto = new() { Margin = new Thickness(0, 2, 0, 6) }; readonly TextBlock pending = Pastel.Text("", 12), status = Pastel.Text("", 12); readonly Button summarize;
    StageRoom room; string loaded = ""; bool filling; CancellationTokenSource? request;
    const string Same = "대화에 쓰는 AI 그대로";
    static TextBox Area(double height, int max) => new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = height, MaxLength = max, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13, Padding = new Thickness(6) };
    static StackPanel Head(string title, string help)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 4) }; var mark = Pastel.Help(help); mark.Margin = new Thickness(7, 0, 0, 0); mark.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(new TextBlock { Text = title, Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 15 }); head.Children.Add(mark); return head;
    }
    public StageRoomWindow(Buddy buddy, string key, string page)
    {
        this.buddy = buddy; this.key = key; room = buddy.Rooms.Current(key); var body = new StackPanel(); var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(status); footer.Children.Add(Pastel.Button("닫기", Close, true)); root.Children.Add(Pastel.Scroll(body));

        body.Children.Add(Head("채팅방 · " + page, "방마다 대화가 따로 이어지고, 작가 노트와 기억도 방마다 따로예요. 여기서 고른 방이 페이지에 열립니다. 적은 내용은 방을 바꾸거나 창을 닫을 때 저장돼요."));
        var pick = new WrapPanel(); pick.Children.Add(rooms);
        pick.Children.Add(Space(Pastel.Button("새 방", Add))); pick.Children.Add(Space(Pastel.Button("이 방 삭제", Remove, true))); body.Children.Add(pick);
        body.Children.Add(Pastel.Text("방 이름", 13)); body.Children.Add(name);

        body.Children.Add(Head("작가 노트", "이 방에서만 프롬프트에 덧붙는 지시예요. 상황 설정, 꼭 지킬 규칙, 문체, 지금 진행 중인 이야기의 방향 같은 것을 적어 두면 매 답변에 반영됩니다. 비워 두면 아무것도 붙지 않아요."));
        body.Children.Add(note);

        body.Children.Add(Head("기억 요약", "이 방에서 있었던 일을 간추린 기억이에요. 오래된 대화는 AI에게 다시 보여 주지 않으므로, 중요한 일은 이 요약으로 기억합니다. AI가 자동으로 쓰게 하거나, '지금 요약'으로 시키거나, 직접 고쳐 쓸 수 있어요."));
        auto.Content = "새 대화가 " + buddy.Rooms.State.SummaryEvery + "개 쌓일 때마다 AI가 자동으로 요약하기"; body.Children.Add(auto); body.Children.Add(memory);
        var act = new WrapPanel(); summarize = Pastel.Button("지금 요약", Summarize); act.Children.Add(summarize); pending.VerticalAlignment = VerticalAlignment.Center; pending.Margin = new Thickness(10, 0, 0, 0); act.Children.Add(pending); body.Children.Add(act);
        body.Children.Add(Pastel.Text("요약에 쓸 AI (모든 방 공통)", 13));
        var names = new[] { Same }.Concat(buddy.Chat.Preferences.Connections.Select(c => c.Name).Distinct()).ToArray(); writer.ItemsSource = names;
        writer.SelectedItem = names.FirstOrDefault(n => string.Equals(n, buddy.Rooms.State.SummaryConnection, StringComparison.OrdinalIgnoreCase)) ?? Same;
        writer.ToolTip = "요약만 다른 모델이나 주소로 보내고 싶으면, 설정 → 말 걸기·AI → AI 연결에서 연결을 하나 더 만든 뒤 여기서 고르세요.";
        var ai = new WrapPanel(); ai.Children.Add(writer); ai.Children.Add(Space(Pastel.Button("AI 연결 관리…", buddy.OpenConnections, true))); body.Children.Add(ai);

        rooms.SelectionChanged += (_, _) => { if (filling || rooms.SelectedItem is not StageRoom chosen || ReferenceEquals(chosen, room)) return; Apply(); room = chosen; buddy.Rooms.Select(key, room); Display(); };
        writer.SelectionChanged += (_, _) => { buddy.Rooms.State.SummaryConnection = writer.SelectedItem as string is { } chosen && chosen != Same ? chosen : ""; buddy.Rooms.Save(); };
        buddy.Rooms.Changed += Arrived; Closing += (_, _) => { request?.Cancel(); Apply(); }; Closed += (_, _) => buddy.Rooms.Changed -= Arrived;
        Pastel.Window(this, "채팅방", root, 640, 800); MinWidth = 520; MinHeight = 520; Fill(); Display();
    }
    static Button Space(Button button) { button.Margin = new Thickness(8, 0, 0, 0); return button; }
    void Fill() { filling = true; rooms.ItemsSource = buddy.Rooms.Rooms(key).ToArray(); rooms.SelectedItem = room; filling = false; }
    void Display()
    {
        name.Text = room.Name; note.Text = room.Note; memory.Text = loaded = room.Summary; auto.IsChecked = room.AutoSummary; Count();
    }
    void Count()
    {
        var turns = buddy.StageTalks.Turns(StageRooms.Thread(key, room)); int fresh = StageMemory.Pending(turns, room).Length;
        pending.Text = turns.Count == 0 ? "아직 이 방에서 나눈 대화가 없어요." : $"대화 {turns.Count}개 중 아직 요약에 없는 것 {fresh}개";
    }
    // What is typed goes into the room.
    void Apply()
    {
        string title = SafeFiles.OneLine(name.Text, 24);
        bool changed = (title.Length > 0 && title != room.Name) || note.Text.Trim() != room.Note || memory.Text.Trim() != room.Summary || (auto.IsChecked == true) != room.AutoSummary; if (!changed) return;
        if (title.Length > 0) room.Name = title; room.Note = note.Text; room.Summary = memory.Text; room.AutoSummary = auto.IsChecked == true; buddy.Rooms.Save(); loaded = room.Summary; Fill();
    }
    // A summary written in the background arrived: it is shown unless the user is in the middle of rewriting the old one.
    void Arrived() { if (memory.Text == loaded && room.Summary != loaded) memory.Text = loaded = room.Summary; Count(); }
    void Add()
    {
        Apply();
        try { room = buddy.Rooms.Add(key, "채팅방 " + (buddy.Rooms.Rooms(key).Count + 1)); Fill(); Display(); status.Text = "새 방을 만들었어요. 이름을 바꾸고 작가 노트를 적어 보세요."; name.Focus(); name.SelectAll(); }
        catch (InvalidOperationException e) { status.Text = e.Message; }
    }
    void Remove()
    {
        if (buddy.IsChatBusy) { status.Text = "답변을 만드는 중이에요. 끝난 뒤에 다시 해 주세요."; return; }
        if (buddy.Rooms.Rooms(key).Count < 2) { status.Text = "방은 하나는 있어야 해요. 대화만 지우려면 '대화 기록'의 정리를 써 주세요."; return; }
        int talks = buddy.StageTalks.Turns(StageRooms.Thread(key, room)).Count;
        if (MessageBox.Show(this, "'" + room.Name + "' 방을 삭제할까요?\n이 방의 대화 " + talks + "개와 작가 노트, 기억 요약이 함께 지워져요. ('모아 둔 것'은 남아요.)", "채팅방 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var gone = room; if (!buddy.Rooms.Remove(key, gone)) return;
        buddy.StageTalks.Clear(StageRooms.Thread(key, gone)); room = buddy.Rooms.Current(key); Fill(); Display(); status.Text = "'" + gone.Name + "' 방을 삭제했어요.";
    }
    async void Summarize()
    {
        if (request != null) return; Apply(); var target = room; request = new CancellationTokenSource(); summarize.IsEnabled = rooms.IsEnabled = false; status.Text = "요약하는 중…";
        try { status.Text = await buddy.Remember(StageRooms.Thread(key, target), target, true, request.Token); if (ReferenceEquals(target, room)) { memory.Text = loaded = room.Summary; Count(); } }
        catch (OperationCanceledException) { }
        catch (Exception e) { status.Text = "요약하지 못했어요. " + e.Message; }
        finally { request.Dispose(); request = null; summarize.IsEnabled = rooms.IsEnabled = true; }
    }
}
