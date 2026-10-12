using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class AutomaticDialogueWindow : Window
{
    readonly Buddy buddy;
    readonly Dictionary<string, TextBox> prompts = new();
    readonly CheckBox nudge, random, context, fallback;
    readonly TextBox min, max, cooldown, limit;
    readonly TextBlock status = Pastel.Text("");
    bool saving;
    public AutomaticDialogueWindow(Buddy buddy)
    {
        this.buddy = buddy; var options = buddy.Chat.Preferences.Automatic; var templates = buddy.Chat.Preferences.Profile.Prompts;
        var root = new DockPanel { Margin = new Thickness(18) }; var footer = new StackPanel(); footer.Children.Add(status);
        footer.Children.Add(Pastel.Button("저장하고 닫기", async () => await Save())); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var tabs = new TabControl { Background = Pastel.Paper, BorderBrush = Pastel.Pink }; root.Children.Add(tabs);
        StackPanel Tab(string title) { var panel = new StackPanel { Margin = new Thickness(16) }; tabs.Items.Add(new TabItem { Header = title, Content = Pastel.Scroll(panel), Padding = new Thickness(8) }); return panel; }
        var settings = Tab("자동 대사"); settings.Children.Add(Pastel.Text("캐릭터가 먼저 툭, 한마디 ♡", 21));
        settings.Children.Add(Pastel.Text("현재 캐릭터: " + buddy.Chat.Preferences.Profile.Name + "\n프롬프트는 현재 프로필과 적용 중인 프리셋에 저장됩니다. 자동 사용 설정·호출 간격은 프로그램 공통입니다.", 12));
        CheckBox Check(string label, bool value) { var box = new CheckBox { Content = label, IsChecked = value }; settings.Children.Add(box); return box; }
        nudge = Check("재촉 대사를 AI가 프로필에 맞춰 생성", options.AiNudge);
        random = Check("중간중간 AI 랜덤 한마디 사용", options.RandomLines);
        fallback = Check("AI 생성 실패·호출 제한 시 직접 설정한 재촉 대사 사용", options.FallbackNudge);
        context = Check("자동 대사에도 최근 채팅 문맥 전달", options.UseConversation);
        TextBox Number(string label, int value) { settings.Children.Add(Pastel.Text(label)); var box = new TextBox { Text = value.ToString() }; settings.Children.Add(box); return box; }
        min = Number("랜덤 한마디 최소 간격 (1~1440분)", options.RandomMinMinutes); max = Number("랜덤 한마디 최대 간격 (최소 이상, 최대 1440분)", options.RandomMaxMinutes);
        cooldown = Number("자동 AI 호출 공통 최소 간격 (60~3600초)", options.CooldownSeconds); limit = Number("이번 실행에서 최근 1시간 자동 AI 호출 최대 수 (1~60회)", options.HourlyLimit);
        settings.Children.Add(Pastel.Text("재촉 시점·반복 간격은 대사·재촉 설정을 따릅니다. 두 기능은 기본 꺼짐입니다. 켜면 선택한 AI 연결로 자동 요청하며 API 사용량이 발생합니다. 채팅 기록에는 자동 대사를 저장하지 않습니다. 대사는 최대 600자이며, 생각하는 모델도 끝까지 답할 수 있게 출력 여유를 넉넉히 두되 실제 사용한 토큰만 집계합니다. 실패하면 5분간 자동 호출을 쉬고, 요청 하나의 대기 시간은 최대 45초입니다.", 12));
        settings.Children.Add(Pastel.Text("휴식·조용한 모드·숨김·앱별 재촉 제외 중에는 멈춥니다. 채팅과 표시 중인 말풍선을 우선하며, 직접 메시지를 보내면 자동 생성은 중단합니다. 입력 문자열이나 화면은 보내지 않습니다.", 12));
        settings.Children.Insert(2, AutomaticStatusCard.Build(this, buddy, "랜덤 한마디 지금 한 번 말해보기", false));
        // The five prompts on one tab, each folded under its name until it is wanted.
        var defaults = new PromptTemplates(); var all = Tab("프롬프트");
        all.Children.Add(Pastel.Text("이름을 누르면 펼쳐져요. 프롬프트는 현재 캐릭터 프로필과 적용 중인 프리셋에 저장됩니다.", 12));
        void Editor(string key, string title, string help)
        {
            var p = new StackPanel { Margin = new Thickness(6, 0, 0, 8) }; p.Children.Add(Pastel.Text(help, 12));
            var property = typeof(PromptTemplates).GetProperty(key)!;
            var box = new TextBox { Text = (string)property.GetValue(templates)!, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 300, MaxLength = 10000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            prompts[key] = box;
            p.Children.Add(box); var buttons = new WrapPanel();
            buttons.Children.Add(Pastel.Button("기본값 복원", () => box.Text = (string)property.GetValue(defaults)!, true));
            buttons.Children.Add(Pastel.Button("실제 조합된 프롬프트 보기", () => Preview(key), true)); p.Children.Add(buttons);
            var heading = StageParts.Fold(title, p, true, _ => { }); heading.Margin = new Thickness(0, 6, 0, 2); all.Children.Add(heading); all.Children.Add(p);
        }
        Editor("Nudge", "AI 재촉", "프로필과 채팅 진행 규칙을 공통으로 전달하고 아래 재촉 지시를 추가합니다. {idle_seconds}: 입력이 없었던 초 · {last_line}: 직전 자동 대사");
        Editor("Random", "랜덤 한마디", "프로필과 채팅 진행 규칙을 공통으로 전달하고 아래 지시를 추가합니다. {last_line}: 직전 자동 대사");
        Editor("Chat", "채팅 진행", "자연스러운 대화 진행 규칙입니다. 프로필 템플릿의 {chat_rules} 자리에 들어갑니다. 프롬프트는 현재 캐릭터 프로필·프리셋에 저장됩니다.");
        Editor("Quick", "짧은 채팅", "한마디 입력에서 채팅할 때 추가하는 지시입니다. 전체 채팅 창에는 추가하지 않습니다.");
        Editor("Profile", "프로필 조합", "치환 항목: {name}, {identity}, {background}, {personality}, {speech}, {user_name}, {relationship}, {length}, {examples}, {boundaries}, {user_notes}, {extra}, {chat_rules}. 프로필 반영에 필요한 항목을 유지해 주세요.");
        Pastel.Window(this, "AI 자동 대사 · 프롬프트", root, 920, 750);
    }
    PromptTemplates Draft() => new() { Profile = prompts["Profile"].Text, Chat = prompts["Chat"].Text, Nudge = prompts["Nudge"].Text, Random = prompts["Random"].Text, Quick = prompts["Quick"].Text };
    void Preview(string kind)
    {
        var profile = buddy.Chat.Preferences.Profile.Snapshot(); profile.Prompts = Draft();
        if (kind == "Quick") profile.Extra += "\n" + profile.Prompts.Quick;
        if (kind is not "Nudge" and not "Random" && buddy.Pref.AdvancedImages) profile.Extra += PetMood.Instruction;
        string system = CharacterPrompt.Build(profile);
        string extra = kind is "Nudge" or "Random" ? PromptTemplates.Expand(kind == "Nudge" ? profile.Prompts.Nudge : profile.Prompts.Random, new Dictionary<string, string> { ["idle_seconds"] = buddy.Pref.IdleSeconds.ToString(), ["last_line"] = "(이전에 표시한 자동 대사)" }) : "(사용자가 전송한 메시지)";
        var box = new TextBox { Text = "[공통 프로필·대화 지시]\n" + system + "\n\n[상황별 지시]\n" + extra, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18) };
        var window = new Window { Owner = this }; Pastel.Window(window, "조합된 프롬프트", box, 760, 730); window.ShowDialog();
    }
    async Task Save()
    {
        if (saving) return;
        try
        {
            int Value(TextBox box, int low, int high) => int.TryParse(box.Text, out int value) && value >= low && value <= high ? value : throw new InvalidOperationException("숫자를 표시된 범위 안에 입력해 주세요.");
            var next = buddy.Chat.Preferences.Snapshot();
            next.Automatic = new()
            {
                AiNudge = nudge.IsChecked == true,
                RandomLines = random.IsChecked == true,
                UseConversation = context.IsChecked == true,
                FallbackNudge = fallback.IsChecked == true,
                RandomMinMinutes = Value(min, 1, 1440),
                RandomMaxMinutes = Value(max, 1, 1440),
                CooldownSeconds = Value(cooldown, 60, 3600),
                HourlyLimit = Value(limit, 1, 60)
            };
            if (next.Automatic.RandomMaxMinutes < next.Automatic.RandomMinMinutes) throw new InvalidOperationException("최대 간격은 최소 간격 이상으로 입력해 주세요.");
            next.Profile.Prompts = Draft(); saving = true; IsEnabled = false; buddy.CancelAutomatic(); await buddy.Chat.ApplyAsync(next);
            await buddy.Presets.SavePromptsAsync(buddy.Pref.ActivePresetId, next.Profile.Prompts); await OrderedStore.FlushAsync(); Close();
        }
        catch (Exception e) { status.Text = "저장하지 못했습니다. " + e.Message; IsEnabled = true; }
        finally { saving = false; }
    }
}
