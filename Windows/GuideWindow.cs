using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// The manual, readable inside the program: sections on the left, text on the right, with a search box.
public sealed class GuideWindow : Window
{
    readonly ListBox list = new() { Width = 200, Margin = new Thickness(0, 0, 14, 0), DisplayMemberPath = "Title", Background = Pastel.Paper, Foreground = Pastel.Ink, BorderBrush = Pastel.Pink };
    readonly StackPanel page = new() { Margin = new Thickness(4, 0, 12, 8) }; readonly ScrollViewer view;
    public void Select(string key) { list.ItemsSource = GuideText.Sections; list.SelectedItem = GuideText.Find(key); }
    public int Count => GuideText.Sections.Length;
    public void SelectIndex(int index) { list.ItemsSource = GuideText.Sections; list.SelectedIndex = index; UpdateLayout(); }
    public GuideWindow(Buddy buddy)
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(Pastel.Button("첫 실행 안내 다시 보기", buddy.ShowWelcome, true)); footer.Children.Add(Pastel.Button("단축키 안내", buddy.OpenShortcutGuide, true)); footer.Children.Add(Pastel.Button("홈", buddy.OpenHome, true)); footer.Children.Add(Pastel.Button("설정", buddy.OpenSettings, true)); footer.Children.Add(Pastel.Button("닫기", Close));
        var version = Pastel.Text("Keyboard Worker " + AppInfo.Version, 11); version.Opacity = .8; version.VerticalAlignment = VerticalAlignment.Center; version.Margin = new Thickness(12, 0, 0, 0); footer.Children.Add(version);
        var side = new DockPanel(); DockPanel.SetDock(side, Dock.Left); root.Children.Add(side);
        var search = new TextBox { Margin = new Thickness(0, 0, 14, 8), Padding = new Thickness(8, 5, 8, 5), ToolTip = "설명서에서 찾기" }; DockPanel.SetDock(search, Dock.Top); side.Children.Add(search); side.Children.Add(list);
        view = Pastel.Scroll(page); root.Children.Add(view);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is GuideText.Section section) Show(section); };
        search.TextChanged += (_, _) => { var found = GuideText.Search(search.Text); list.ItemsSource = found; if (found.Length > 0) list.SelectedIndex = 0; else { page.Children.Clear(); page.Children.Add(Pastel.Text("'" + search.Text.Trim() + "'이(가) 들어간 항목이 없어요.", 13)); } };
        Pastel.Window(this, "사용 설명서", root, 880, 760); list.ItemsSource = GuideText.Sections; list.SelectedIndex = 0;
    }
    void Show(GuideText.Section section)
    {
        page.Children.Clear(); page.Children.Add(Pastel.Text(section.Title, 23));
        // Blank lines separate paragraphs; a line starting with ■ is a sub-heading.
        foreach (string block in section.Body.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Trim('\n').Split('\n'); int start = 0;
            if (lines[0].StartsWith("■")) { page.Children.Add(new TextBlock { Text = lines[0][1..].Trim(), Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 12, 0, 4) }); start = 1; }
            if (lines.Length > start) page.Children.Add(new TextBlock { Text = string.Join("\n", lines.Skip(start)), Foreground = Pastel.Ink, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 0, 0, 4) });
        }
        view.ScrollToTop();
    }
}

// Shown once, the first time the program starts on this PC.
public sealed class WelcomeWindow : Window
{
    public WelcomeWindow(Buddy buddy)
    {
        var p = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
        p.Children.Add(Pastel.Text("반가워요 ♡ Keyboard Worker", 24));
        p.Children.Add(Pastel.Text("화면 구석에서 함께 일하는 작은 친구예요. 키보드를 치면 반응하고, 멈춰 있으면 살짝 재촉해요.", 14));
        void Step(string title, string body, bool blue)
        {
            var card = new StackPanel(); card.Children.Add(new TextBlock { Text = title, Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 15 });
            card.Children.Add(new TextBlock { Text = body, Foreground = Pastel.Ink, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 20 });
            var border = Pastel.Card(card, blue); border.Padding = new Thickness(14, 10, 14, 11); border.Margin = new Thickness(0, 4, 0, 4); p.Children.Add(border);
        }
        Step("1. 만져 보기", "드래그로 옮기고, 더블클릭으로 홈을, 우클릭으로 메뉴를 열어요. 캐릭터 위에서 Ctrl + 휠을 돌리면 크기가 바뀌고, 마우스로 문지르면 쓰다듬기예요.", false);
        Step("2. 내 캐릭터로 바꾸기", "설정 → 캐릭터에서 그림과 이름·성격을 넣어요. '대사집'에 상황별 대사를 적어 두면 AI 없이도 그 말로 인사하고 재촉합니다.", true);
        Step("3. AI는 원할 때만", "AI를 연결하지 않아도 모든 기본 기능이 동작해요. 연결하면 채팅과 상황에 맞춘 즉석 대사가 더해집니다. 키와 기록은 이 PC에만 저장돼요.", false);
        Step("4. 홈에서 함께 지내기", "캐릭터를 더블클릭하면 홈이 열려요. 출석과 이번 주 목표, 친구와의 친밀도, 모은 대사와 업적, 코인 쓰는 상점이 모두 거기 있어요. 바꾸는 것은 설정에, 자주 켜고 끄는 것은 우클릭 → 빠른 설정에 있습니다.", true);
        var auto = new CheckBox { Content = "Windows를 시작할 때 자동으로 실행하기", IsChecked = Autostart.IsOn(), Margin = new Thickness(2, 10, 0, 4) }; var note = Pastel.Text("", 12);
        auto.Click += (_, _) => { try { Autostart.Set(auto.IsChecked == true); note.Text = auto.IsChecked == true ? "다음 로그인부터 함께 시작해요 ♡" : "자동 실행을 껐어요."; } catch (Exception e) { note.Text = "자동 실행을 바꾸지 못했습니다. " + e.Message; auto.IsChecked = Autostart.IsOn(); } };
        p.Children.Add(auto); p.Children.Add(note);
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Pastel.Button("시작하기 ♡", Close)); buttons.Children.Add(Pastel.Button("홈 열기", () => { Close(); buddy.OpenHome(); }, true)); buttons.Children.Add(Pastel.Button("설정 열기", () => { Close(); buddy.OpenSettings(); }, true));
        buttons.Children.Add(Pastel.Button("대사집 열기", () => { Close(); buddy.OpenLineBook(); }, true)); buttons.Children.Add(Pastel.Button("사용 설명서", () => { Close(); buddy.OpenGuide(); }, true)); p.Children.Add(buttons);
        var later = Pastel.Text("이 안내와 설명서는 설정 → 시스템에서 언제든 다시 볼 수 있어요.  ·  버전 " + AppInfo.Version, 11); later.Opacity = .8; p.Children.Add(later);
        Pastel.Window(this, "처음 만나서 반가워요", Pastel.Scroll(p), 620, 760); ShowInTaskbar = true;
    }
}
