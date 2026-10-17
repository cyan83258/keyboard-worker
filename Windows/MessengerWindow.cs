using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace KeyboardWorker;

// Text messages with a character: a chat log (MessengerView) with its own chat rooms, prompt and length. It shares nothing
// with the character page - not the rooms, not the threads, not the settings, not what is set aside. An exchange is set
// aside, edited, deleted or asked again right under its answer; old messages are tidied from the top bar. Every line the
// user writes is a message of its own; "＋" adds a voice message, a photo, a gift, money or what happened since (TextCards),
// and what the character says it is doing shows next to its name.
public sealed class MessengerWindow : Window
{
    static readonly string[] emoji = "😀 😄 😊 😉 😍 🥰 😘 😂 🤣 😅 😭 😢 🥺 😡 😤 😳 😴 🤔 😎 🙄 😱 🤗 👍 👎 👏 🙏 💪 👋 ❤ 💕 💔 ✨ 🔥 🎉 🌸 ☀ 🌙 ⭐ ☕ 🍰".Split(' ');
    readonly Buddy buddy; readonly PageFrame frame; readonly MessengerView view = new();
    readonly ComboBox who = new() { DisplayMemberPath = "Label", MinWidth = 170 }, modes = new() { DisplayMemberPath = "Name", MinWidth = 110, Margin = new Thickness(8, 0, 0, 0), ToolTip = "문자 모드 (추가 모드가 넣은 것)" };
    readonly TextBlock status = Pastel.Text("", 12), presence = new() { FontSize = 11.5, Margin = new Thickness(8, 0, 0, 0), MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = .8 };
    readonly TextBox input; readonly Button send;
    sealed record Choice(Buddy.StageCharacter Character, string Label);
    StageMode mode; CancellationTokenSource? request; bool busy, filling;
    Buddy.StageCharacter Current => ((Choice)who.SelectedItem).Character;
    string Page => mode.Thread(Current.Id);
    string Thread => buddy.Rooms.Thread(Page);
    public MessengerWindow(Buddy buddy)
    {
        this.buddy = buddy; mode = buddy.TextModeNow;
        frame = new PageFrame(buddy, this, StageMode.Messenger, () => busy, Enter, () => Current.Id); frame.Left(who); frame.Left(presence); frame.Left(modes); presence.Foreground = Pastel.Ink;
        frame.Tool("모아 둔 것", "☆ 나 ✂ 로 모아 둔 문자를 봐요", () => new StageHistoryWindow(buddy, "", true, true) { Owner = this, ShowInTaskbar = false }.ShowDialog());
        Button tidy = null!; tidy = frame.Tool("정리", "오래된 문자를 지워 쌓이지 않게 해요 (모아 둔 것은 남아요)", () => Tidy(tidy));
        frame.Tool("문자 설정", "문자 메시지의 프롬프트 · 길이 · 말투 · 배경 (마주 보기와 따로예요)", () => { new StageSettingsWindow(buddy, Current, true) { Owner = this, ShowInTaskbar = false }.ShowDialog(); Enter(); });

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); frame.Root.Children.Add(bottom);
        var entry = PageFrame.Entry(async () => await Say(), () => request?.Cancel(), () => busy, 6000); input = entry.Input; send = entry.Send;
        var smile = Pastel.Button("☺", () => { }, true); smile.Padding = new Thickness(11, 4, 11, 6); smile.FontSize = 17; smile.Margin = new Thickness(6, 0, 0, 0); smile.VerticalAlignment = VerticalAlignment.Bottom; smile.ToolTip = "이모티콘 넣기";
        var picker = Emoji(smile); smile.Click += (_, _) => { if (!picker.IsOpen) Stickers(); picker.IsOpen = !picker.IsOpen; };
        view.StickerPath = title => buddy.Stage.Sticker(title)?.Path;
        var plus = Pastel.Button("＋", () => { }, true); plus.Padding = new Thickness(11, 4, 11, 6); plus.FontSize = 16; plus.Margin = new Thickness(6, 0, 0, 0); plus.VerticalAlignment = VerticalAlignment.Bottom; plus.ToolTip = "음성 메시지 · 사진 · 선물 · 송금 · 있었던 일";
        var extras = new ContextMenu { PlacementTarget = plus, Placement = PlacementMode.Top };
        foreach (var (kind, name, ask) in TextCards.Kinds) { var item = new MenuItem { Header = name }; item.Click += (_, _) => Card(kind, name, ask); extras.Items.Add(item); }
        plus.Click += (_, _) => extras.IsOpen = true;
        DockPanel.SetDock(smile, Dock.Right); entry.Row.Children.Add(smile); DockPanel.SetDock(plus, Dock.Right); entry.Row.Children.Add(plus); entry.Row.Children.Add(input); bottom.Children.Add(entry.Row); status.Margin = new Thickness(2, 6, 0, 0); bottom.Children.Add(status);
        frame.Root.Children.Add(new Border { Child = view, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 3, 3.5), CornerRadius = new CornerRadius(12), ClipToBounds = true });
        view.Edit += Edit; view.Delete += Delete; view.Again += async () => await Again(); view.IsKept = turn => buddy.StageClips.Has(Thread, turn);
        view.Keep += turn => { status.Text = buddy.StageClips.Toggle(Thread, turn) ? "모아 뒀어요. 문자를 정리해도 남아 있어요." : "모아 둔 것에서 뺐어요."; Reload(); };
        view.Excerpt += (turn, passage) => status.Text = passage.Length == 0 ? "먼저 답장에서 마음에 드는 부분을 마우스로 끌어 고른 뒤 ✂ 를 눌러 주세요." : buddy.StageClips.Excerpt(Thread, turn, passage) ? "고른 부분을 모아 뒀어요." : "이미 모아 둔 부분이에요.";

        filling = true;
        who.ItemsSource = buddy.StageCharacters().Select(c => new Choice(c, c.Role + " · " + c.Title)).ToArray(); who.SelectedIndex = 0;
        var all = StageMode.Of(StageMode.Messenger); modes.ItemsSource = all; modes.SelectedItem = mode; modes.Visibility = all.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        filling = false;
        who.SelectionChanged += (_, _) => { if (!filling && who.SelectedItem != null) Enter(); };
        modes.SelectionChanged += (_, _) => { if (filling || modes.SelectedItem is not StageMode chosen) return; mode = chosen; buddy.Stage.State.TextMode = chosen.Id; buddy.Stage.Save(); Enter(); };
        Closed += (_, _) => request?.Cancel();
        Pastel.Window(this, "문자 메시지", frame.Shell, 1120, 820, PageFrame.Sky); MinWidth = 820; MinHeight = 520; Enter();
        Loaded += (_, _) => input.Focus();
    }
    public void Select(string id) { foreach (Choice choice in who.Items) if (choice.Character.Id == id) { who.SelectedItem = choice; return; } }
    // The user's own emoticons on the picker, as they are now; a click sends one as a message of its own.
    readonly WrapPanel mine = new() { Width = 8 * 38, Margin = new Thickness(0, 0, 0, 6) }; Popup? board;
    void Stickers()
    {
        mine.Children.Clear();
        foreach (var sticker in buddy.Stage.State.Stickers)
        {
            var key = new Button { Content = StageParts.Picture(sticker.Path, 64), Padding = new Thickness(3), Margin = new Thickness(0, 0, 4, 4), ToolTip = sticker.Title, Cursor = System.Windows.Input.Cursors.Hand }; Pastel.Ghost(key);
            key.Click += async (_, _) => { if (board != null) board.IsOpen = false; await Talk(StageSticker.Mark(sticker.Title)); };
            mine.Children.Add(key);
        }
        if (mine.Children.Count == 0) mine.Children.Add(new TextBlock { Text = "내 그림 이모티콘은 아래 '이모티콘 관리'에서 넣어요.", Foreground = Pastel.Ink, Opacity = .75, FontSize = 12, TextWrapping = TextWrapping.Wrap, Width = 8 * 38 - 8 });
    }
    // The picker over the button: the user's own emoticons (sent at once), then picture characters (put where the caret is).
    Popup Emoji(Button anchor)
    {
        var board = new WrapPanel { Width = 8 * 38 };
        foreach (string one in emoji)
        {
            var key = new Button { Content = one, FontSize = 20, Width = 38, Height = 38, Padding = new Thickness(0), FontFamily = new FontFamily("Segoe UI Emoji"), Cursor = System.Windows.Input.Cursors.Hand }; Pastel.Ghost(key);
            key.Click += (_, _) => { int at = input.SelectionStart; input.SelectedText = one; input.SelectionLength = 0; input.SelectionStart = at + one.Length; input.Focus(); };
            board.Children.Add(key);
        }
        var manage = Pastel.Button("이모티콘 관리…", () => { this.board!.IsOpen = false; new StickerWindow(buddy) { Owner = this, ShowInTaskbar = false }.ShowDialog(); Reload(); }, true); manage.FontSize = 12; manage.HorizontalAlignment = HorizontalAlignment.Left; manage.Margin = new Thickness(0, 6, 0, 0);
        var all = new StackPanel(); all.Children.Add(new ScrollViewer { Content = mine, MaxHeight = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); all.Children.Add(board); all.Children.Add(manage);
        return this.board = new Popup { Child = new Border { Child = all, Background = Pastel.Paper, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.5, 1.5, 3, 3), CornerRadius = new CornerRadius(12), Padding = new Thickness(8) }, PlacementTarget = anchor, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, VerticalOffset = -6 };
    }
    // Switching to a character, a mode or a room: its own thread of messages.
    void Enter()
    {
        var current = Current; string name = current.Name + " · " + mode.Name; frame.Rooms.Load(Page, name);
        Title = name + (buddy.Rooms.Rooms(Page).Count > 1 ? " · " + buddy.Rooms.Current(Page).Name : "");
        view.SetFont(FontAssets.Get(buddy.Pref.StageFontPath), buddy.Pref.StageFontSize); view.SetBackdrop(buddy.Stage.State.TextBackdrop); Reload(true); status.Text = "";
    }
    void Reload(bool fresh = false)
    {
        var turns = buddy.StageTalks.Turns(Thread); view.Load(turns, fresh);
        string now = TextCards.Now(turns); presence.Text = now.Length > 0 ? "● " + now : ""; presence.ToolTip = now.Length > 0 ? Current.Name + "의 지금 상태" : null;
    }
    // A special message, written in a small box and put on its own line of what is being typed (sent with the rest).
    void Card(string kind, string name, string ask)
    {
        var text = new TextBox { AcceptsReturn = kind != TextCards.Money, TextWrapping = TextWrapping.Wrap, MinHeight = kind == TextCards.Money ? 0 : 70, MaxLength = 1000, Padding = new Thickness(6, 4, 6, 4), FontSize = 13 };
        var memo = new TextBox { MaxLength = 100, Padding = new Thickness(6, 4, 6, 4), FontSize = 13 };
        var body = new StackPanel { Margin = new Thickness(18) }; body.Children.Add(Pastel.Text(ask, 12.5)); body.Children.Add(text);
        if (kind == TextCards.Money) { body.Children.Add(new TextBlock { Text = "메모 (안 써도 돼요)", Foreground = Pastel.Ink, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) }); body.Children.Add(memo); }
        var dialog = new Window { Owner = this, ShowInTaskbar = false }; string? made = null;
        var ok = Pastel.Button("넣기", () =>
        {
            string said = text.Text.Trim(); if (said.Length == 0) return;
            if (kind == TextCards.Money && TextCards.Read(TextCards.Write(kind, said)) == null) { text.Text = ""; text.Focus(); return; }
            made = TextCards.Write(kind, said, memo.Text); dialog.Close();
        }); ok.HorizontalAlignment = HorizontalAlignment.Right; ok.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(ok);
        Pastel.Window(dialog, name, body, 420, kind == TextCards.Money ? 260 : 250); dialog.Loaded += (_, _) => text.Focus(); dialog.ShowDialog();
        if (made == null) return;
        input.Text = (input.Text.TrimEnd().Length > 0 ? input.Text.TrimEnd() + "\n" : "") + made; input.CaretIndex = input.Text.Length; input.Focus();
        status.Text = name + "을(를) 넣었어요. 보내기를 누르면 함께 보내요.";
    }
    void Tidy(Button anchor)
    {
        if (busy) return; string thread = Thread; int total = buddy.StageTalks.Turns(thread).Count; var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (int left in new[] { 30, 50, 100, 0 })
        {
            var item = new MenuItem { Header = left == 0 ? "이 방의 문자 모두 지우기" : $"최근 {left}개만 남기기", IsEnabled = total > left };
            item.Click += (_, _) =>
            {
                if (MessageBox.Show(this, (left == 0 ? "이 방의 문자를 모두 지울까요?" : $"오래된 문자 {total - left}개를 지우고 최근 {left}개만 남길까요?") + "\n모아 둔 것은 지워지지 않아요.", "문자 정리", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                status.Text = buddy.StageTalks.Trim(thread, left) + "개를 정리했어요."; Reload(true);
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuItem { Header = $"지금 {total}개 · 자동 보관 한도는 '문자 설정'에서", IsEnabled = false }); menu.IsOpen = true;
    }
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
