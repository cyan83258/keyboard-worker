using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class PresetsWindow : Window
{
    readonly Buddy buddy;
    readonly PresetStore store;
    readonly ListBox list = new() { DisplayMemberPath = "Name", MinHeight = 130, MaxHeight = 230 };
    readonly TextBox name = new() { MaxLength = 80 };
    readonly TextBlock info = Pastel.Text(""), status = Pastel.Text("");
    readonly CheckBox clear = new() { Content = "프리셋 적용할 때 기존 대화 기록 비우기", IsChecked = false };
    public PresetsWindow(Buddy buddy)
    {
        this.buddy = buddy; store = buddy.Presets; var p = new StackPanel { Margin = new Thickness(24) };
        p.Children.Add(Pastel.Text("오늘은 어떤 친구와 함께할까? ♡", 23));
        p.Children.Add(Pastel.Text("현재 저장된 이미지·프로필·대사·손과 몸의 반응 간격·흔들림 강도를 묶어 저장합니다. 먼저 각 설정을 저장한 뒤 프리셋을 만들어 주세요. API 키, 연결 설정, 대화 기록과 타이머 설정은 넣지 않습니다.", 13));
        p.Children.Add(list); p.Children.Add(Pastel.Card(info, true)); p.Children.Add(Pastel.Text("프리셋 이름")); p.Children.Add(name);
        var save = new WrapPanel();
        save.Children.Add(Pastel.Button("현재 캐릭터를 새로 저장", () => Run(() =>
        {
            ValidateName();
            var added = store.Add(name.Text, buddy.Pref, buddy.Chat.Preferences.Profile);
            buddy.Pref.ActivePresetId = added.Id;
            Storage.Save(buddy.Pref);
            Refresh(added.Id);
            status.Text = "새 프리셋을 저장했어요 ♡ 이후 대사 편집도 이 캐릭터에 저장됩니다.";
        })));
        save.Children.Add(Pastel.Button("선택 프리셋에 덮어쓰기", () => Run(() =>
        {
            ValidateName();
            var selected = Selected();
            if (!Confirm("프리셋 덮어쓰기", selected.Name + " 프리셋을 현재 캐릭터로 덮어쓸까요?\n저장돼 있던 이미지·프로필·대사가 지금 설정으로 바뀌며 되돌릴 수 없습니다.")) return;
            string id = selected.Id;
            store.Update(selected, name.Text, buddy.Pref, buddy.Chat.Preferences.Profile);
            Refresh(id);
            status.Text = "선택한 프리셋을 갱신했어요.";
        }), true));
        p.Children.Add(save);
        p.Children.Add(clear); var actions = new WrapPanel();
        actions.Children.Add(Pastel.Button("선택한 캐릭터 적용", () => Run(() => { var selected = Selected(); buddy.ApplyCharacterPreset(selected, clear.IsChecked == true); status.Text = selected.Name + " 적용 완료 ♡"; }))); 
        actions.Children.Add(Pastel.Button("선택 프리셋 삭제", () => Run(() =>
        {
            var selected = Selected();
            if (!Confirm("프리셋 삭제", selected.Name + " 프리셋을 삭제할까요?\n삭제한 프리셋은 되돌릴 수 없습니다. 지금 화면에 있는 캐릭터는 유지됩니다.")) return;
            store.Delete(selected);
            Refresh("");
            status.Text = "프리셋을 삭제했어요.";
        }), true));
        p.Children.Add(actions);
        // One preset as a file: for moving a single character to another PC or sharing it.
        var share = new WrapPanel();
        share.Children.Add(Pastel.Button("선택 프리셋을 파일로 내보내기…", () => Run(() =>
        {
            var selected = Selected(); string safe = string.Concat(selected.Name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c));
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "캐릭터 프리셋 내보내기",
                Filter = "Keyboard Worker 캐릭터 프리셋 (*" + PresetPackage.Extension + ")|*" + PresetPackage.Extension,
                FileName = "프리셋-" + safe + PresetPackage.Extension,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true) return;
            PresetPackage.Export(selected, dialog.FileName); status.Text = selected.Name + " 프리셋을 파일로 저장했어요 ♡\n" + dialog.FileName;
        }), true));
        share.Children.Add(Pastel.Button("프리셋 파일 가져오기…", () => Run(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "캐릭터 프리셋 가져오기", Filter = "Keyboard Worker 캐릭터 프리셋 (*" + PresetPackage.Extension + ")|*" + PresetPackage.Extension + "|모든 파일|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            if (store.Items.Count >= 50) throw new InvalidOperationException("프리셋은 최대 50개까지 저장할 수 있습니다.");
            var added = store.AddImported(PresetPackage.Import(dialog.FileName, Storage.Root, store.Items.Select(i => i.Name))); Refresh(added.Id);
            status.Text = added.Name + " 프리셋을 가져왔어요 ♡ 목록에 추가만 했으니, 쓰려면 ‘선택한 캐릭터 적용’을 눌러 주세요.";
        })));
        p.Children.Add(share);
        p.Children.Add(Pastel.Text("프리셋 파일에는 그 캐릭터의 이미지·프로필(기억 메모 포함)·대사·움직임 설정이 들어갑니다. API 키, AI 연결, 대화 기록은 들어가지 않아요.", 12));
        p.Children.Add(status);
        Pastel.Window(this, "캐릭터 프리셋", Pastel.Scroll(p), 720, 760);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is CharacterPreset selected)
        {
            name.Text = selected.Name;
            string movement = selected.Motion == null ? "움직임 설정 없음 · 현재 값 유지" : $"손 {selected.Motion.HandsMs}ms / 몸 {selected.Motion.BodyMs}ms / 강도 {selected.Motion.Strength}";
            info.Text = $"♡ {selected.Profile.Name}\n{selected.Images.Count}개 이미지 · 고급 반응 {(selected.Advanced ? "켜짐" : "꺼짐")}\n{movement}\n{selected.Profile.Speech}";
        } else { name.Text = buddy.Chat.Preferences.Profile.Name; info.Text = "아직 저장한 프리셋이 없어요. 현재 캐릭터를 저장해 보세요."; } };
        Refresh(buddy.Pref.ActivePresetId);
        store.Changed += StoreChanged; Closed += (_, _) => store.Changed -= StoreChanged;
    }
    void StoreChanged() => Refresh((list.SelectedItem as CharacterPreset)?.Id ?? buddy.Pref.ActivePresetId);
    CharacterPreset Selected() => list.SelectedItem as CharacterPreset ?? throw new InvalidOperationException("프리셋을 먼저 선택해 주세요.");
    void ValidateName() { if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("프리셋 이름을 입력해 주세요."); }
    void Refresh(string id)
    {
        list.ItemsSource = null;
        list.ItemsSource = store.Items.ToArray();
        list.SelectedItem = store.Items.FirstOrDefault(p => p.Id == id) ?? store.Items.FirstOrDefault();
        if (store.Items.Count == 0) { name.Text = buddy.Chat.Preferences.Profile.Name; info.Text = "아직 저장한 프리셋이 없어요. 현재 캐릭터를 저장해 보세요."; }
    }
    // Overwriting and deleting cannot be undone, so both ask first; "No" is the default button.
    bool Confirm(string title, string question) => MessageBox.Show(this, question, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    void Run(Action action) { try { action(); } catch (Exception e) { status.Text = e.Message; } }
}
