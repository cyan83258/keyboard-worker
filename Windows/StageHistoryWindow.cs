using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace KeyboardWorker;

// The talks held on the pages, to read again and look after: search, edit, delete, set aside (a whole exchange or a
// passage), write to a file, read back from one, and tidy so they do not pile up. What is set aside is on the second view.
public sealed class StageHistoryWindow : Window
{
    const int Shown = 100;
    readonly Buddy buddy; readonly StageLog log; readonly StageClips clips;
    readonly ComboBox threads = new() { DisplayMemberPath = "Label", MinWidth = 260 }, keep = new() { MinWidth = 150 }, limit = new() { MinWidth = 90 };
    readonly TextBox search = new() { Padding = new Thickness(8, 5, 8, 5) }; readonly StackPanel list = new(), tidy = new() { Orientation = Orientation.Horizontal };
    readonly TextBlock count = Pastel.Text("", 12), status = Pastel.Text("", 12); readonly Button talksTab, clipsTab, import;
    sealed record Choice(string Key, string Label);
    bool kept, filling;
    string Thread => (threads.SelectedItem as Choice)?.Key ?? "";
    public StageHistoryWindow(Buddy buddy, string thread, bool showKept = false)
    {
        this.buddy = buddy; log = buddy.StageTalks; clips = buddy.StageClips; kept = showKept; var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        talksTab = Pastel.Button("대화 기록", () => View(false)); clipsTab = Pastel.Button("모아 둔 것", () => View(true)); tabs.Children.Add(talksTab); tabs.Children.Add(clipsTab); top.Children.Add(tabs);
        var pick = new DockPanel { Margin = new Thickness(0, 0, 0, 6) }; DockPanel.SetDock(threads, Dock.Left); threads.Margin = new Thickness(0, 0, 8, 0); pick.Children.Add(threads); search.ToolTip = "낱말로 찾기 (띄어쓰기로 여러 낱말)"; pick.Children.Add(search);
        top.Children.Add(pick); top.Children.Add(count);

        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(status);
        keep.ItemsSource = new[] { "최근 50개만 남기기", "최근 100개만 남기기", "최근 200개만 남기기", "모두 지우기" }; keep.SelectedIndex = 1; keep.VerticalAlignment = limit.VerticalAlignment = VerticalAlignment.Center;
        limit.ItemsSource = StageLog.Limits; limit.SelectedItem = StageLog.Limits.Contains(log.Limit) ? log.Limit : StageLog.Keep; limit.ToolTip = "대화마다 이 개수를 넘으면 가장 오래된 것부터 자동으로 지워져요. 모아 둔 것은 지워지지 않아요.";
        tidy.Children.Add(keep); tidy.Children.Add(Pastel.Button("정리", Tidy, true));
        tidy.Children.Add(new TextBlock { Text = "자동 보관 한도", Foreground = Pastel.Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 6, 0) }); tidy.Children.Add(limit);
        footer.Children.Add(tidy);
        var buttons = new WrapPanel(); buttons.Children.Add(Pastel.Button("내보내기…", Export)); import = Pastel.Button("불러오기…", Import, true); buttons.Children.Add(import); buttons.Children.Add(Pastel.Button("닫기", Close, true)); footer.Children.Add(buttons);
        root.Children.Add(Pastel.Scroll(list));

        filling = true;
        // Text messages keep no record here: they are read, edited and deleted in their own window.
        var keys = log.Threads.Where(t => StageMode.Kind(t) != StageMode.Messenger).ToList(); if (thread.Length > 0 && !keys.Contains(thread)) keys.Insert(0, thread);
        var choices = keys.Select(k => new Choice(k, Title(k))).OrderBy(c => c.Label, StringComparer.CurrentCulture).ToArray();
        threads.ItemsSource = choices; threads.SelectedItem = choices.FirstOrDefault(c => c.Key == thread) ?? choices.FirstOrDefault();
        filling = false;
        threads.SelectionChanged += (_, _) => { if (!filling) Refresh(); }; search.TextChanged += (_, _) => Refresh();
        limit.SelectionChanged += (_, _) => { if (limit.SelectedItem is int value) { buddy.Stage.State.Talk.KeepTurns = value; buddy.Stage.Save(); log.Limit = value; status.Text = "앞으로 대화마다 최근 " + value + "개까지 보관해요."; } };
        log.Changed += Refresh; clips.Changed += Refresh; Closed += (_, _) => { log.Changed -= Refresh; clips.Changed -= Refresh; };
        Pastel.Window(this, "대화 기록", root, 720, 820); MinWidth = 560; MinHeight = 480; Refresh();
    }
    // A thread's key in words: whose page, and which mode ("pair:" is the page for two).
    public static new string Title(string key, IReadOnlyList<Buddy.StageCharacter> people, IReadOnlyList<StageMode> modes)
    {
        string Name(string id) => people.FirstOrDefault(c => c.Id == id)?.Title ?? "(지워진 캐릭터)";
        if (key.StartsWith("pair:", StringComparison.Ordinal)) { var ids = key[5..].Split('+'); return ids.Length == 2 ? Name(ids[0]) + " ♡ " + Name(ids[1]) + " · 둘만의 페이지" : key; }
        int bar = key.IndexOf('|'); return bar < 0 ? Name(key) + " · " + StageMode.BuiltIn[0].Name : Name(key[..bar]) + " · " + (modes.FirstOrDefault(m => m.Id == key[(bar + 1)..])?.Name ?? key[(bar + 1)..]);
    }
    new string Title(string thread) { string page = Title(StageRooms.Split(thread).Key, buddy.StageCharacters(), StageMode.All), room = buddy.Rooms.RoomName(thread); return room.Length > 0 ? page + " · " + room : page; }
    void View(bool keptView) { kept = keptView; status.Text = ""; Refresh(); }
    bool Busy() { if (!buddy.IsChatBusy) return false; status.Text = "답변을 만드는 중이에요. 끝난 뒤에 다시 해 주세요."; return true; }

    ChatTurn[] Found() => ChatSearchWindow.Find(log.Turns(Thread), search.Text);
    StageClip[] FoundClips()
    {
        var words = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return clips.Items.Where(c => words.All(w => c.Text.Contains(w, StringComparison.OrdinalIgnoreCase) || c.User.Contains(w, StringComparison.OrdinalIgnoreCase) || c.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
    static TextBox Line(string text) => new() { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Pastel.Ink, FontSize = 13, Padding = new Thickness(0), Margin = new Thickness(0, 3, 0, 0), MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    static Button Small(string text, Action action, string tip) { var button = Pastel.Button(text, action, true); button.Padding = new Thickness(9, 2, 9, 3); button.Margin = new Thickness(4, 0, 0, 0); button.FontSize = 11.5; button.ToolTip = tip; return button; }
    static DockPanel Head(string text, params Button[] actions)
    {
        var head = new DockPanel(); var right = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(right, Dock.Right); foreach (var action in actions) right.Children.Add(action); head.Children.Add(right);
        head.Children.Add(new TextBlock { Text = text, Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }); return head;
    }
    void Refresh()
    {
        list.Children.Clear(); talksTab.Opacity = kept ? .6 : 1; clipsTab.Opacity = kept ? 1 : .6; clipsTab.Content = "모아 둔 것 (" + clips.Items.Count + ")";
        threads.Visibility = tidy.Visibility = import.Visibility = kept ? Visibility.Collapsed : Visibility.Visible;
        if (kept) { Kept(); return; }
        string thread = Thread; bool pair = thread.StartsWith("pair:", StringComparison.Ordinal); int total = log.Turns(thread).Count; var found = Found().Reverse().ToArray();
        count.Text = search.Text.Trim().Length == 0 ? $"대화 {total}개 (최근 것부터) · 전체 페이지 대화 {log.Count}개" : $"찾은 대화 {found.Length}개 / {total}개";
        if (found.Length == 0) { list.Children.Add(Pastel.Text(total == 0 ? "아직 여기서 나눈 대화가 없어요." : "맞는 대화가 없어요. 다른 낱말로 찾아보세요.", 13)); return; }
        foreach (var turn in found.Take(Shown))
        {
            var answer = Line(turn.Assistant); bool has = clips.Has(thread, turn);
            var entry = new StackPanel();
            entry.Children.Add(Head($"♡ {turn.At.ToLocalTime():yyyy-MM-dd HH:mm}" + (turn.CharacterName.Length > 0 && !pair ? " · " + turn.CharacterName : ""),
                Small(has ? "★ 담음" : "☆ 담기", () => status.Text = clips.Toggle(thread, turn) ? "이 대화를 '모아 둔 것'에 담았어요. 정리해도 남아 있어요." : "모아 둔 것에서 뺐어요.", "이 대화를 통째로 모아 둬요"),
                Small("발췌", () => status.Text = answer.SelectedText.Trim().Length == 0 ? "먼저 답변에서 마음에 드는 부분을 마우스로 끌어 고른 뒤 눌러 주세요." : clips.Excerpt(thread, turn, answer.SelectedText) ? "고른 부분을 '모아 둔 것'에 담았어요." : "이미 담아 둔 부분이에요.", "답변에서 끌어서 고른 부분만 모아 둬요"),
                Small("편집", () => Edit(thread, turn), "내 말과 답변을 고쳐요"),
                Small("삭제", () => { if (!Busy() && MessageBox.Show(this, "이 대화를 삭제할까요?", "대화 기록", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes && log.Remove(thread, turn)) status.Text = "대화 하나를 삭제했어요."; }, "이 대화만 지워요")));
            if (turn.User.Trim().Length > 0) entry.Children.Add(Line((pair ? "연출: " : "나: ") + turn.User));
            entry.Children.Add(answer);
            var card = Pastel.Card(entry); card.Padding = new Thickness(12, 8, 12, 8); card.Margin = new Thickness(0, 0, 4, 6); list.Children.Add(card);
        }
        if (found.Length > Shown) list.Children.Add(Pastel.Text("최근 " + Shown + "개만 보여줍니다. 낱말을 적어 좁히거나 파일로 내보내 보세요.", 12));
    }
    void Kept()
    {
        var found = FoundClips().Reverse().ToArray(); count.Text = $"모아 둔 것 {found.Length}개" + (search.Text.Trim().Length > 0 ? $" / {clips.Items.Count}개" : "") + " · 대화를 정리하거나 지워도 여기 있는 것은 남아요";
        if (found.Length == 0) { list.Children.Add(Pastel.Text(clips.Items.Count == 0 ? "아직 모아 둔 것이 없어요. '대화 기록'에서 ☆ 담기나 발췌를 눌러 보세요." : "맞는 것이 없어요.", 13)); return; }
        foreach (var clip in found.Take(Shown))
        {
            var entry = new StackPanel();
            entry.Children.Add(Head($"{(clip.Whole ? "★" : "✂")} {clip.At.ToLocalTime():yyyy-MM-dd HH:mm} · {Title(clip.Thread)}",
                Small("복사", () => { try { Clipboard.SetText(clip.Text); status.Text = "복사했어요."; } catch (System.Runtime.InteropServices.ExternalException) { status.Text = "지금은 복사할 수 없어요. 다시 눌러 주세요."; } }, "클립보드에 복사"),
                Small("빼기", () => { clips.Remove(clip); status.Text = "모아 둔 것에서 뺐어요."; }, "모아 둔 것에서 지워요 (원래 대화는 그대로)")));
            if (clip.User.Trim().Length > 0) entry.Children.Add(Line("나: " + clip.User));
            entry.Children.Add(Line(clip.Text));
            var card = Pastel.Card(entry, true); card.Padding = new Thickness(12, 8, 12, 8); card.Margin = new Thickness(0, 0, 4, 6); list.Children.Add(card);
        }
        if (found.Length > Shown) list.Children.Add(Pastel.Text("최근 " + Shown + "개만 보여줍니다.", 12));
    }
    void Edit(string thread, ChatTurn turn)
    {
        if (Busy()) return; var editor = new StageTurnEditor(turn) { Owner = this, ShowInTaskbar = false };
        if (editor.ShowDialog() == true && editor.Result != null) status.Text = log.Replace(thread, turn, editor.Result) ? "고친 내용을 저장했어요. 다음 대화부터 캐릭터도 이 내용으로 기억해요." : "대화를 찾지 못했어요.";
    }
    void Tidy()
    {
        if (Busy()) return; string thread = Thread; int total = log.Turns(thread).Count, left = keep.SelectedIndex switch { 0 => 50, 1 => 100, 2 => 200, _ => 0 };
        if (total <= left) { status.Text = "정리할 것이 없어요. (대화 " + total + "개)"; return; }
        if (MessageBox.Show(this, (left == 0 ? "이 대화를 모두 지울까요?" : $"오래된 대화 {total - left}개를 지우고 최근 {left}개만 남길까요?") + "\n'모아 둔 것'은 지워지지 않아요.", "대화 정리", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        status.Text = log.Trim(thread, left) + "개 대화를 정리했어요.";
    }
    void Export()
    {
        bool json = false; int amount = kept ? FoundClips().Length : Found().Length; if (amount == 0) { status.Text = "내보낼 것이 없어요."; return; }
        var dialog = new SaveFileDialog { Filter = "텍스트 (*.txt)|*.txt|Markdown (*.md)|*.md" + (kept ? "" : "|다시 불러올 수 있는 파일 (*.json)|*.json"), FileName = SafeFiles.OneLine(kept ? "모아 둔 장면" : Title(Thread), 60).Replace(" · ", "-").Replace("♡", "+") + "-" + DateTime.Now.ToString("yyyyMMdd") };
        foreach (char bad in Path.GetInvalidFileNameChars()) dialog.FileName = dialog.FileName.Replace(bad, '_');
        if (dialog.ShowDialog(this) != true) return; bool markdown = dialog.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase); json = !kept && dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        string user = string.IsNullOrWhiteSpace(buddy.Chat.Preferences.Profile.UserName) ? "나" : buddy.Chat.Preferences.Profile.UserName;
        try
        {
            string text = kept ? StageClips.Text(FoundClips(), markdown, Title) : json ? StageArchive.Write(Title(Thread), Found()) : ChatSearchWindow.Text(Found(), markdown, user);
            File.WriteAllText(dialog.FileName, text, new UTF8Encoding(!json)); status.Text = amount + "개를 내보냈어요 · " + Path.GetFileName(dialog.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "저장하지 못했습니다. " + e.Message; }
    }
    void Import()
    {
        if (Busy() || Thread.Length == 0) return;
        var dialog = new OpenFileDialog { Filter = "Keyboard Worker 대화 파일 (*.json)|*.json" }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 32L * 1024 * 1024) throw new InvalidOperationException("파일이 너무 큽니다.");
            int added = log.Import(Thread, StageArchive.Read(File.ReadAllText(dialog.FileName)));
            status.Text = added > 0 ? added + "개 대화를 '" + Title(Thread) + "'에 불러왔어요." : "이미 모두 들어 있는 대화예요.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { status.Text = "불러오지 못했습니다. " + e.Message; }
    }
}

// Editing one exchange of a page talk.
public sealed class StageTurnEditor : Window
{
    public ChatTurn? Result { get; private set; }
    public StageTurnEditor(ChatTurn turn)
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        TextBox Box(string text, double height) => new() { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = height, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13, Padding = new Thickness(6) };
        var user = Box(turn.User, 90); user.MaxLength = 6000; var answer = Box(turn.Assistant, 300); answer.MaxLength = StageLog.MaxAnswer;
        var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var note = Pastel.Text("", 12);
        buttons.Children.Add(Pastel.Button("저장", () => { if (answer.Text.Trim().Length == 0) { note.Text = "답변을 비워 둘 수는 없어요. 지우려면 '삭제'를 써 주세요."; return; } Result = turn with { User = user.Text.Trim(), Assistant = answer.Text.Trim() }; DialogResult = true; }));
        buttons.Children.Add(Pastel.Button("취소", Close, true)); buttons.Children.Add(note);
        var body = new StackPanel(); body.Children.Add(Pastel.Text("내 말 (또는 연출)", 13)); body.Children.Add(user); body.Children.Add(Pastel.Text("답변", 13)); body.Children.Add(answer); root.Children.Add(body);
        Pastel.Window(this, "대화 편집", root, 620, 580);
    }
}
