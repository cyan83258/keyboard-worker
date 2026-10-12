using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// Text messages with a character: a chat log (MessengerView) with its own chat rooms, prompt and length. It shares nothing
// with the character page - not the rooms, not the threads, not the settings. An exchange is edited, deleted or asked
// again right in the log, so there is no separate record window here.
public sealed class MessengerWindow : Window
{
    readonly Buddy buddy; readonly PageFrame frame; readonly MessengerView view = new();
    readonly ComboBox who = new() { DisplayMemberPath = "Label", MinWidth = 170 }, modes = new() { DisplayMemberPath = "Name", MinWidth = 110, Margin = new Thickness(8, 0, 0, 0), ToolTip = "문자 모드 (추가 모드가 넣은 것)" };
    readonly TextBlock status = Pastel.Text("", 12); readonly TextBox input; readonly Button send;
    sealed record Choice(Buddy.StageCharacter Character, string Label);
    StageMode mode; CancellationTokenSource? request; bool busy, filling;
    Buddy.StageCharacter Current => ((Choice)who.SelectedItem).Character;
    string Page => mode.Thread(Current.Id);
    string Thread => buddy.Rooms.Thread(Page);
    public MessengerWindow(Buddy buddy)
    {
        this.buddy = buddy; mode = buddy.TextModeNow;
        frame = new PageFrame(buddy, this, StageMode.Messenger, () => busy, Enter, () => Current.Id); frame.Left(who); frame.Left(modes);
        frame.Tool("문자 설정", "문자 메시지의 프롬프트 · 길이 · 토큰 (마주 보기와 따로예요)", () => { new StageSettingsWindow(buddy, Current, true) { Owner = this, ShowInTaskbar = false }.ShowDialog(); Enter(); });

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); frame.Root.Children.Add(bottom);
        var entry = PageFrame.Entry(async () => await Say(), () => request?.Cancel(), () => busy, 6000); input = entry.Input; send = entry.Send;
        entry.Row.Children.Add(input); bottom.Children.Add(entry.Row); status.Margin = new Thickness(2, 6, 0, 0); bottom.Children.Add(status);
        frame.Root.Children.Add(new Border { Child = view, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 3, 3.5), CornerRadius = new CornerRadius(12), ClipToBounds = true });
        view.Edit += Edit; view.Delete += Delete; view.Again += async () => await Again();

        filling = true;
        who.ItemsSource = buddy.StageCharacters().Select(c => new Choice(c, c.Role + " · " + c.Title)).ToArray(); who.SelectedIndex = 0;
        var all = StageMode.Of(StageMode.Messenger); modes.ItemsSource = all; modes.SelectedItem = mode; modes.Visibility = all.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        filling = false;
        who.SelectionChanged += (_, _) => { if (!filling && who.SelectedItem != null) Enter(); };
        modes.SelectionChanged += (_, _) => { if (filling || modes.SelectedItem is not StageMode chosen) return; mode = chosen; buddy.Stage.State.TextMode = chosen.Id; buddy.Stage.Save(); Enter(); };
        Closed += (_, _) => request?.Cancel();
        Pastel.Window(this, "문자 메시지", frame.Shell, 1040, 820); MinWidth = 780; MinHeight = 520; Enter();
        Loaded += (_, _) => input.Focus();
    }
    public void Select(string id) { foreach (Choice choice in who.Items) if (choice.Character.Id == id) { who.SelectedItem = choice; return; } }
    // Switching to a character, a mode or a room: its own thread of messages.
    void Enter()
    {
        var current = Current; string name = current.Name + " · " + mode.Name; frame.Rooms.Load(Page, name);
        Title = name + (buddy.Rooms.Rooms(Page).Count > 1 ? " · " + buddy.Rooms.Current(Page).Name : "");
        view.SetFont(FontAssets.Get(buddy.Pref.StageFontPath), buddy.Pref.StageFontSize); Reload(true);
        status.Text = buddy.StageTalks.Turns(Thread).Count > 0 ? "" : "첫 메시지를 보내 보세요. AI 연결이 필요해요 (설정 → 말 걸기·AI → AI 연결).";
    }
    void Reload(bool fresh = false) => view.Load(buddy.StageTalks.Turns(Thread), fresh);
    void Edit(ChatTurn turn)
    {
        var editor = new StageTurnEditor(turn) { Owner = this, ShowInTaskbar = false };
        if (editor.ShowDialog() == true && editor.Result != null && buddy.StageTalks.Replace(Thread, turn, editor.Result)) { Reload(); status.Text = "고친 내용을 저장했어요."; }
    }
    void Delete(ChatTurn turn)
    {
        if (MessageBox.Show(this, "이 메시지와 답장을 삭제할까요?", "문자 메시지", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (buddy.StageTalks.Remove(Thread, turn)) { Reload(); status.Text = "삭제했어요."; }
    }
    async Task Say() { string text = input.Text.Trim(); if (text.Length > 0 && await Talk(text)) input.Clear(); }
    // The last exchange is taken back and asked again; it returns if no new answer comes.
    async Task Again()
    {
        string thread = Thread; var last = buddy.StageTalks.Turns(thread).LastOrDefault(); if (busy || last == null) return;
        buddy.StageTalks.Remove(thread, last); Reload();
        if (!await Talk(last.User)) { buddy.StageTalks.Import(thread, new[] { last }); Reload(); }
    }
    // One exchange. False when no answer came.
    async Task<bool> Talk(string text)
    {
        if (busy) return false; var current = Current; bool answered = false;
        busy = view.Locked = true; send.Content = "중단"; who.IsEnabled = modes.IsEnabled = input.IsEnabled = false; status.Text = current.Name + "이(가) 입력하는 중…"; view.Asked(text); request = new CancellationTokenSource();
        try { await buddy.SendOnPage(current.Id, mode, text, request.Token, partial => { if (partial.Length > 0) view.Streaming(partial); }); answered = true; status.Text = ""; }
        catch (OperationCanceledException) { status.Text = "답장 만들기를 멈췄어요. 저장하지 않았습니다."; }
        catch (Exception e) { status.Text = "답장을 받지 못했어요. " + e.Message; }
        finally
        {
            request?.Dispose(); request = null; busy = view.Locked = false; send.Content = "보내기"; who.IsEnabled = modes.IsEnabled = input.IsEnabled = true; Reload();
            if (IsVisible) input.Focus();
        }
        return answered;
    }
}
