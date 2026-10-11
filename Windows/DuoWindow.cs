using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class DuoWindow : Window
{
    sealed record Choice(string Id, string Name);
    readonly TabControl pages = null!;
    public void ShowPage(int index) => pages.SelectedIndex = index;
    public void CaptureInteraction(string path) { pages.SelectedIndex = 1; UpdateLayout(); Pastel.Capture(this, path); pages.SelectedIndex = 0; }
    public DuoWindow(Buddy buddy)
    {
        var current = buddy.Pref.Duo; var root = new DockPanel { Margin = new Thickness(20) }; var footer = new StackPanel(); var status = Pastel.Text(""); footer.Children.Add(status);
        var tabs = new TabControl { Background = Pastel.Paper, BorderBrush = Pastel.Pink }; pages = tabs; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(tabs);
        StackPanel Tab(string title) { var p = new StackPanel { Margin = new Thickness(18) }; tabs.Items.Add(new TabItem { Header = title, Content = Pastel.Scroll(p), Padding = new Thickness(14, 8, 14, 8) }); return p; }
        var scene = Tab("친구 배치"); scene.Children.Add(Pastel.Text("혼자도, 둘이 함께도 ♡", 23));
        var enabled = new CheckBox { Content = "두 캐릭터 함께 꺼내놓기", IsChecked = current.Enabled }; scene.Children.Add(enabled);
        var choices = buddy.Presets.Items.Select(p => new Choice(p.Id, p.Name)).ToArray();
        scene.Children.Add(Pastel.Text("대표 친구"));
        var first = new ComboBox { DisplayMemberPath = "Name", ItemsSource = new[] { new Choice("", "현재 대표 친구 유지") }.Concat(choices).ToArray() };
        first.SelectedIndex = 0;
        scene.Children.Add(first);
        scene.Children.Add(Pastel.Text("둘째 친구 프리셋"));
        var second = new ComboBox { DisplayMemberPath = "Name", ItemsSource = choices };
        second.SelectedItem = choices.FirstOrDefault(p => p.Id == current.PresetId) ?? choices.FirstOrDefault(p => p.Id != buddy.Pref.ActivePresetId);
        scene.Children.Add(second);
        void RefreshChoices()
        {
            string firstId = (first.SelectedItem as Choice)?.Id ?? "", secondId = (second.SelectedItem as Choice)?.Id ?? "";
            choices = buddy.Presets.Items.Select(p => new Choice(p.Id, p.Name)).ToArray();
            var primaryChoices = new[] { new Choice("", "현재 대표 친구 유지") }.Concat(choices).ToArray();
            first.ItemsSource = primaryChoices;
            first.SelectedItem = primaryChoices.FirstOrDefault(p => p.Id == firstId) ?? primaryChoices[0];
            second.ItemsSource = choices; second.SelectedItem = choices.FirstOrDefault(p => p.Id == secondId) ?? choices.FirstOrDefault(p => p.Id != buddy.Pref.ActivePresetId);
        }
        buddy.Presets.Changed += RefreshChoices; Closed += (_, _) => buddy.Presets.Changed -= RefreshChoices;
        scene.Children.Add(Pastel.Button("선택한 둘째 불러내기", async () =>
        {
            try
            {
                if (second.SelectedItem is not Choice selected) throw new InvalidOperationException("둘째 프리셋을 선택해 주세요.");
                IsEnabled = false;
                await buddy.LoadCompanion(selected.Id);
                enabled.IsChecked = true;
                status.Text = selected.Name + " 불러내기 완료 · 두 친구가 각각 표시됩니다.";
            }
            catch (Exception e) { status.Text = e.Message; }
            finally { IsEnabled = true; }
        }));
        scene.Children.Add(Pastel.Button("둘째 넣어두기 · 한 명만 표시", async () => { buddy.SetCompanion(false); enabled.IsChecked = false; await Storage.SaveAsync(buddy.Pref); status.Text = "대표 친구만 표시합니다."; }, true));
        scene.Children.Add(Pastel.Text("각 캐릭터를 따로 드래그해 위치를 옮길 수 있습니다. 소리는 대표 친구에서 한 번만 재생합니다. 작업 시간·코인·옷장은 함께 사용하며 둘째 친구는 버튼 없이 작게 표시합니다.", 13));
        scene.Children.Add(Pastel.Button("둘째 위치를 대표 친구 옆으로", buddy.ResetCompanionPosition, true)); scene.Children.Add(Pastel.Button("캐릭터 프리셋 관리", buddy.OpenPresets, true));
        var interaction = Tab("둘의 상호작용"); var active = new CheckBox { Content = "AI가 두 친구의 상호작용 대사 생성", IsChecked = current.Interactions }; interaction.Children.Add(active);
        interaction.Children.Insert(0, Pastel.Text("둘이서 주고받는 한마디 ♡", 23));
        interaction.Children.Add(Pastel.Text("AI 자동 재촉 또는 랜덤 한마디가 켜져 있을 때 동작합니다. 두 친구의 프로필을 선택한 AI에 전달해 한 번의 요청으로 두 대사를 생성합니다. 기존 자동 대사 공통 호출 한도와 아래 제한을 함께 지키며 API 사용량이 발생합니다.", 13));
        var share = new CheckBox { Content = "재촉·랜덤 한마디를 두 친구가 번갈아 말하기", IsChecked = current.ShareLines }; interaction.Children.Add(share);
        interaction.Children.Add(Pastel.Text("켜면 재촉과 랜덤 한마디를 대표 친구만 하지 않고 두 친구 중 한 명이 번갈아 가며 말합니다. 둘째 친구는 자기 프리셋의 프로필과 대사를 사용해요.", 12));
        TextBox Field(StackPanel panel, string title, int value) { panel.Children.Add(Pastel.Text(title)); var box = new TextBox { Text = value.ToString() }; panel.Children.Add(box); return box; }
        var min = Field(interaction, "최소 대화 간격 (1~1440분)", current.MinMinutes); var max = Field(interaction, "최대 대화 간격 (최소 이상, 최대 1440분)", current.MaxMinutes);
        var cooldown = Field(interaction, "상호작용 최소 호출 간격 (60~3600초)", current.CooldownSeconds); var limit = Field(interaction, "이번 실행의 최근 1시간 호출 최대 수 (1~60회)", current.HourlyLimit);
        interaction.Children.Add(AutomaticStatusCard.Build(this, buddy, "상호작용 지금 한 번 해보기", true));
        var promptPage = Tab("상호작용 프롬프트"); promptPage.Children.Add(Pastel.Text("치환: {first_name}, {second_name}, {last_line}. first/second 두 대사가 있는 JSON 출력 규칙을 유지해 주세요.", 12));
        var prompt = new TextBox { Text = current.Prompt, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 330, MaxLength = 10000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        promptPage.Children.Add(prompt);
        promptPage.Children.Add(Pastel.Button("기본 프롬프트 복원", () => prompt.Text = new DuoOptions().Prompt, true));
        footer.Children.Add(Pastel.Button("저장하고 닫기", async () =>
        {
            try
            {
                if (buddy.IsChatBusy) throw new InvalidOperationException("채팅 답변 생성이 끝난 뒤 친구 배치를 바꿔 주세요.");
                int Value(TextBox box, int low, int high) => int.TryParse(box.Text, out int n) && n >= low && n <= high ? n : throw new InvalidOperationException("숫자를 표시된 범위 안에 입력해 주세요.");
                var selected = second.SelectedItem as Choice; var primary = first.SelectedItem as Choice;
                if (enabled.IsChecked == true && selected == null) throw new InvalidOperationException("둘째 친구 프리셋을 먼저 만들어 선택해 주세요.");
                string firstId = string.IsNullOrEmpty(primary?.Id) ? buddy.Pref.ActivePresetId : primary.Id;
                if (enabled.IsChecked == true && selected?.Id == firstId) throw new InvalidOperationException("대표와 둘째는 서로 다른 프리셋을 선택해 주세요.");
                var next = buddy.Pref.Duo with
                {
                    Enabled = enabled.IsChecked == true,
                    PresetId = selected?.Id ?? "",
                    Interactions = active.IsChecked == true,
                    ShareLines = share.IsChecked == true,
                    MinMinutes = Value(min, 1, 1440),
                    MaxMinutes = Value(max, 1, 1440),
                    CooldownSeconds = Value(cooldown, 60, 3600),
                    HourlyLimit = Value(limit, 1, 60),
                    Prompt = prompt.Text
                };
                if (next.MaxMinutes < next.MinMinutes) throw new InvalidOperationException("최대 간격은 최소 간격 이상이어야 합니다.");
                IsEnabled = false; buddy.CancelAutomatic();
                if (!string.IsNullOrEmpty(primary?.Id)) buddy.ApplyCharacterPreset(buddy.Presets.Items.First(p => p.Id == primary.Id), false);
                buddy.Pref.Duo = next; buddy.SyncCompanion(); await Storage.SaveAsync(buddy.Pref); Close();
            }
            catch (Exception e) { IsEnabled = true; status.Text = e.Message; }
        }));
        Pastel.Window(this, "친구 배치 · 두 캐릭터", root, 790, 760);
    }
}
