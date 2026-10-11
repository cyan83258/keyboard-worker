using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.Linq;

namespace KeyboardWorker;

public sealed class SettingsWindow : Window
{
    readonly Buddy buddy;
    readonly TypingAudio preview = new();
    readonly Preferences draft;
    System.Windows.Threading.DispatcherTimer? previewTimer;
    readonly TextBlock soundLabel = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Pastel.Brush("#C2307E") };
    readonly TextBox idle, repeat, duration;
    readonly Slider size, volume;
    readonly CheckBox top, through, locked, mouse, fullscreen, repeating, sound;
    readonly CheckBox showQuick, showTimer, showLevel;
    string selectedSound;
    readonly TextBox soundPresetName = new() { MaxLength = 80 };
    readonly StackPanel soundPresets = new();
    readonly WrapPanel soundQuick = new();
    readonly TextBlock soundPresetStatus = Pastel.Text("", 12);
    bool saving;
    readonly StackPanel screenPanel = new() { Margin = new Thickness(24) };
    readonly StackPanel reminderPanel = new() { Margin = new Thickness(24) };
    readonly StackPanel soundPanel = new() { Margin = new Thickness(24) };
    StackPanel activePanel = null!;
    public SettingsWindow(Buddy buddy, TypingAudio live)
    {
        this.buddy = buddy;
        draft = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(buddy.Pref))!;
        selectedSound = draft.SoundPath;
        activePanel = screenPanel;
        Heading("화면과 위치"); size = Slider("캐릭터 크기", 100, 400, draft.Size);
        top = new CheckBox { IsChecked = true }; through = Check("마우스 클릭 통과", draft.ClickThrough);
        locked = Check("위치 고정", draft.Locked); fullscreen = Check("전체 화면에서 자동 숨김", draft.HideFullscreen);
        Heading("캐릭터 아래 표시");
        showQuick = Check("한마디 버튼 표시", buddy.Pref.Desktop.QuickButton);
        showTimer = Check("타이머 표시", buddy.Pref.Desktop.ShowWorkOnPet);
        showLevel = Check("레벨 / 코인 표시", buddy.Game.State.ShowBadge);
        var hudActions = new WrapPanel(); hudActions.Children.Add(Pastel.Button("모두 숨기기", () => { showQuick.IsChecked = false; showTimer.IsChecked = false; showLevel.IsChecked = false; }, true));
        hudActions.Children.Add(Pastel.Button("모두 표시", () => { showQuick.IsChecked = true; showTimer.IsChecked = true; showLevel.IsChecked = true; })); activePanel.Children.Add(hudActions);
        Label("숨겨도 시간 측정과 성장 보상은 계속됩니다. 한마디는 단축키나 우클릭으로, 타이머와 상점은 홈(캐릭터 더블클릭)에서 열 수 있어요.");
        activePanel = reminderPanel;
        Heading("살짝 재촉하기"); idle = Number("재촉 시작 (5~86400초)", draft.IdleSeconds);
        repeating = Check("재촉 반복", draft.Repeat); repeat = Number("반복 간격 (5~86400초)", draft.RepeatSeconds);
        duration = Number("말풍선 자동 숨김 시간 (1~3600초)", draft.Desktop.BubbleHideSeconds ?? draft.BubbleSeconds);
        Label("모든 말풍선에 공통으로 적용됩니다. 자동 숨김 켜기/끄기는 데스크톱 설정 → 말풍선에서 변경할 수 있어요.");
        mouse = Check("마우스 활동도 작업으로 인정", draft.MouseActivity);
        Label("재촉·쓰다듬기·인사 등 캐릭터가 하는 말은 설정 → 캐릭터 → 대사집에서 고칩니다."); Button("대사집 열기", buddy.OpenLineBook);
        activePanel = soundPanel;
        Heading("톡톡, 타이핑 소리"); sound = Check("타이핑 소리 켜기", draft.Sound); volume = Slider("볼륨", 0, 100, draft.Volume);
        activePanel.Children.Add(soundQuick);
        Label("MP3 / WAV 지원 · 파일당 20MB 이하. 짧은 소리가 잘 어울려요. 최대 3개까지 겹쳐 재생합니다."); activePanel.Children.Add(soundLabel);
        Button("MP3 / WAV 선택…", ChooseSound);
        Button("미리 듣기", PreviewSound);
        Button("소리 파일 해제", () => { selectedSound = ""; preview.Stop(); RefreshLabels(); });
        Heading("소리 프리셋"); Label("파일을 선택한 뒤 이름을 붙여 저장하세요. ‘이 소리 사용’을 누르면 즉시 전환·저장됩니다. 볼륨은 위 설정을 공통으로 사용합니다.");
        activePanel.Children.Add(soundPresetName); activePanel.Children.Add(Pastel.Button("선택한 소리를 프리셋으로 저장", async () => await SoundAction(async () =>
        {
            if (string.IsNullOrWhiteSpace(soundPresetName.Text) || selectedSound.Length == 0) throw new InvalidOperationException("소리 파일과 프리셋 이름을 입력해 주세요.");
            if (buddy.Pref.SoundPresets.Count >= 50) throw new InvalidOperationException("소리 프리셋은 최대 50개까지 저장할 수 있어요.");
            string source = selectedSound, presetName = soundPresetName.Text.Trim(); string imported = await Task.Run(() => SoundAssets.Import(source, Storage.Root));
            var next = buddy.Pref.Snapshot(); next.SoundPresets.Add(new SoundPreset { Name = presetName, Path = imported });
            next.SoundPath = imported; next.Sound = true; next.Volume = (int)volume.Value; await PersistSound(next);
            soundPresetStatus.Text = presetName + " 저장·적용 완료 ♡";
        }), true));
        activePanel.Children.Add(soundPresetStatus); activePanel.Children.Add(soundPresets); RefreshSoundPresets();
        RefreshLabels();
        var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel(); footer.Children.Add(status); footer.Children.Add(Pastel.Button("닫기", Close)); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var sidebar = new StackPanel { Width = 160, Margin = new Thickness(0, 0, 18, 0) }; DockPanel.SetDock(sidebar, Dock.Left); root.Children.Add(sidebar);
        var content = new ContentControl(); root.Children.Add(content);
        // Pages on the left, each split into titled sections of compact two-column tiles. A search box finds a setting by name.
        var navigation = new System.Collections.Generic.List<System.Windows.Controls.Button>();
        var pageViews = new System.Collections.Generic.Dictionary<StackPanel, ScrollViewer>();
        var tiles = new System.Collections.Generic.List<(string Page, string Title, string Help, Action Open)>();
        ScrollViewer? lastPage = null; string building = "";
        StackPanel Page(string name, string description)
        {
            var page = new StackPanel { Margin = new Thickness(6, 2, 10, 6) };
            page.Children.Add(Pastel.Text(name, 23));
            var about = Pastel.Text(description, 13);
            about.Margin = new Thickness(0, 0, 0, 4);
            page.Children.Add(about);
            var pageView = Pastel.Scroll(page); pageViews[page] = pageView;
            var nav = Pastel.Button(name, () => { content.Content = pageView; lastPage = pageView; foreach (var b in navigation) Pastel.Select(b, (string)b.Content == name); }, true);
            sidebar.Children.Add(nav); navigation.Add(nav); building = name; return page;
        }
        Border Tile(string title, string help, Action open, string? origin = null)
        {
            var heading = new DockPanel();
            var arrow = new TextBlock { Text = "›", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 16, Margin = new Thickness(6, -3, 0, 0) };
            DockPanel.SetDock(arrow, Dock.Right);
            heading.Children.Add(arrow);
            // What the tile opens is told by its "?"; written out only when explanations are set to show.
            if (!Pastel.ShowNotes) { var what = Pastel.Help(help); what.Margin = new Thickness(6, 0, 0, 0); what.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(what, Dock.Right); heading.Children.Add(what); }
            heading.Children.Add(new TextBlock { Text = title, Foreground = Pastel.Ink, FontWeight = FontWeights.Bold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
            var body = new StackPanel();
            body.Children.Add(heading);
            if (Pastel.ShowNotes) body.Children.Add(new TextBlock { Text = help, Foreground = Pastel.Ink, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), Opacity = .85 });
            if (origin != null) body.Children.Add(new TextBlock { Text = origin, Foreground = Pastel.Teal, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
            var tile = new Border
            {
                Child = body,
                Background = Pastel.CardPink,
                BorderBrush = Pastel.Line,
                BorderThickness = new Thickness(1.5, 1.5, 1.5, 3),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 9, 10, 10),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Focusable = true,
                FocusVisualStyle = null
            };
            tile.MouseEnter += (_, _) => tile.Background = Pastel.CardBlue; tile.MouseLeave += (_, _) => tile.Background = Pastel.CardPink;
            tile.MouseLeftButtonUp += (_, _) => open(); tile.KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) { e.Handled = true; open(); } };
            return tile;
        }
        // A titled group of tiles; returns the function that adds a tile to it.
        Action<string, string, Action> Section(StackPanel page, string title)
        {
            var label = new TextBlock { Text = title, Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 14, 0, 3) }; page.Children.Add(label);
            page.Children.Add(new Border { Height = 2, Background = Pastel.PinkBar, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 0, 8, 9) });
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 }; page.Children.Add(grid); string owner = building;
            return (name, help, open) => { grid.Children.Add(Tile(name, help, open)); tiles.Add((owner + " › " + title, name, help, open)); };
        }

        // Four pages of things that are configured. What is done or looked at (attendance, friends, collection, shop) is in the home window.
        var pet = Page("캐릭터", "캐릭터의 모습과 말, 프리셋과 둘째 친구를 정해요.");
        var add = Section(pet, "모습");
        add("캐릭터 이미지", "상황별 이미지 · 크기와 위치 보정 · 좌우반전", OpenImages);
        add("캐릭터 표시", "크기 · 클릭 통과 · 위치 고정 · 아래 버튼과 타이머 숨김", () => Category("화면", screenPanel));
        add = Section(pet, "말과 소리");
        add("캐릭터 프로필", "이름 · 성격 · 말투 · 기억 메모 · 답변 길이", buddy.OpenProfile);
        add("대사집", "재촉 · 쓰다듬기 · 인사 · 상황별 대사 (AI 없이 동작) · 대사 팩 넣기", buddy.OpenLineBook);
        add("캐릭터 음성", "상황·표정별 음성 등록 · 읽어주기(TTS) · 켜기/끄기", buddy.OpenVoices);
        add = Section(pet, "프리셋과 친구");
        add("캐릭터 프리셋", "이미지·프로필·대사·음성을 묶어 저장하고 바꾸기 · 파일로 내보내기", buddy.OpenPresets);
        add("둘째 친구 배치", "둘째 친구 불러내기 · 대표/둘째 선택", buddy.OpenDuo);

        var talk = Page("말 걸기·AI", "캐릭터가 언제 어떻게 먼저 말할지, AI를 어디에 연결할지 정해요.");
        add = Section(talk, "먼저 말하기");
        add("재촉 시작 시간", "입력이 멈춘 뒤 재촉까지의 시간과 반복 간격 · 말풍선 숨김 시간", () => Category("재촉", reminderPanel));
        add("AI 자동 대사 · 프롬프트", "AI 재촉 · 랜덤 한마디 · 지금 상태 확인과 테스트", () => new AutomaticDialogueWindow(buddy) { Owner = this, ShowInTaskbar = false }.ShowDialog());
        add("둘의 상호작용", "두 친구가 주고받는 AI 대사 · 간격 · 지금 상태 확인과 테스트", () => buddy.OpenDuo(1));
        add("주변 알아보기", "쓰는 프로그램에 핀잔·칭찬 · 보는 영상과 노래로 대화", () => buddy.OpenAwareness(this));
        add = Section(talk, "AI 연결");
        add("AI 연결", "제공사 · 모델 · API 키 · 직접 엔드포인트", buddy.OpenConnections);
        add("AI 사용량 · 성능", "호출·토큰 상한 · 저전력 모드 · 게임 중 가볍게 · 시작 속도", buddy.OpenUsage);

        var look = Page("화면·소리", "보이는 것과 들리는 것: 테마, 말풍선, 움직임, 소리와 음악.");        var themeCard = new StackPanel(); themeCard.Children.Add(Pastel.Text("테마", 15)); var themeTiles = new WrapPanel();
        foreach (var (id, name) in Pastel.Themes)
        {
            // A small mock window drawn in that theme's colours: title bar, buttons, and both speech bubbles.
            Brush Tone(string hex) => Pastel.Sample(id, hex);
            Border Chip(string fill, string label, Brush ink) => new()
            {
                Background = Tone(fill),
                BorderBrush = Tone("#A85A8E"),
                BorderThickness = new Thickness(1, 1, 1, 2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 4, 0),
                Child = new TextBlock { Text = label, FontSize = 11, Foreground = ink }
            };
            Border Bubble(string fill, string label) => new()
            {
                Background = Tone(fill),
                BorderBrush = Tone("#A85A8E"),
                BorderThickness = new Thickness(1, 1, 2, 2),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = label, FontSize = 11, Foreground = Tone(DesktopOptions.StandardBubbleText) }
            };
            var chips = new StackPanel { Orientation = Orientation.Horizontal };
            chips.Children.Add(Chip("#FFB9DC", "버튼", Tone("#7E3A6C")));
            chips.Children.Add(Chip("#B3EFF4", "버튼", Tone("#7E3A6C")));
            chips.Children.Add(Chip("#E4479A", "선택", Brushes.White));
            var sample = new StackPanel { Margin = new Thickness(9, 7, 9, 9) };
            sample.Children.Add(new TextBlock { Text = "글자는 이렇게 보여요", FontSize = 11, Foreground = Tone("#7E3A6C"), Margin = new Thickness(0, 0, 0, 5) });
            sample.Children.Add(chips);
            sample.Children.Add(Bubble(DesktopOptions.StandardBubbleBackground, "♡ 대표 친구 말풍선")); sample.Children.Add(Bubble(DesktopOptions.StandardBubbleBackground2, "♡ 둘째 친구 말풍선"));
            var mock = new StackPanel();
            mock.Children.Add(new Border
            {
                Background = Tone("#F9AED4"),
                BorderBrush = Tone("#A85A8E"),
                BorderThickness = new Thickness(0, 0, 0, 1.5),
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Padding = new Thickness(9, 4, 9, 4),
                Child = new TextBlock { Text = "♡ " + name, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Tone("#7E3A6C") }
            });
            mock.Children.Add(sample);
            var tile = new StackPanel { Width = 176, Margin = new Thickness(0, 0, 10, 8) };
            tile.Children.Add(new Border { Background = Tone("#FFF1F8"), BorderBrush = Tone("#A85A8E"), BorderThickness = new Thickness(1.5, 1.5, 3, 3), CornerRadius = new CornerRadius(10), Child = mock });
            var choice = Pastel.Button(id == Pastel.Theme ? name + " · 사용 중" : name + " 사용", () =>
            {
                if (id == Pastel.Theme) return;
                if (MessageBox.Show(this, name + " 테마로 바꿀까요?\n색을 바꾸려면 앱이 바로 다시 시작됩니다.", "테마 변경", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) buddy.ChangeTheme(id);
            }, true);
            Pastel.Select(choice, id == Pastel.Theme); choice.Margin = new Thickness(0, 6, 0, 0); tile.Children.Add(choice); themeTiles.Children.Add(tile);
        }
        themeCard.Children.Add(themeTiles); themeCard.Children.Add(Pastel.Text("디자인은 같고 색만 바뀝니다. 직접 정한 말풍선 색과 장식 아이템 색은 그대로 유지돼요.", 12)); look.Children.Add(Pastel.Card(themeCard));
        add = Section(look, "화면");
        add("빠른 설정 · 상황 프로필", "타이머 · 레벨 · 뮤직플레이어 · 소리 등을 체크박스로 켜고 끄기 · 묶어서 전환", buddy.OpenQuickPanel);
        add("대사 글꼴", "말풍선과 캐릭터 페이지 대사의 글꼴(TTF/OTF)과 글자 크기", () => buddy.OpenFonts(this));
        add("말풍선 모양·위치·색", "친구별 색 · 글자 크기 · 숨김 시간 · 위치 보정", () => buddy.OpenDesktopSection("말풍선"));
        add("타이핑 움직임 · 반응 간격", "손·몸 반응과 흔들림 세기 미리보기", () => buddy.OpenDesktopSection("표정·반응"));
        add("쓰다듬기 · 하트 효과", "마우스 쓰다듬기 감지와 하트 켜기/끄기", () => new PetTouchWindow(buddy) { Owner = this, ShowInTaskbar = false }.ShowDialog());
        add = Section(look, "소리");
        add("타이핑 소리", "MP3/WAV 소리 프리셋과 볼륨", () => Category("소리", soundPanel));
        add("음악 감지 · 뮤직플레이어", "브라우저 음악 감지 · 플레이어 표시 · 듣기 이미지", buddy.OpenMusicSettings);

        var tools = Page("시스템", "시작 방식, 단축키, 프로그램별 예외, 백업과 저장 공간을 관리해요.");        var startCard = new StackPanel(); startCard.Children.Add(Pastel.Text("시작", 15));
        var autostart = new CheckBox { Content = "Windows를 시작할 때 자동으로 실행하기", IsChecked = Autostart.IsOn() }; var autostartNote = Pastel.Text("", 12);
        void DescribeAutostart() => autostartNote.Text = !Autostart.IsOn() ? "꺼져 있어요. 켜면 내 계정의 시작 프로그램에 등록합니다 (관리자 권한 불필요)." : Autostart.Elsewhere() ? "다른 위치의 Keyboard Worker가 등록돼 있어요: " + Autostart.Registered() + "\n체크를 껐다 켜면 지금 실행 중인 프로그램으로 바꿉니다." : "켜져 있어요. 다음 로그인부터 함께 시작합니다.";
        autostart.Click += (_, _) => { try { Autostart.Set(autostart.IsChecked == true); } catch (Exception e) { status.Text = "자동 실행을 바꾸지 못했습니다. " + e.Message; } autostart.IsChecked = Autostart.IsOn(); DescribeAutostart(); };
        DescribeAutostart(); startCard.Children.Add(autostart); startCard.Children.Add(autostartNote); tools.Children.Add(Pastel.Card(startCard));
        tools.Children.Add(Pastel.Card(new UpdatePanel(buddy, this), true));
        add = Section(tools, "단축키와 예외");
        add("단축키 안내", "지금 쓰는 조합과 사용 방법", buddy.OpenShortcutGuide);
        add("단축키 변경", "한마디 · 숨김 · 휴식 · 설정 · 조용한 모드", () => buddy.OpenDesktopSection("대화·단축키"));
        add("프로그램별 예외", "앱에 따라 숨김 · 음소거 · 재촉 제외 · 측정 제외", () => buddy.OpenDesktopSection("앱 예외"));
        add = Section(tools, "저장과 관리");
        add("설정 백업 · 자동 백업", "ZIP 하나로 내보내기 / 가져오기 · 정해 둔 간격으로 자동 백업", () => buddy.OpenBackup(this));
        add("저장 공간 관리 · 자동 정리", "임시·오래된 파일 자동 정리 · 미사용 이미지·소리 정리", () => buddy.OpenStorage(this));
        add("오류 기록 보기", "예상하지 못한 오류가 남긴 기록 파일 열기", () =>
        {
            if (!File.Exists(ErrorLog.FilePath)) { status.Text = "기록된 오류가 없어요 ♡"; return; }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ErrorLog.FilePath) { UseShellExecute = true })?.Dispose(); } catch (Exception e) { status.Text = "기록 파일을 열지 못했습니다. " + e.Message; }
        });
        add("사용 설명서", "기능별 안내 읽기 · 찾기 · 첫 실행 안내 다시 보기", buddy.OpenGuide);
        var extra = Page("추가 모드", "직접 만들거나 받은 모드 파일을 넣고, 켜고 끄고, 지워요.");
        extra.Children.Add(new ModsPanel(buddy, this, text => status.Text = text));

        // Search: type part of a name or description to see matching settings from every page.
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(8, 5, 8, 5), ToolTip = "설정 이름으로 찾기" };
        var hint = new TextBlock { Text = "설정 찾기…", Foreground = Pastel.Line, IsHitTestVisible = false, Margin = new Thickness(11, 7, 0, 0), FontSize = 13 };
        var searchBox = new Grid(); searchBox.Children.Add(search); searchBox.Children.Add(hint); sidebar.Children.Insert(0, searchBox);
        // The way to everything that is not a setting.
        var homeNote = Pastel.Text("출석 · 목표 · 친구 · 도감 · 상점은 홈에 있어요.", 11); homeNote.Margin = new Thickness(4, 16, 0, 2); homeNote.Opacity = .85; sidebar.Children.Add(homeNote);
        var homeButton = Pastel.Button("홈 열기", buddy.OpenHome); sidebar.Children.Add(homeButton);
        var versionLabel = Pastel.Text("버전 " + AppInfo.Version, 11); versionLabel.Opacity = .75; versionLabel.Margin = new Thickness(4, 10, 0, 0); sidebar.Children.Add(versionLabel);
        search.TextChanged += (_, _) =>
        {
            string wanted = search.Text.Trim(); hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (wanted.Length == 0)
            {
                content.Content = lastPage ?? pageViews[pet];
                foreach (var b in navigation) Pastel.Select(b, ReferenceEquals(pageViews.FirstOrDefault(p => (string)b.Content == ((TextBlock)p.Key.Children[0]).Text).Value, content.Content));
                return;
            }
            var found = tiles.Where(t => t.Title.Contains(wanted, StringComparison.OrdinalIgnoreCase) || t.Help.Contains(wanted, StringComparison.OrdinalIgnoreCase) || t.Page.Contains(wanted, StringComparison.OrdinalIgnoreCase)).ToArray();
            var results = new StackPanel { Margin = new Thickness(6, 2, 10, 6) }; results.Children.Add(Pastel.Text("찾은 설정 " + found.Length + "개", 23));
            if (found.Length == 0) results.Children.Add(Pastel.Text("'" + wanted + "'에 맞는 설정이 없어요. 다른 낱말로 찾아보세요.", 13));
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var item in found) grid.Children.Add(Tile(item.Title, item.Help, item.Open, item.Page));
            results.Children.Add(grid);
            content.Content = Pastel.Scroll(results); foreach (var b in navigation) Pastel.Select(b, false);
        };
        lastPage = pageViews[pet]; content.Content = lastPage; foreach (var b in navigation) Pastel.Select(b, b == navigation[0]);
        Pastel.Window(this, "Keyboard Worker · 설정", root, 900, 800);
        Closed += (_, _) => { previewTimer?.Stop(); preview.Dispose(); };
    }
    void Category(string title, StackPanel content)
    {
        if (title == "재촉")
        {
            idle.Text = buddy.Pref.IdleSeconds.ToString(); repeat.Text = buddy.Pref.RepeatSeconds.ToString(); repeating.IsChecked = buddy.Pref.Repeat; mouse.IsChecked = buddy.Pref.MouseActivity;
            duration.Text = (buddy.Pref.Desktop.BubbleHideSeconds ?? buddy.Pref.BubbleSeconds).ToString();
        }
        if (title == "화면")
        {
            size.Value = buddy.Pref.Size;
            top.IsChecked = true;
            through.IsChecked = buddy.Pref.ClickThrough;
            locked.IsChecked = buddy.Pref.Locked;
            fullscreen.IsChecked = buddy.Pref.HideFullscreen;
            showQuick.IsChecked = buddy.Pref.Desktop.QuickButton;
            showTimer.IsChecked = buddy.Pref.Desktop.ShowWorkOnPet;
            showLevel.IsChecked = buddy.Game.State.ShowBadge;
        }
        if (title == "소리") { selectedSound = buddy.Pref.SoundPath; sound.IsChecked = buddy.Pref.Sound; volume.Value = buddy.Pref.Volume; RefreshLabels(); RefreshSoundPresets(); }
        CreateCategory(title, content).ShowDialog();
    }
    public void CapturePages(string path)
    {
        var dock = (DockPanel)((Grid)((Border)Content).Child).Children[1];
        var sidebar = (StackPanel)dock.Children[1];
        var buttons = sidebar.Children.OfType<System.Windows.Controls.Button>().Where(b => (string)b.Content != "홈 열기").ToArray();
        for (int i = 0; i < buttons.Length; i++) { buttons[i].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); UpdateLayout(); Pastel.Capture(this, Path.Combine(path, "ui-settings-section-" + i + ".png")); }
        buttons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    }
    Window CreateCategory(string title, StackPanel content)
    {
        var w = new Window { Owner = this, ShowInTaskbar = false };
        var layout = new DockPanel();
        var footer = new StackPanel { Margin = new Thickness(20, 0, 20, 16) };
        var error = Pastel.Text(""); footer.Children.Add(error);
        footer.Children.Add(Pastel.Button("저장하고 닫기", async () => { if (await Save(title)) w.Close(); else error.Text = status.Text; }));
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer); var scroll = Pastel.Scroll(content); layout.Children.Add(scroll);
        Pastel.Window(w, title + " 설정", layout, 520, 650);
        w.Closed += (_, _) => { scroll.Content = null; previewTimer?.Stop(); preview.Stop(); }; return w;
    }
    public void CaptureCategories(string path)
    {
        foreach (var pair in new[] { ("screen", screenPanel), ("reminder", reminderPanel), ("sound", soundPanel) })
        {
            var w = CreateCategory(pair.Item1 == "reminder" ? "재촉" : pair.Item1, pair.Item2); w.Show(); Pastel.Capture(w, Path.Combine(path, "ui-" + pair.Item1 + ".png"));
            w.Close();
        }
        var images = new ImageSettingsWindow(buddy) { Owner = this };
        images.Show();
        Pastel.Capture(images, Path.Combine(path, "ui-images.png"));
        images.CaptureAdvanced(Path.Combine(path, "ui-images-advanced.png"));
        images.Close();
    }
    void OpenImages()
    {
        new ImageSettingsWindow(buddy) { Owner = this, ShowInTaskbar = false }.ShowDialog();
        draft.ImagePaths = new(buddy.Pref.ImagePaths); draft.AdvancedImages = buddy.Pref.AdvancedImages; draft.SkinPath = buddy.Pref.SkinPath;
    }
    void PreviewSound()
        => PreviewSoundFile(selectedSound);
    void PreviewSoundFile(string soundPath)
    {
        previewTimer?.Stop();
        var p = new Preferences { SoundPath = soundPath, Volume = (int)volume.Value, Sound = true }; preview.Configure(p);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) }; int attempts = 0;
        previewTimer = timer;
        timer.Tick += (_, _) => { if (preview.Play(p, true)) timer.Stop(); else if (++attempts >= 10) { timer.Stop(); MessageBox.Show(this, preview.Error.Length > 0 ? preview.Error : "MP3/WAV를 선택하고 다시 시도해 주세요."); } };
        timer.Start(); Closed += (_, _) => timer.Stop();
    }
    void Heading(string text) => activePanel.Children.Add(new TextBlock { Text = text, Foreground = Pastel.Hot, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 18, 0, 10) });
    void Label(string text) => activePanel.Children.Add(text.Length >= Pastel.NoteLength ? Pastel.Text(text, 13) : new TextBlock { Text = text, Foreground = Pastel.Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) });
    CheckBox Check(string text, bool value) { var c = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 6, 0, 6) }; activePanel.Children.Add(c); return c; }
    TextBox Number(string text, int value) { Label(text); var box = new TextBox { Text = value.ToString(), Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(6) }; activePanel.Children.Add(box); return box; }
    Slider Slider(string text, int min, int max, int value)
    {
        var label = new TextBlock { Text = text + ": " + value, Margin = new Thickness(0, 3, 0, 6) }; activePanel.Children.Add(label);
        var s = new Slider { Minimum = min, Maximum = max, Value = value, IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(0, 4, 0, 12) };
        s.ValueChanged += (_, _) => label.Text = text + ": " + (int)s.Value;
        activePanel.Children.Add(s);
        return s;
    }
    void Button(string text, Action action)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 5, 0, 5), HorizontalAlignment = HorizontalAlignment.Stretch };
        b.Click += (_, _) => action();
        activePanel.Children.Add(b);
    }
    void RefreshLabels() { soundLabel.Text = selectedSound.Length == 0 ? "선택한 소리 없음" : buddy.Pref.SoundPresets.FirstOrDefault(p => p.Path == selectedSound)?.Name ?? Path.GetFileName(selectedSound); }
    async Task SoundAction(Func<Task> action)
    {
        if (saving) return;
        saving = true; soundPanel.IsEnabled = false; soundPresetStatus.Text = "저장 중…";
        try { await action(); }
        catch (Exception e) { soundPresetStatus.Text = "소리 설정을 저장하지 못했습니다. " + e.Message; }
        finally { saving = false; soundPanel.IsEnabled = true; }
    }
    async Task PersistSound(Preferences next)
    {
        buddy.Apply(next, configureKeys: false, persist: false); await Storage.SaveAsync(buddy.Pref);
        selectedSound = next.SoundPath; sound.IsChecked = next.Sound; preview.Stop(); RefreshLabels(); RefreshSoundPresets();
    }
    void RefreshSoundPresets()
    {
        soundPresets.Children.Clear(); soundQuick.Children.Clear();
        foreach (var preset in buddy.Pref.SoundPresets)
        {
            soundQuick.Children.Add(Pastel.Button(preset.Name, async () => await UseSoundPreset(preset), true));
            var row = new StackPanel(); row.Children.Add(Pastel.Text(preset.Name + (preset.Path == buddy.Pref.SoundPath ? " · 사용 중" : ""), 15));
            row.Children.Add(Pastel.Text(Path.GetExtension(preset.Path).TrimStart('.').ToUpperInvariant(), 11));
            var actions = new WrapPanel(); actions.Children.Add(Pastel.Button("이 소리 사용", async () => await UseSoundPreset(preset), true));
            actions.Children.Add(Pastel.Button("미리 듣기", () => PreviewSoundFile(preset.Path)));
            actions.Children.Add(Pastel.Button("프리셋 삭제", async () => await SoundAction(async () =>
            {
                var next = buddy.Pref.Snapshot(); next.SoundPresets.RemoveAll(p => p.Id == preset.Id); await PersistSound(next); soundPresetStatus.Text = "프리셋을 삭제했어요. 사용 중인 소리는 유지합니다.";
            }))); row.Children.Add(actions); soundPresets.Children.Add(Pastel.Card(row, true));
        }
        if (buddy.Pref.SoundPresets.Count == 0) soundPresets.Children.Add(Pastel.Text("저장한 소리 프리셋이 없어요.", 12));
    }
    Task UseSoundPreset(SoundPreset preset) => SoundAction(async () =>
    {
        await Task.Run(() => SoundAssets.Validate(preset.Path)); var next = buddy.Pref.Snapshot(); next.SoundPath = preset.Path; next.Sound = true; next.Volume = (int)volume.Value;
        await PersistSound(next); soundPresetStatus.Text = preset.Name + " 적용 완료 ♡";
    });
    void ChooseSound()
    {
        var dialog = new OpenFileDialog { Title = "타이핑 소리 선택", Filter = "소리 파일 (MP3 / WAV)|*.mp3;*.wav|MP3 파일|*.mp3|WAV 파일|*.wav", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            try { SoundAssets.Validate(dialog.FileName); selectedSound = dialog.FileName; soundPresetName.Text = Path.GetFileNameWithoutExtension(dialog.FileName); RefreshLabels(); soundPresetStatus.Text = "미리 듣기로 소리를 확인해 주세요."; }
            catch (Exception e) { soundPresetStatus.Text = e.Message; }
        }
    }
    async Task<bool> Save(string category)
    {
        if (saving) { status.Text = "저장 중입니다. 잠시 기다려 주세요."; return false; }
        if (!int.TryParse(idle.Text, out int i) || i < 5 || i > 86400 || !int.TryParse(repeat.Text, out int r) || r < 5 || r > 86400 || !int.TryParse(duration.Text, out int d) || d < 1 || d > 3600)
        { status.Text = "시간을 표시된 범위 안의 정수로 입력해 주세요."; return false; }
        try
        {
            saving = true;
            var next = buddy.Pref.Snapshot();
            if (category == "화면")
            {
                next.Size = (int)size.Value;
                next.Topmost = top.IsChecked == true;
                next.ClickThrough = through.IsChecked == true;
                next.Locked = locked.IsChecked == true;
                next.HideFullscreen = fullscreen.IsChecked == true;
                next.Desktop.QuickButton = showQuick.IsChecked == true; next.Desktop.ShowWorkOnPet = showTimer.IsChecked == true;
            }
            if (category == "재촉")
            {
                next.IdleSeconds = i;
                next.RepeatSeconds = r;
                next.BubbleSeconds = Math.Min(d, 60);
                next.Desktop.BubbleHideSeconds = d;
                next.MouseActivity = mouse.IsChecked == true;
                next.Repeat = repeating.IsChecked == true;
            }
            if (category == "소리" && selectedSound != buddy.Pref.SoundPath && selectedSound.Length > 0)
            {
                string source = selectedSound;
                status.Text = "소리 파일을 저장하는 중…"; selectedSound = await Task.Run(() => SoundAssets.Import(source, Storage.Root));
            }
            if (category == "소리") { next.SoundPath = selectedSound; next.Sound = sound.IsChecked == true; next.Volume = (int)volume.Value; }
            buddy.Apply(next, persist: false);
            await Storage.SaveAsync(buddy.Pref);
            if (category == "화면") { var gameOptions = buddy.Game.State.Snapshot(); gameOptions.ShowBadge = showLevel.IsChecked == true; buddy.Game.Options(gameOptions); }
            status.Text = "저장했어요 ♡"; return true;
        }
        catch (Exception e) { status.Text = "저장하지 못했습니다. " + e.Message; return false; }
        finally { saving = false; }
    }
}
