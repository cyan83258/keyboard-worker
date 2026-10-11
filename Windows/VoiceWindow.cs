using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace KeyboardWorker;

// Voice clips for each pet: a short MP3 / WAV per situation or expression, played when the pet speaks.
public sealed class VoiceWindow : Window
{
    sealed record Who(string Id, string Name, bool Main);
    readonly Dictionary<string, Dictionary<string, string>> drafts = new();
    public VoiceWindow(Buddy buddy)
    {
        var p = new StackPanel { Margin = new Thickness(22) }; var status = Pastel.Text("", 12);
        p.Children.Add(Pastel.Text("캐릭터 음성 ♡", 22));
        p.Children.Add(Pastel.Text("상황이나 표정마다 짧은 음성(MP3 / WAV, 20MB 이하)을 등록하면 캐릭터가 말풍선을 띄울 때 함께 재생합니다. 상황 → 표정 → '말할 때 (기본)' 순서로 맞는 음성을 찾고, 없으면 조용히 말해요.", 12));
        var options = buddy.Pref.Voice;
        var enabled = new CheckBox { Content = "캐릭터 음성 켜기", IsChecked = options.Enabled }; p.Children.Add(enabled);
        var volumeLabel = Pastel.Text("음성 볼륨: " + options.Volume, 13); var volume = new Slider { Minimum = 0, Maximum = 100, Value = options.Volume, IsSnapToTickEnabled = true, TickFrequency = 1 };
        volume.ValueChanged += (_, _) => volumeLabel.Text = "음성 볼륨: " + (int)volume.Value; p.Children.Add(volumeLabel); p.Children.Add(volume);
        var read = new CheckBox { Content = "등록한 음성이 없으면 Windows 음성으로 대사 읽어주기 (TTS)", IsChecked = options.ReadAloud, Margin = new Thickness(0, 8, 0, 2) }; p.Children.Add(read);
        var readRow = new WrapPanel(); readRow.Children.Add(Pastel.Button("읽어주기 들어보기", () => buddy.PreviewReading("안녕! 오늘도 같이 힘내자.", (int)volume.Value), true)); p.Children.Add(readRow);
        p.Children.Add(Pastel.Text("읽어주기는 이 PC에 설치된 Windows 음성을 사용합니다 (AI 요청 없음). 한국어 음성이 없으면 Windows 설정 → 시간 및 언어 → 음성에서 추가할 수 있어요. 조용한 모드·게임 중 가볍게·음소거 프로그램에서는 음성도 쉽니다.", 11));

        var people = new List<Who> { new(buddy.CharacterId, "대표 · " + buddy.Chat.Preferences.Profile.Name, true) };
        people.AddRange(buddy.Presets.Items.Where(x => x.Id != buddy.Pref.ActivePresetId).OrderByDescending(x => x.Id == buddy.Pref.Duo.PresetId).Select(x => new Who(x.Id, (x.Id == buddy.Pref.Duo.PresetId ? "둘째 · " : "프리셋 · ") + x.Name, false)));
        p.Children.Add(Pastel.Text("음성을 등록할 캐릭터", 13)); var who = new ComboBox { DisplayMemberPath = "Name", ItemsSource = people, SelectedIndex = 0 }; p.Children.Add(who);
        var rows = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; p.Children.Add(rows);
        Dictionary<string, string> Load(Who person) => drafts.TryGetValue(person.Id, out var known) ? known : drafts[person.Id] = new(person.Main ? buddy.Pref.VoicePaths : buddy.Presets.Items.FirstOrDefault(x => x.Id == person.Id)?.VoicePaths ?? new());
        void Fill()
        {
            rows.Children.Clear(); var clips = Load((Who)who.SelectedItem);
            foreach (var (key, title) in VoiceSlots.All)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) }; var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right);
                clips.TryGetValue(key, out var path); bool has = !string.IsNullOrWhiteSpace(path);
                void Small(Button button) { button.Padding = new Thickness(9, 3, 9, 3); button.Margin = new Thickness(4, 0, 0, 0); button.FontSize = 12; actions.Children.Add(button); }
                Small(Pastel.Button("선택…", () =>
                {
                    var dialog = new OpenFileDialog { Title = title + " 음성 선택", Filter = "소리 파일 (MP3 / WAV)|*.mp3;*.wav", CheckFileExists = true }; if (dialog.ShowDialog(this) != true) return;
                    try { SoundAssets.Validate(dialog.FileName); clips[key] = dialog.FileName; status.Text = "'저장'을 눌러야 적용돼요."; Fill(); } catch (Exception e) { status.Text = e.Message; }
                }, true));
                if (has) { Small(Pastel.Button("듣기", () => buddy.PreviewVoice(path!, (int)volume.Value), true)); Small(Pastel.Button("해제", () => { clips.Remove(key); Fill(); })); }
                row.Children.Add(actions);
                row.Children.Add(new TextBlock { Text = (has ? "● " : "○ ") + title + (has ? "  ·  " + (File.Exists(path) ? Path.GetFileName(path) : "파일 없음") : ""), Foreground = Pastel.Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                rows.Children.Add(row);
            }
        }
        who.SelectionChanged += (_, _) => Fill();
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Pastel.Button("저장", async () =>
        {
            try
            {
                IsEnabled = false; var next = buddy.Pref.Snapshot(); next.Voice = new VoiceOptions { Enabled = enabled.IsChecked == true, Volume = (int)volume.Value, ReadAloud = read.IsChecked == true };
                foreach (var person in people)
                {
                    if (!drafts.TryGetValue(person.Id, out var clips)) continue;
                    // Clips are copied into the settings folder so they keep working when the original file moves.
                    var chosen = clips.ToArray(); var stored = await Task.Run(() => chosen.ToDictionary(c => c.Key, c => SoundAssets.Import(c.Value, Storage.Root)));
                    drafts[person.Id] = new(stored);
                    if (person.Main) next.VoicePaths = stored; else await buddy.Presets.SaveVoicesAsync(person.Id, stored);
                }
                buddy.Apply(next, configureKeys: false, persist: false); await buddy.Presets.SaveDialoguesAsync(next); await Storage.SaveAsync(buddy.Pref);
                status.Text = "저장했어요 ♡ " + DateTime.Now.ToString("HH:mm:ss"); Fill();
            }
            catch (Exception e) { status.Text = "저장하지 못했습니다. " + e.Message; }
            finally { IsEnabled = true; }
        }));
        buttons.Children.Add(Pastel.Button("닫기", Close, true)); p.Children.Add(status); p.Children.Add(buttons);
        Pastel.Window(this, "캐릭터 음성", Pastel.Scroll(p), 640, 820); Fill();
    }
}
