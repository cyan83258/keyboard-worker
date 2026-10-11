using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class ConnectionsWindow : Window
{
    readonly ChatRepository repo;
    readonly ChatPreferences draft;
    readonly ComboBox connections = new(), format = new() { ItemsSource = new[] { "Gemini", "Responses", "Chat Completions" } };
    readonly TextBox name = new(), endpoint = new(), model = new(), header = new();
    readonly PasswordBox key = new();
    readonly CheckBox bearer = new() { Content = "인증 값 앞에 Bearer 붙이기" };
    readonly Expander advanced = new() { Header = "요청 주소 · API 형식 · 인증 옵션", Margin = new Thickness(0, 12, 0, 12) };
    readonly TextBlock status = Pastel.Text("");
    int index;
    bool loading;
    CancellationTokenSource? testing;
    public ConnectionsWindow(ChatRepository repo)
    {
        this.repo = repo; draft = ChatRepository.Clone(repo.Preferences); index = draft.Selected;
        var p = new StackPanel { Margin = new Thickness(24) };
        p.Children.Add(Pastel.Text("작은 친구의 AI 연결", 24)); p.Children.Add(Pastel.Text("제공자를 고르거나 직접 연결을 추가하세요. 모델 ID는 계정에서 이용할 수 있는 이름으로 변경할 수 있습니다."));
        p.Children.Add(connections);
        var actions = new WrapPanel(); actions.Children.Add(Pastel.Button("+ 직접 연결 추가", () =>
        {
            Capture(); if (draft.Connections.Count >= 20) { status.Text = "연결은 최대 20개까지 만들 수 있어요."; return; }
            draft.Connections.Add(new AiConnection { Name = "직접 연결 " + (draft.Connections.Count + 1), Format = "Chat Completions", Endpoint = "http://localhost:1234/v1/chat/completions", Model = "", Header = "Authorization", Bearer = true });
            index = draft.Connections.Count - 1; Refresh();
        }, true));
        actions.Children.Add(Pastel.Button("선택 연결 삭제", () => { if (draft.Connections.Count <= 1) return; draft.Connections.RemoveAt(index); index = Math.Min(index, draft.Connections.Count - 1); Refresh(); }));
        p.Children.Add(actions);
        Field(p, "연결 이름", name); Field(p, "모델 ID", model); Field(p, "API 키 (로컬 서버는 비워둘 수 있음)", key);
        var options = new StackPanel { Margin = new Thickness(10) }; Field(options, "API 응답 형식", format); Field(options, "전체 요청 엔드포인트 URL", endpoint);
        options.Children.Add(Pastel.Text("Gemini URL에는 {model}을 사용할 수 있어요. 직접 연결은 위 세 형식 중 하나를 지원해야 합니다.", 12));
        Field(options, "인증 헤더 이름", header); options.Children.Add(bearer); advanced.Content = options; p.Children.Add(advanced);
        p.Children.Add(Pastel.Text("키는 Windows 계정에 묶어 암호화해 저장합니다. 입력한 채팅과 프로필은 선택한 서버로 전송되며, 이용 요금은 해당 API 계정에 적용됩니다.", 12));
        p.Children.Add(status);
        var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("연결 테스트", async () =>
        {
            if (testing != null) return;
            try
            {
                Capture(); draft.Selected = index; testing = new(); status.Text = "짧은 테스트 메시지를 보내는 중…";
                await new AiClient().Send(draft, Array.Empty<ChatTurn>(), "짧게 인사 한 문장만 해줘.", testing.Token); status.Text = "연결 성공 ♡";
            }
            catch (OperationCanceledException) { status.Text = "테스트가 중단되었거나 시간이 초과됐어요."; }
            catch (Exception e) { status.Text = e.Message; }
            finally { testing?.Dispose(); testing = null; }
        }, true));
        buttons.Children.Add(Pastel.Button("저장하고 사용", () =>
        {
            try
            {
                Capture();
                draft.Selected = index;
                using var check = AiClient.Build(draft.Current, "", "", Array.Empty<ChatTurn>(), "", draft.MaxOutputTokens);
                var latest = ChatRepository.Clone(repo.Preferences);
                latest.Connections = draft.Connections;
                latest.Selected = index;
                repo.Apply(latest);
                Close();
            }
            catch (Exception e) { status.Text = e.Message; }
        }));
        p.Children.Remove(status);
        var footer = new StackPanel { Margin = new Thickness(20, 4, 20, 14) }; footer.Children.Add(status); footer.Children.Add(buttons);
        var layout = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer); layout.Children.Add(Pastel.Scroll(p));
        Pastel.Window(this, "AI 연결", layout, 640, 760);
        connections.SelectionChanged += (_, _) => { if (loading || connections.SelectedIndex < 0) return; Capture(); index = connections.SelectedIndex; Load(); };
        Refresh(); Closed += (_, _) => testing?.Cancel();
    }
    static void Field(StackPanel p, string title, UIElement box) { p.Children.Add(Pastel.Text(title)); p.Children.Add(box); }
    void Capture()
    {
        var c = draft.Connections[index]; c.Name = name.Text.Trim(); c.Format = format.SelectedItem?.ToString() ?? "Chat Completions";
        c.Endpoint = endpoint.Text.Trim(); c.Model = model.Text.Trim(); c.Header = header.Text.Trim(); c.Bearer = bearer.IsChecked == true; c.Secret = SecretVault.Protect(key.Password);
    }
    void Refresh()
    {
        loading = true; connections.ItemsSource = draft.Connections.Select(c => c.Name).ToArray(); connections.SelectedIndex = index; loading = false; Load();
    }
    void Load()
    {
        var c = draft.Connections[index]; name.Text = c.Name; format.SelectedItem = c.Format; endpoint.Text = c.Endpoint; model.Text = c.Model; header.Text = c.Header; bearer.IsChecked = c.Bearer;
        advanced.IsExpanded = index >= 4;
        try { key.Password = SecretVault.Unprotect(c.Secret); } catch (Exception e) { key.Password = ""; status.Text = e.Message; }
    }
}

public sealed class ProfileWindow : Window
{
    readonly ChatRepository repo;
    readonly Dictionary<string, TextBox> fields = new();
    readonly TextBox keep, context, output, timeout;
    readonly CheckBox bubble;
    readonly CheckBox streaming;
    readonly CheckBox autoContinue;
    readonly TextBlock status = Pastel.Text("");
    public ProfileWindow(ChatRepository repo)
    {
        this.repo = repo; var profile = repo.Preferences.Profile;
        var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel(); footer.Children.Add(status);
        var buttons = new WrapPanel(); buttons.Children.Add(Pastel.Button("프롬프트 미리 보기", () =>
        {
            var w = new Window { Owner = this };
            var box = new TextBox { Text = CharacterPrompt.Build(ReadPersona()), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(20) };
            Pastel.Window(w, "캐릭터 대화 프롬프트", box, 680, 680); w.ShowDialog();
        }, true));
        buttons.Children.Add(Pastel.Button("저장하고 닫기", Save)); footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var tabs = new TabControl { Background = Pastel.Paper, BorderBrush = Pastel.Pink }; root.Children.Add(tabs);
        var identity = new StackPanel { Margin = new Thickness(18) }; var voice = new StackPanel { Margin = new Thickness(18) }; var memory = new StackPanel { Margin = new Thickness(18) };
        Tab("♡ 캐릭터", identity); Tab("☆ 말투", voice); Tab("기억과 대화", memory);
        Add(identity, "Name", "캐릭터 이름", profile.Name, 1); Add(identity, "UserName", "사용자 호칭", profile.UserName, 1);
        Add(identity, "Identity", "정체성 · 종족 · 역할", profile.Identity); Add(identity, "Background", "배경 이야기 · 취향", profile.Background);
        Add(identity, "Personality", "성격 · 감정 표현", profile.Personality); Add(identity, "Relationship", "사용자와의 관계", profile.Relationship);
        Add(voice, "Speech", "말투 · 존댓말/반말 · 이모지 · 어미", profile.Speech); Add(voice, "Length", "답변 길이와 형식", profile.Length);
        Add(voice, "Examples", "실제 말투 예시", profile.Examples); Add(voice, "Boundaries", "피할 표현과 행동", profile.Boundaries); Add(voice, "Extra", "추가 지시와 세계관", profile.Extra);
        memory.Children.Add(Pastel.Text("대화 1개 = 내 메시지 + 캐릭터 답변 한 쌍.\n보관 수를 줄이면 초과한 오래된 대화는 즉시 삭제합니다. 0개는 저장하지 않습니다."));
        keep = Number(memory, "자동 보관할 최근 대화 수 (0~200개)", repo.Preferences.KeepTurns);
        context = Number(memory, "답변할 때 참고할 최근 대화 수 (보관 수 이하)", repo.Preferences.ContextTurns);
        output = Number(memory, "답변 출력 한도 (100~16000 토큰)", repo.Preferences.MaxOutputTokens);
        timeout = Number(memory, "전체 답변 대기 시간 (30~600초)", repo.Preferences.RequestTimeoutSeconds);
        autoContinue = new CheckBox { Content = "출력 한도로 잘린 답변 자동 이어받기", IsChecked = repo.Preferences.AutoContinue }; memory.Children.Add(autoContinue);
        memory.Children.Add(Pastel.Text("최대 2회 추가로 요청해 답변을 완성합니다. 사고 과정만 생성된 경우 출력 한도를 일시적으로 늘려 재요청합니다. 추가 요청에는 API 사용량이 발생하며, 서버·연결 오류는 자동 재요청하지 않습니다.", 12));
        bubble = new CheckBox { Content = "답변을 캐릭터 말풍선에도 표시", IsChecked = repo.Preferences.PetBubble }; memory.Children.Add(bubble);
        streaming = new CheckBox { Content = "답장을 실시간으로 조금씩 표시 (스트리밍)", IsChecked = repo.Preferences.Streaming }; memory.Children.Add(streaming);
        Add(memory, "UserNotes", "계속 기억할 메모 (직접 작성)", profile.UserNotes);
        memory.Children.Add(Pastel.Text("메모는 대화가 삭제되어도 남습니다. 자동으로 과거 내용을 요약해 보관하지는 않습니다.", 12));
        Pastel.Window(this, "캐릭터 프로필", root, 680, 740);
        void Tab(string title, StackPanel panel) => tabs.Items.Add(new TabItem { Header = title, Content = Pastel.Scroll(panel), Padding = new Thickness(14, 8, 14, 8) });
    }
    void Add(StackPanel panel, string key, string title, string value, int rows = 3)
    {
        panel.Children.Add(Pastel.Text(title));
        var box = new TextBox { Text = value, AcceptsReturn = rows > 1, TextWrapping = TextWrapping.Wrap, MinHeight = rows == 1 ? 36 : 72, MaxHeight = 140, MaxLength = 4000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        fields[key] = box;
        panel.Children.Add(box);
    }
    static TextBox Number(StackPanel p, string title, int value) { p.Children.Add(Pastel.Text(title)); var box = new TextBox { Text = value.ToString() }; p.Children.Add(box); return box; }
    Persona ReadPersona()
    {
        var p = new Persona { Prompts = repo.Preferences.Profile.Prompts }; foreach (var f in fields) typeof(Persona).GetProperty(f.Key)!.SetValue(p, f.Value.Text.Trim()); return p;
    }
    public void CaptureMemory(string path)
    {
        var layout = (Grid)((Border)Content).Child; var root = (DockPanel)layout.Children[1];
        root.Children.OfType<TabControl>().Single().SelectedIndex = 2; UpdateLayout(); Pastel.Capture(this, path);
    }
    void Save()
    {
        if (!int.TryParse(keep.Text, out var k) || k < 0 || k > 200 || !int.TryParse(context.Text, out var c) || c < 0 || c > k || !int.TryParse(output.Text, out var o) || o < 100 || o > 16000 || !int.TryParse(timeout.Text, out var seconds) || seconds < 30 || seconds > 600)
        { status.Text = "각 숫자를 범위 안에 입력하고, 참고 수는 보관 수 이하로 지정해 주세요."; return; }
        var profile = ReadPersona(); if (profile.Name.Length == 0) { status.Text = "캐릭터 이름을 입력해 주세요."; return; }
        try
        {
            var p = ChatRepository.Clone(repo.Preferences);
            p.Profile = profile;
            p.KeepTurns = k;
            p.ContextTurns = c;
            p.MaxOutputTokens = o;
            p.RequestTimeoutSeconds = seconds;
            p.AutoContinue = autoContinue.IsChecked == true;
            p.PetBubble = bubble.IsChecked == true;
            p.Streaming = streaming.IsChecked == true;
            repo.Apply(p);
            Close();
        }
        catch (Exception e) { status.Text = e.Message; }
    }
}
