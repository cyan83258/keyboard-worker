using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class DesktopOptionsWindow : Window
{
    readonly Buddy buddy;
    readonly DesktopOptions draft;
    readonly Dictionary<string, (TextBox Box, int Min, int Max)> numbers = new();
    readonly Dictionary<string, CheckBox> checks = new();
    readonly Dictionary<string, TextBox> shortcuts = new();
    readonly List<(StackPanel Card, TextBox Name, CheckBox Hide, CheckBox Mute, CheckBox Reminder, CheckBox Work)> rules = new();
    readonly TextBox color, background, color2, background2, gap;
    readonly ComboBox position = new() { ItemsSource = new[] { "위", "왼쪽", "오른쪽" } };
    readonly StackPanel rulePanel = new();
    readonly TextBlock status = Pastel.Text("");
    public DesktopOptionsWindow(Buddy buddy)
    {
        this.buddy = buddy; draft = ChatRepository.Clone(buddy.Pref.Desktop);
        var root = new DockPanel { Margin = new Thickness(18) };
        var footer = new StackPanel();
        footer.Children.Add(status);
        footer.Children.Add(Pastel.Button("저장하고 닫기", Save));
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var tabs = new TabControl { Background = Pastel.Paper, BorderBrush = Pastel.Pink }; root.Children.Add(tabs);
        StackPanel Tab(string title) { var p = new StackPanel { Margin = new Thickness(18) }; tabs.Items.Add(new TabItem { Header = title, Content = Pastel.Scroll(p), Padding = new Thickness(9, 8, 9, 8) }); return p; }
        var quick = Tab("대화·단축키"); quick.Children.Add(Pastel.Text("작업 중 툭, 한마디 ♡", 21));
        Check(quick, "QuickButton", "캐릭터 아래에 한마디 버튼 표시", draft.QuickButton);
        quick.Children.Add(Pastel.Text("단축키는 Ctrl/Alt/Win과 문자·F키 조합을 사용하세요. 비워두면 해당 단축키를 끕니다."));
        foreach (var pair in new[] { ("quick", "한마디 입력"), ("visibility", "캐릭터 숨기기 / 보이기"), ("rest", "휴식 시작 / 중단"), ("settings", "설정 열기"), ("quiet", "조용한 작업 모드 켜기 / 끄기") })
        { quick.Children.Add(Pastel.Text(pair.Item2)); var box = new TextBox { Text = draft.Shortcuts.GetValueOrDefault(pair.Item1, "") }; shortcuts[pair.Item1] = box; quick.Children.Add(box); }
        Number(quick, "QuickRestMinutes", "휴식 단축키로 시작할 시간 (1~1440분)", draft.QuickRestMinutes, 1, 1440);
        var face = Tab("표정·반응"); face.Children.Add(Pastel.Text("키보드 반응 속도", 21));
        Number(face, "TypingReactionMs", "손 이미지 최소 반응 간격 (0~2000ms)", draft.TypingReactionMs, 0, 2000);
        Number(face, "BounceReactionMs", "몸 흔들림 최소 반응 간격 (0~2000ms)", draft.BodyReactionMs, 0, 2000);
        face.Children.Add(Pastel.Text("200ms는 초당 최대 5번, 300ms는 약 3번 반응합니다. 0이면 키마다 반응합니다. 화면 반응만 제한하며 입력 감지·소리·작업 시간·경험치는 그대로 유지됩니다."));
        var pacing = new WrapPanel();
        foreach (int interval in new[] { 0, 100, 200, 300, 500 }) pacing.Children.Add(Pastel.Button(interval == 0 ? "제한 없음" : interval + "ms", () => numbers["TypingReactionMs"].Box.Text = interval.ToString(), true));
        face.Children.Add(pacing);
        face.Children.Add(Pastel.Button("타이핑 반응 미리보기", () => buddy.OpenReactionPreview(this, PreviewOptions), true));
        face.Children.Add(Pastel.Text("위 빠른 선택 버튼은 손 간격만 바꿉니다. 미리보기는 현재 입력한 설정을 사용하며 저장하지 않아도 확인할 수 있어요.", 12));
        face.Children.Add(Pastel.Text("표정과 움직임", 21));
        Number(face, "ExpressionSeconds", "대화 감정 표정 유지 (1~120초)", draft.ExpressionSeconds, 1, 120);
        Number(face, "PatSeconds", "쓰다듬기 표정 유지 (1~120초)", draft.PatSeconds, 1, 120);
        Check(face, "TypingPriority", "표정 표시 중에도 키 입력에 손 반응 우선", draft.TypingPriority);
        face.Children.Add(Pastel.Text("끄면 표정을 보여주는 동안 손 이미지는 잠시 멈춥니다. 타이핑 소리와 시간 측정은 계속 동작해요."));
        Check(face, "TypingBounce", "키 입력에 통통 튀고 흔들리는 효과", draft.TypingBounce);
        Number(face, "BounceStrength", "타이핑 흔들림 강도 (1~10)", draft.BounceStrength, 1, 10);
        face.Children.Add(Pastel.Text("캐릭터와 장착한 장식만 함께 움직입니다. 말풍선·한마디·타이머·레벨 표시와 창 위치는 고정됩니다."));
        var bubble = Tab("말풍선");
        Number(bubble, "BubbleFont", "글자 크기 (10~28)", draft.BubbleFont, 10, 28);
        Number(bubble, "BubbleWidth", "말풍선 너비 (200~600)", draft.BubbleWidth, 200, 600);
        Number(bubble, "BubbleHeight", "본문 최대 높이 (80~400)", draft.BubbleHeight, 80, 400);
        Number(bubble, "BubbleOpacity", "불투명도 (30~100%)", draft.BubbleOpacity, 30, 100);
        Check(bubble, "BubbleAutoHide", "말풍선 자동으로 숨기기", draft.BubbleAutoHide);
        Number(bubble, "BubbleHideSeconds", "말풍선 자동 숨김 시간 (1~3600초)", draft.BubbleHideSeconds ?? draft.ReplySeconds, 1, 3600);
        bubble.Children.Add(Pastel.Text("답장·재촉·쓰다듬기·게임 안내에 공통으로 적용됩니다. 끄면 ×를 눌러 닫을 때까지 유지됩니다. 답변 생성 중에는 사라지지 않고, 생성 완료 후부터 시간을 셉니다. 마우스를 올리면 숨김 시간이 멈추고, 벗어나면 남은 시간부터 이어집니다.", 12));
        bubble.Children.Add(Pastel.Text("말풍선 색 · 친구마다 따로", 16));
        bubble.Children.Add(Pastel.Text("기본 색은 테마를 따라 바뀝니다 (딸기팝: 분홍 / 하늘, 사과팝: 노랑 / 연두 …). 직접 고른 색은 테마를 바꿔도 그대로예요.", 12));
        bubble.Children.Add(Pastel.Text("대표 친구 · 글자 색 (#RRGGBB)")); color = new TextBox { Text = draft.BubbleText }; bubble.Children.Add(color);
        bubble.Children.Add(Pastel.Text("대표 친구 · 배경 색 (#RRGGBB)")); background = new TextBox { Text = draft.BubbleBackground }; bubble.Children.Add(background);
        var firstPick = new WrapPanel();
        firstPick.Children.Add(Pastel.Button("글자 색 고르기", () => PickColor(color), true));
        firstPick.Children.Add(Pastel.Button("배경 색 고르기", () => PickColor(background)));
        bubble.Children.Add(firstPick);
        bubble.Children.Add(Pastel.Text("둘째 친구 · 글자 색 (#RRGGBB)")); color2 = new TextBox { Text = draft.BubbleText2 }; bubble.Children.Add(color2);
        bubble.Children.Add(Pastel.Text("둘째 친구 · 배경 색 (#RRGGBB)")); background2 = new TextBox { Text = draft.BubbleBackground2 }; bubble.Children.Add(background2);
        var secondPick = new WrapPanel();
        secondPick.Children.Add(Pastel.Button("글자 색 고르기", () => PickColor(color2), true));
        secondPick.Children.Add(Pastel.Button("배경 색 고르기", () => PickColor(background2)));
        bubble.Children.Add(secondPick);
        bubble.Children.Add(Pastel.Button("말풍선 색을 테마 기본 색으로 되돌리기", () =>
        {
            color.Text = color2.Text = DesktopOptions.StandardBubbleText;
            background.Text = DesktopOptions.StandardBubbleBackground;
            background2.Text = DesktopOptions.StandardBubbleBackground2;
        }, true));
        bubble.Children.Add(Pastel.Text("표시 위치")); position.SelectedItem = draft.BubblePosition; bubble.Children.Add(position);
        Number(bubble, "BubbleGap", "캐릭터와 간격 (0~100px)", draft.BubbleGap, 0, 100);
        Number(bubble, "BubbleOffsetX", "가로 위치 보정 (-600~600px)", draft.BubbleOffsetX, -600, 600);
        Number(bubble, "BubbleOffsetY", "세로 위치 보정 (-600~600px)", draft.BubbleOffsetY, -600, 600);
        bubble.Children.Add(Pastel.Text("실제 캐릭터 그림을 기준으로 붙여 표시합니다. 화면 가장자리나 캐릭터와 겹치는 위치는 자동으로 보정합니다.", 12));
        bubble.Children.Add(Pastel.Text("답장은 작업을 계속 입력해도 유지됩니다. 길면 말풍선 안에서 스크롤하거나 설정의 채팅 창에서 읽을 수 있어요."));
        var apps = Tab("앱 예외"); apps.Children.Add(Pastel.Text("프로그램별로 다르게", 21));
        apps.Children.Add(Pastel.Text("현재 앞에 있는 프로그램의 실행 파일 이름으로 적용합니다. 예: chrome.exe, Code.exe. 경로는 필요 없어요."));
        apps.Children.Add(Pastel.Button("+ 프로그램 추가", () => AddRule(new()), true)); apps.Children.Add(rulePanel); foreach (var rule in draft.AppRules) AddRule(rule);
        var timer = Tab("타이머"); Check(timer, "WorkEnabled", "키보드 작업 시간 자동 측정", draft.WorkEnabled);
        Check(timer, "ShowWorkOnPet", "캐릭터 아래에 오늘 작업 시간 표시", draft.ShowWorkOnPet);
        timer.Children.Add(Pastel.Text("연속 입력 인정 간격 (0.2~10초)")); gap = new TextBox { Text = (draft.WorkGapMs / 1000.0).ToString("0.0") }; timer.Children.Add(gap);
        timer.Children.Add(Pastel.Text("입력 사이 간격이 기준 이하인 구간만 더합니다. 마지막 키 이후 대기 시간, 읽기·생각 시간은 더하지 않습니다. 앱 자체의 메시지·설정 입력, 휴식, 측정 제외 앱도 계산에서 빠집니다."));
        Number(timer, "FocusMinutes", "집중 시간 (1~240분)", draft.FocusMinutes, 1, 240); Number(timer, "BreakMinutes", "휴식 시간 (1~120분)", draft.BreakMinutes, 1, 120);
        Check(timer, "FocusRepeat", "집중 → 휴식 주기를 자동 반복", draft.FocusRepeat);
        timer.Children.Add(Pastel.Button("타이머 열기", buddy.OpenTimers, true));
        Pastel.Window(this, "데스크톱 설정", root, 800, 740);
    }
    void Number(StackPanel p, string key, string label, int value, int min, int max) { p.Children.Add(Pastel.Text(label)); var box = new TextBox { Text = value.ToString() }; p.Children.Add(box); numbers[key] = (box, min, max); }
    public void SelectTab(string title)
    {
        var dock = (DockPanel)((Grid)((Border)Content).Child).Children[1]; var tabs = dock.Children.OfType<TabControl>().Single();
        foreach (TabItem item in tabs.Items) if (item.Header?.ToString() == title) { tabs.SelectedItem = item; break; }
    }
    DesktopOptions PreviewOptions()
    {
        var d = draft.Snapshot();
        foreach (var key in new[] { "TypingReactionMs", "BounceReactionMs", "BounceStrength" }) { var n = numbers[key]; if (int.TryParse(n.Box.Text, out int value)) typeof(DesktopOptions).GetProperty(key)!.SetValue(d, Math.Clamp(value, n.Min, n.Max)); }
        d.TypingBounce = checks["TypingBounce"].IsChecked == true; d.Normalize(); return d;
    }
    static void PickColor(TextBox box)
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        if (DesktopOptions.ValidColor(box.Text)) dialog.Color = System.Drawing.ColorTranslator.FromHtml(box.Text);
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) box.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }
    public void CaptureTabs(string path)
    {
        var layout = (Grid)((Border)Content).Child; var dock = (DockPanel)layout.Children[1]; var tabs = dock.Children.OfType<TabControl>().Single();
        for (int n = 0; n < tabs.Items.Count; n++) { tabs.SelectedIndex = n; UpdateLayout(); Pastel.Capture(this, Path.Combine(path, "ui-desktop-tab-" + n + ".png")); }
    }
    void Check(StackPanel p, string key, string label, bool value) { var c = new CheckBox { Content = label, IsChecked = value }; p.Children.Add(c); checks[key] = c; }
    void AddRule(AppRule rule)
    {
        if (rules.Count >= 50) { status.Text = "프로그램은 최대 50개까지 등록할 수 있어요."; return; }
        var p = new StackPanel { Margin = new Thickness(0, 12, 0, 12) }; var name = new TextBox { Text = rule.Process };
        p.Children.Add(name);
        CheckBox Box(string text, bool value) { var b = new CheckBox { Content = text, IsChecked = value }; p.Children.Add(b); return b; }
        var row = (p, name, Box("캐릭터 숨김", rule.Hide), Box("타이핑 소리 끔", rule.Mute), Box("재촉 끔", rule.NoReminder), Box("작업 시간 측정에서 제외", rule.ExcludeWork));
        p.Children.Add(Pastel.Button("삭제", () => { rules.Remove(row); rulePanel.Children.Remove(p); })); rules.Add(row); rulePanel.Children.Add(p);
    }
    void Save()
    {
        try
        {
            foreach (var pair in numbers)
            {
                if (!int.TryParse(pair.Value.Box.Text, out var n) || n < pair.Value.Min || n > pair.Value.Max) throw new InvalidOperationException("숫자를 표시된 범위 안에 입력해 주세요.");
                typeof(DesktopOptions).GetProperty(pair.Key)!.SetValue(draft, n);
            }
            if (!double.TryParse(gap.Text, out var seconds) || !double.IsFinite(seconds) || seconds < .2 || seconds > 10) throw new InvalidOperationException("연속 입력 간격은 0.2~10초로 입력해 주세요.");
            draft.WorkGapMs = (int)Math.Round(seconds * 1000);
            foreach (var pair in checks) typeof(DesktopOptions).GetProperty(pair.Key)!.SetValue(draft, pair.Value.IsChecked == true);
            if (!DesktopOptions.ValidColor(color.Text) || !DesktopOptions.ValidColor(background.Text) || !DesktopOptions.ValidColor(color2.Text) || !DesktopOptions.ValidColor(background2.Text)) throw new InvalidOperationException("색상은 #875477처럼 #과 6자리 색상 코드로 입력해 주세요.");
            // Refuse combinations in which the text would be hard to read.
            foreach (var (who, ink, fill, standard) in new[] { ("대표 친구", color.Text, background.Text, DesktopOptions.StandardBubbleBackground), ("둘째 친구", color2.Text, background2.Text, DesktopOptions.StandardBubbleBackground2) })
            {
                double contrast = Pastel.Contrast(Pastel.Custom(ink, DesktopOptions.StandardBubbleText), Pastel.Custom(fill, standard));
                if (contrast < 3) throw new InvalidOperationException($"{who} 말풍선의 글자 색과 배경 색이 너무 비슷해서 글씨가 잘 보이지 않아요 (대비 {contrast:0.0}, 3 이상 필요). 더 어두운 글자 색이나 더 밝은 배경 색을 골라 주세요.");
            }
            draft.BubbleText = color.Text;
            draft.BubbleBackground = background.Text;
            draft.BubbleText2 = color2.Text;
            draft.BubbleBackground2 = background2.Text;
            draft.BubblePosition = position.SelectedItem?.ToString() ?? "위";
            draft.Shortcuts = shortcuts.ToDictionary(k => k.Key, k => k.Value.Text.Trim());
            draft.AppRules = rules.Where(r => !string.IsNullOrWhiteSpace(r.Name.Text)).Select(r => new AppRule
            {
                Process = Path.GetFileName(r.Name.Text.Trim()),
                Hide = r.Hide.IsChecked == true,
                Mute = r.Mute.IsChecked == true,
                NoReminder = r.Reminder.IsChecked == true,
                ExcludeWork = r.Work.IsChecked == true
            }).ToList();
            var next = ChatRepository.Clone(buddy.Pref); next.Desktop = draft; buddy.Apply(next); Close();
        }
        catch (Exception e) { status.Text = e.Message; }
    }
}
