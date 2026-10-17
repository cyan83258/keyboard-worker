using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace KeyboardWorker;

// The novel's prompt, part by part as in SillyTavern's prompt manager: switch each on or off, move it up or down, edit
// what it says and in which role, add or remove parts, or read a SillyTavern preset in. Markers (characters, persona,
// scene, the story so far) are filled in by the program. Changes count once "저장" is pressed.
public sealed class NovelPromptWindow : Window
{
    readonly Buddy buddy; NovelPromptSet draft; readonly StackPanel list = new(); readonly TextBlock title = Pastel.Text("", 13), status = Pastel.Text("", 12);
    readonly TextBox name = new() { MaxLength = 60, Padding = new Thickness(6, 4, 6, 4) }, content = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13, Padding = new Thickness(6), MaxLength = NovelPrompts.MaxContent };
    readonly TextBox prefill = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 56, MaxLength = NovelPrompts.MaxContent, FontSize = 12.5, ToolTip = "AI 답변의 첫머리로 넣어 두면 그 뒤를 이어 써요 (SillyTavern의 assistant prefill). 비워 두면 쓰지 않아요." };
    readonly ComboBox role = new() { ItemsSource = new[] { "system", "user", "assistant" }, MinWidth = 120 }; readonly TextBlock markerNote = new() { Foreground = Pastel.Ink, Opacity = .75, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    NovelPromptPart? chosen; bool filling, changed;
    public NovelPromptWindow(Buddy buddy)
    {
        this.buddy = buddy; draft = buddy.Novel.State.Prompts.Snapshot(); var root = new DockPanel { Margin = new Thickness(18) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var load = Pastel.Button("SillyTavern 프리셋 불러오기…", Import); DockPanel.SetDock(load, Dock.Right); top.Children.Add(load); top.Children.Add(title);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(status);
        var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("저장", () => { Apply(); buddy.Novel.State.Prompts = NovelPrompts.Normalize(draft.Snapshot()); buddy.Novel.Save(); changed = false; status.Text = "저장했어요. 다음에 쓰는 부분부터 적용돼요."; }));
        buttons.Children.Add(Pastel.Button("조합된 프롬프트 보기", Preview, true)); buttons.Children.Add(Pastel.Button("기본 프롬프트로", () => { if (MessageBox.Show(this, "기본 소설 프롬프트로 바꿀까요? (저장해야 적용돼요)", "프롬프트", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; draft = NovelPrompts.Standard(); changed = true; Fill(draft.Parts[0]); }, true));
        buttons.Children.Add(Pastel.Button("닫기", Close, true)); footer.Children.Add(buttons);

        // Left: the parts in order. Right: the one chosen.
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) }; var add = new WrapPanel(); DockPanel.SetDock(add, Dock.Bottom); left.Children.Add(add);
        add.Children.Add(StageParts.Small("＋ 새 항목", () => { Apply(); var part = new NovelPromptPart { Identifier = Guid.NewGuid().ToString("N"), Name = "새 프롬프트", Role = PromptMessage.System }; draft.Parts.Insert(chosen == null ? draft.Parts.Count : draft.Parts.IndexOf(chosen) + 1, part); changed = true; Fill(part); }));
        add.Children.Add(StageParts.Small("삭제", () => { if (chosen == null) return; if (chosen.Marker && MessageBox.Show(this, "자리 표시 항목을 지우면 그 내용이 프롬프트에 들어가지 않아요. 지울까요?", "프롬프트", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; int at = draft.Parts.IndexOf(chosen); draft.Parts.Remove(chosen); changed = true; Fill(draft.Parts.ElementAtOrDefault(Math.Min(at, draft.Parts.Count - 1))); }));
        left.Children.Add(new Border { Child = Pastel.Scroll(list), BorderBrush = Pastel.Line, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(8), Padding = new Thickness(4) });
        body.Children.Add(left);
        var right = new DockPanel(); Grid.SetColumn(right, 1); body.Children.Add(right);
        var head = new StackPanel(); DockPanel.SetDock(head, Dock.Top); right.Children.Add(head);
        var line = new DockPanel(); DockPanel.SetDock(role, Dock.Right); role.Margin = new Thickness(8, 0, 0, 0); line.Children.Add(role); line.Children.Add(name); head.Children.Add(line); head.Children.Add(markerNote);
        var tail = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(tail, Dock.Bottom); right.Children.Add(tail);
        tail.Children.Add(new TextBlock { Text = "답변 첫머리 (prefill)", Foreground = Pastel.Ink, FontSize = 12, Margin = new Thickness(0, 0, 0, 2) }); tail.Children.Add(prefill);
        content.Margin = new Thickness(0, 6, 0, 0); right.Children.Add(content);
        root.Children.Add(body);
        name.TextChanged += (_, _) => { if (!filling) changed = true; }; content.TextChanged += (_, _) => { if (!filling) changed = true; }; role.SelectionChanged += (_, _) => { if (!filling) changed = true; };
        prefill.TextChanged += (_, _) => { if (!filling) changed = true; };
        Closing += (_, e) => { Apply(); if (changed && MessageBox.Show(this, "저장하지 않은 변경이 있어요. 저장하지 않고 닫을까요?", "프롬프트", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) e.Cancel = true; };
        Pastel.Window(this, "소설 프롬프트", root, 1000, 780); MinWidth = 820; MinHeight = 560;
        Fill(draft.Parts.FirstOrDefault());
    }
    // What is being edited goes into the part.
    void Apply()
    {
        draft.Prefill = prefill.Text; if (chosen == null) return;
        chosen.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : chosen.Name; chosen.Role = role.SelectedItem as string ?? chosen.Role; if (!chosen.Marker) chosen.Content = content.Text;
    }
    // The list, and the chosen part in the editor.
    void Fill(NovelPromptPart? part)
    {
        filling = true; chosen = part; title.Text = $"{draft.Name} · 켜진 항목 {draft.Parts.Count(p => p.Enabled)} / {draft.Parts.Count}"; list.Children.Clear();
        foreach (var each in draft.Parts)
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Background = ReferenceEquals(each, chosen) ? Pastel.CardPink : System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            var on = new CheckBox { IsChecked = each.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0), ToolTip = "켜고 끄기" }; on.Click += (_, _) => { each.Enabled = on.IsChecked == true; changed = true; Fill(chosen); };
            DockPanel.SetDock(on, Dock.Left); row.Children.Add(on);
            var moves = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(moves, Dock.Right); row.Children.Add(moves);
            foreach (var (glyph, step) in new[] { ("▲", -1), ("▼", 1) }) { var move = new Button { Content = glyph, FontSize = 9, Padding = new Thickness(4, 0, 4, 1), Foreground = Pastel.Ink, Opacity = .6 }; Pastel.Ghost(move); move.Click += (_, _) => Move(each, step); moves.Children.Add(move); }
            var label = new TextBlock { Text = (each.Marker ? "◆ " : "") + each.Name, Foreground = Pastel.Ink, FontSize = 12.5, Opacity = each.Enabled ? 1 : .45, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 4, 0, 4), ToolTip = each.Marker ? "자리 표시: " + NovelPrompts.MarkerName(each.Identifier) : each.Role };
            row.Children.Add(label); row.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not CheckBox) { Apply(); Fill(each); } };
            list.Children.Add(row);
        }
        name.Text = part?.Name ?? ""; role.SelectedItem = part?.Role ?? PromptMessage.System; content.Text = part == null ? "" : part.Marker ? "" : part.Content; prefill.Text = draft.Prefill;
        name.IsEnabled = role.IsEnabled = content.IsEnabled = part != null; role.Visibility = part?.Marker == true ? Visibility.Collapsed : Visibility.Visible; content.IsEnabled = part != null && !part.Marker;
        markerNote.Text = part?.Marker != true ? "{{char}} {{user}} 등은 캐릭터 이름과 내 호칭으로 바뀌어요." : part.Identifier switch
        {
            "charDescription" => "자리 표시: 등장하는 캐릭터의 프로필이 여기에 들어가요.", "charPersonality" => "자리 표시: 캐릭터 성격은 '캐릭터 설정'에 함께 들어가 있어 따로 넣지 않아요.",
            "personaDescription" => "자리 표시: 내 호칭과 사용자 메모(캐릭터 프로필 → 사용자에 대한 메모)가 들어가요.", "scenario" => "자리 표시: 지금 채팅방의 작가 노트가 들어가요.",
            "dialogueExamples" => "자리 표시: 캐릭터 프로필의 대사 예시가 들어가요.", "chatHistory" => "자리 표시: 방의 기억 요약, 지금까지 쓴 이야기, 이번 지시가 여기에 들어가요. 없으면 맨 끝에 붙어요.",
            _ => "자리 표시: 이 프로그램에는 없는 내용이라 비어 있어요 (SillyTavern의 " + part.Identifier + ")."
        };
        filling = false;
    }
    void Move(NovelPromptPart part, int step)
    {
        Apply(); int at = draft.Parts.IndexOf(part), to = at + step; if (to < 0 || to >= draft.Parts.Count) return;
        draft.Parts.RemoveAt(at); draft.Parts.Insert(to, part); changed = true; Fill(chosen);
    }
    void Import()
    {
        var dialog = new OpenFileDialog { Title = "SillyTavern 프롬프트 프리셋", Filter = "SillyTavern 프리셋 (*.json)|*.json" }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 8L * 1024 * 1024) throw new InvalidOperationException("파일이 너무 큽니다.");
            var (set, sampling) = NovelPrompts.ReadSillyTavern(File.ReadAllText(dialog.FileName), Path.GetFileNameWithoutExtension(dialog.FileName));
            draft = set; changed = true; Fill(draft.Parts.FirstOrDefault());
            status.Text = $"'{set.Name}'을(를) 불러왔어요. 항목 {set.Parts.Count}개 중 {set.Parts.Count(p => p.Enabled)}개가 켜져 있어요. '저장'을 눌러야 적용돼요.";
            if (MessageBox.Show(this, "프리셋에 들어 있는 생성 설정(온도·Top P 등 샘플링, 최대 입력·출력 토큰, 추론 강도, 스트리밍)도 소설 API 설정에 가져올까요?", "SillyTavern 프리셋", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var book = buddy.Novel.State; book.Parameters = sampling.Parameters; if (sampling.MaxContext is int context) book.MaxContext = context; if (sampling.MaxOutput is int output) book.MaxOutput = output;
                if (sampling.Stream is bool stream) book.Stream = stream; if (sampling.ShowThinking is bool thoughts) book.ShowThinking = thoughts; buddy.Novel.Save();
                status.Text += " 생성 설정도 가져왔어요.";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { status.Text = "불러오지 못했어요. " + e.Message; }
    }
    // The messages one request would send now, with a made-up story, so the result of the switches can be read.
    void Preview()
    {
        Apply(); var cast = buddy.StageCharacters().Take(1).Select(c => c.Persona).ToArray();
        var scene = new NovelScene(cast, buddy.Chat.Preferences.Profile.UserName ?? "", buddy.Chat.Preferences.Profile.UserNotes ?? "", "(채팅방의 작가 노트)", "(방의 기억 요약)", new[] { new ChatTurn("(지난 지시)", "(지난번에 쓴 부분)", DateTimeOffset.Now) }, "(이번 지시)");
        string text = string.Join("\n\n", NovelPrompts.Build(draft, scene).Select(m => $"── {m.Role} ──\n{m.Text}"));
        var box = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18), FontSize = 12.5 };
        var window = new Window { Owner = this }; Pastel.Window(window, "조합된 프롬프트", box, 760, 760); window.ShowDialog();
    }
}
