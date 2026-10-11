using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace KeyboardWorker;

public sealed class ChatRow : INotifyPropertyChanged
{
    public ChatTurn? Turn { get; set; }
    public string UserLabel { get; init; } = "";
    public string User { get; init; } = "";
    public string Character { get; init; } = "";
    string reply = "";
    public string Reply { get => reply; set { if (reply == value) return; reply = value; PropertyChanged?.Invoke(this, new(nameof(Reply))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public static ChatRow From(ChatTurn turn, string fallback) => new() { Turn = turn, User = turn.User, Reply = turn.Assistant, UserLabel = "나 · " + turn.At.ToLocalTime().ToString("HH:mm"), Character = "♡ " + (string.IsNullOrEmpty(turn.CharacterName) ? fallback : turn.CharacterName) };
}
public sealed class ChatWindow : Window
{
    sealed record Recipient(string Key, string Name);
    readonly ComboBox recipient = new() { DisplayMemberPath = "Name", SelectedValuePath = "Key", Width = 260 };
    readonly ComboBox mode = new() { ItemsSource = DialogueModes.Rules.Keys.ToArray(), Width = 180 };
    bool syncing;
    readonly Buddy buddy;
    readonly ChatRepository repo;
    readonly ObservableCollection<ChatRow> rows = new();
    readonly ListBox history = new() { BorderThickness = new Thickness(0), Background = Pastel.Paper };
    readonly TextBox input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90, MaxLength = 6000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock status = Pastel.Text(""), caption = Pastel.Text("");
    readonly Button send;
    readonly Border greeting = Pastel.Card(Pastel.Text("안녕, 여기서 이야기하자 ♡\nAI 연결에서 키와 모델을 설정한 뒤 메시지를 보내 주세요.", 18), true);
    CancellationTokenSource? pending;
    ChatRow? draft;
    string lastReply = "";
    bool closed;
    public int DisplayedTurns => rows.Count;
    public int RealizedTurns => Enumerable.Range(0, rows.Count).Count(i => history.ItemContainerGenerator.ContainerFromIndex(i) != null);
    public ChatWindow(Buddy buddy, ChatRepository repo)
    {
        this.buddy = buddy; this.repo = repo;
        var root = new DockPanel(); var top = new StackPanel { Margin = new Thickness(16, 12, 16, 0) }; top.Children.Add(caption);
        var routing = new WrapPanel(); TextBlock RouteLabel(string t) { var l = Pastel.Text(t, 12); l.VerticalAlignment = VerticalAlignment.Center; l.Margin = new Thickness(0, 0, 8, 6); l.FontWeight = FontWeights.SemiBold; return l; }
        recipient.Margin = new Thickness(0, 4, 16, 10); routing.Children.Add(RouteLabel("대화 상대")); routing.Children.Add(recipient); routing.Children.Add(RouteLabel("모드")); routing.Children.Add(mode); top.Children.Add(routing); RefreshParticipants();
        recipient.SelectionChanged += async (_, _) => await SaveRouting(); mode.SelectionChanged += async (_, _) => await SaveRouting();
        var nav = new WrapPanel(); nav.Children.Add(Pastel.Button("프로필", buddy.OpenProfile)); nav.Children.Add(Pastel.Button("AI 연결", buddy.OpenConnections, true));
        nav.Children.Add(Pastel.Button("검색·내보내기", buddy.OpenChatSearch, true));
        nav.Children.Add(Pastel.Button("답변 복사", () => { if (lastReply.Length > 0) Clipboard.SetText(lastReply); }, true));
        nav.Children.Add(Pastel.Button("대화 비우기", () => { if (buddy.IsChatBusy) { status.Text = "먼저 답변 생성을 중단해 주세요."; return; } if (MessageBox.Show(this, "보관된 대화를 모두 삭제할까요?", "대화 비우기", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { repo.Clear(); Reset(); } }));
        top.Children.Add(nav); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new StackPanel { Margin = new Thickness(18, 8, 18, 16) }; bottom.Children.Add(status);
        var suggestions = new WrapPanel(); foreach (var text in new[] { "오늘 좀 지쳤어", "같이 작업하자", "잠깐 수다 떨자" }) suggestions.Children.Add(Pastel.Button(text, () => { input.Text = text; input.Focus(); }, true)); bottom.Children.Add(suggestions);
        bottom.Children.Add(input); var buttons = new WrapPanel(); send = Pastel.Button("♡ 보내기", async () => await Send()); buttons.Children.Add(send); buttons.Children.Add(Pastel.Button("생성 중단", buddy.CancelMessage, true)); bottom.Children.Add(buttons);
        bottom.Children.Add(Pastel.Text("Enter 전송 · Shift+Enter 줄바꿈 · 직접 전송할 때만 AI를 호출해요", 12)); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        history.ItemsSource = rows; history.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VirtualizingStackPanel.SetIsVirtualizing(history, true); VirtualizingStackPanel.SetVirtualizationMode(history, VirtualizationMode.Recycling); VirtualizingStackPanel.SetScrollUnit(history, ScrollUnit.Pixel); VirtualizingPanel.SetCacheLength(history, new VirtualizationCacheLength(0));
        ScrollViewer.SetCanContentScroll(history, true); ScrollViewer.SetHorizontalScrollBarVisibility(history, ScrollBarVisibility.Disabled);
        history.ItemContainerStyle = Pastel.PlainItem();
        history.ItemTemplate = (DataTemplate)XamlReader.Parse(Pastel.Themed("""
          <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
            <StackPanel Margin="14,6">
              <Border HorizontalAlignment="Right" Background="#DDF8FB" BorderBrush="#A85A8E" BorderThickness="1.5,1.5,3,3.5" CornerRadius="16,16,4,16" Padding="14,10" Margin="70,4,0,4">
                <StackPanel><TextBlock Text="{Binding UserLabel}" Foreground="#2F8791" FontSize="11" FontWeight="SemiBold" Margin="0,0,0,4"/><TextBlock Text="{Binding User}" Foreground="#6E3360" FontSize="14" TextWrapping="Wrap"/></StackPanel>
              </Border>
              <Border HorizontalAlignment="Left" Background="#FFFAFD" BorderBrush="#A85A8E" BorderThickness="1.5,1.5,3,3.5" CornerRadius="16,16,16,4" Padding="14,10" Margin="0,4,70,4">
                <StackPanel><TextBlock Text="{Binding Character}" Foreground="#E4479A" FontSize="11" FontWeight="Bold" Margin="0,0,0,4"/><TextBlock Text="{Binding Reply}" Foreground="#6E3360" FontSize="14" TextWrapping="Wrap"/></StackPanel>
              </Border>
            </StackPanel>
          </DataTemplate>
          """));
        var viewport = new Grid(); viewport.Children.Add(history); greeting.VerticalAlignment = VerticalAlignment.Top; greeting.Margin = new Thickness(18, 12, 18, 0); viewport.Children.Add(greeting);
        rows.CollectionChanged += (_, _) => greeting.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        root.Children.Add(viewport); Pastel.Window(this, "캐릭터 채팅", root, 680, 800);
        input.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; await Send(); } };
        foreach (var turn in repo.Turns) rows.Add(ChatRow.From(turn, repo.Preferences.Profile.Name)); RefreshCaption(); lastReply = repo.Turns.LastOrDefault()?.Assistant ?? "";
        repo.Changed += Synchronize; repo.Cleared += Reset; buddy.MessageStarted += Start; buddy.MessageProgress += Progress; buddy.MessageFailed += Fail; buddy.MessageReceived += Receive;
        buddy.PartyChanged += RefreshParticipants;
        Closed += (_, _) => { closed = true; pending?.Cancel(); repo.Changed -= Synchronize; repo.Cleared -= Reset; buddy.PartyChanged -= RefreshParticipants; buddy.MessageStarted -= Start; buddy.MessageProgress -= Progress; buddy.MessageFailed -= Fail; buddy.MessageReceived -= Receive; };
        if (buddy.IsChatBusy) Start(buddy.CurrentMessageUser);
    }
    void RefreshCaption() => caption.Text = $"♡ {buddy.ChatSpeakerLabel} · {repo.Preferences.Mode} · {repo.Preferences.Current.Name}\n보관 {repo.Turns.Count}/{repo.Preferences.KeepTurns}개 · 최근 {repo.Preferences.ContextTurns}개 참고";
    void RefreshParticipants()
    {
        syncing = true; recipient.ItemsSource = new[] { new Recipient("primary", repo.Preferences.Profile.Name), new Recipient("secondary", buddy.SecondaryPreset?.Profile.Name ?? "둘째 친구 (먼저 불러내기)"), new Recipient("both", "두 친구 모두") }; recipient.SelectedValue = repo.Preferences.Target; mode.SelectedItem = repo.Preferences.Mode; syncing = false;
    }
    async System.Threading.Tasks.Task SaveRouting()
    {
        if (syncing || closed || buddy.IsChatBusy || recipient.SelectedValue == null || mode.SelectedItem == null) return;
        try { var p = repo.Preferences.Snapshot(); p.Target = recipient.SelectedValue.ToString()!; p.Mode = mode.SelectedItem.ToString()!; await repo.ApplyAsync(p); RefreshCaption(); }
        catch (Exception e) { status.Text = e.Message; }
    }
    void Synchronize()
    {
        foreach (var row in rows.Where(r => r.Turn != null && !repo.Turns.Contains(r.Turn)).ToArray()) rows.Remove(row);
        RefreshCaption();
    }
    void Reset() { rows.Clear(); draft = null; lastReply = ""; RefreshCaption(); }
    void Start(string text)
    {
        foreach (var row in rows.Where(r => r.Turn == null).ToArray()) rows.Remove(row);
        if (repo.Preferences.KeepTurns == 0) rows.Clear();
        recipient.IsEnabled = mode.IsEnabled = false; draft = new() { User = text, UserLabel = "나 · " + DateTime.Now.ToString("HH:mm"), Character = "♡ " + buddy.ChatSpeakerLabel, Reply = "답변을 생각하는 중…" }; rows.Add(draft); history.ScrollIntoView(draft);
    }
    void Progress(string text) { if (draft != null) { draft.Reply = text; history.ScrollIntoView(draft); } }
    void Fail(string message) { recipient.IsEnabled = mode.IsEnabled = true; if (draft != null) draft.Reply = (draft.Reply == "답변을 생각하는 중…" ? "" : draft.Reply + "\n\n") + message; draft = null; }
    void Receive(ChatTurn turn)
    {
        recipient.IsEnabled = mode.IsEnabled = true;
        if (draft != null) { draft.Turn = turn; draft.Reply = turn.Assistant; } else { draft = ChatRow.From(turn, repo.Preferences.Profile.Name); rows.Add(draft); }
        var completed = draft; draft = null; lastReply = turn.Assistant;
        foreach (var row in rows.Where(r => r != completed && r.Turn != null && !repo.Turns.Contains(r.Turn)).ToArray()) rows.Remove(row);
        RefreshCaption(); history.ScrollIntoView(completed);
    }
    async System.Threading.Tasks.Task Send()
    {
        string text = input.Text.Trim(); if (pending != null || text.Length == 0) return;
        pending = new(); send.IsEnabled = false; input.IsReadOnly = true; status.Text = "답변을 받는 중…";
        try { await buddy.SendMessage(text, false, pending.Token); if (!closed) { input.Clear(); status.Text = ""; } }
        catch (OperationCanceledException) { if (!closed) status.Text = "중단되었거나 응답 시간이 초과됐어요."; }
        catch (System.Net.Http.HttpRequestException) { if (!closed) status.Text = "네트워크와 엔드포인트를 확인해 주세요."; }
        catch (Exception e) { if (!closed) status.Text = e.Message; }
        finally { pending?.Dispose(); pending = null; if (!closed) { send.IsEnabled = true; input.IsReadOnly = false; input.Focus(); } }
    }
}
