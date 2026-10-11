using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace KeyboardWorker;

public sealed class BackupWindow : Window
{
    readonly Buddy buddy; readonly TextBlock status = Pastel.Text("", 12); readonly StackPanel panel = new() { Margin = new Thickness(22) }; bool busy;
    public BackupWindow(Buddy buddy)
    {
        this.buddy = buddy; panel.Children.Add(Pastel.Text("설정 백업 · 복원", 23));
        panel.Children.Add(Pastel.Text("캐릭터 프리셋, 이미지, 소리, 단축키, 성장 기록과 모든 설정을 ZIP 파일 하나로 저장하고 다시 불러올 수 있어요.", 13));
        var media = new CheckBox { Content = "이미지·소리 파일 포함 (용량이 커질 수 있어요)", IsChecked = true };
        var chat = new CheckBox { Content = "대화 기록 포함", IsChecked = false };
        var noKeys = new CheckBox { Content = "API 키 제외", IsChecked = false };
        var export = new StackPanel();
        export.Children.Add(media);
        export.Children.Add(chat);
        export.Children.Add(noKeys);
        export.Children.Add(Pastel.Button("백업 파일로 내보내기…", async () => await Export(media.IsChecked == true, chat.IsChecked == true, noKeys.IsChecked != true), true));
        export.Children.Add(Pastel.Text("API 키는 암호화된 상태로 들어가며 이 Windows 계정에서만 풀립니다. 제외하면 암호화된 키도 넣지 않아요. 키를 뺀 백업을 가져올 때는 이 PC에 저장돼 있던 키를 그대로 둡니다.", 12));
        export.Children.Add(Pastel.Text("이미지·소리를 빼면 설정만 작게 저장됩니다. 같은 PC에서 되돌릴 때는 그것으로 충분해요.", 12)); panel.Children.Add(Pastel.Card(export));
        var import = new StackPanel(); import.Children.Add(Pastel.Button("백업 파일에서 가져오기…", Import));
        import.Children.Add(Pastel.Text("백업에 들어 있는 설정이 현재 설정을 덮어쓰고, 앱이 다시 시작됩니다. 덮어쓰기 전의 설정은 설정 폴더의 backups 안에 자동으로 보관돼요 (최근 5개).", 12));
        import.Children.Add(Pastel.Text("API 키는 저장한 Windows 계정에서만 복원됩니다. 다른 PC로 옮겼다면 AI 연결에서 키를 다시 입력해 주세요.", 12)); panel.Children.Add(Pastel.Card(import, true));
        var tidy = buddy.Pref.Tidy; var auto = new StackPanel(); auto.Children.Add(Pastel.Text("자동 백업", 16));
        var every = new ComboBox { ItemsSource = new[] { "끄기", "매일", "3일마다", "7일마다", "14일마다", "30일마다" } }; int[] days = { 0, 1, 3, 7, 14, 30 }; every.SelectedIndex = Math.Max(0, Array.IndexOf(days, tidy.AutoBackupDays));
        var keep = new ComboBox { ItemsSource = new[] { "최근 3개 보관", "최근 5개 보관", "최근 10개 보관" } }; int[] kept = { 3, 5, 10 }; keep.SelectedIndex = Math.Max(0, Array.IndexOf(kept, tidy.AutoBackupKeep)); if (keep.SelectedIndex < 0) keep.SelectedIndex = 1;
        var autoMedia = new CheckBox { Content = "이미지·소리도 함께 (백업 하나가 수십 MB가 될 수 있어요)", IsChecked = tidy.AutoBackupMedia, Margin = new Thickness(0, 6, 0, 2) }; var autoList = Pastel.Text("", 12);
        void ListAuto() { var files = Housekeeping.AutoBackups(Storage.Root); autoList.Text = files.Length == 0 ? "아직 자동 백업이 없어요." : "보관 중 " + files.Length + "개 · 가장 최근 " + files[0].LastWriteTime.ToString("yyyy-MM-dd HH:mm") + " (" + (files.Sum(f => f.Length) / 1048576.0).ToString("0.0") + "MB)"; }
        async Task SaveAuto() { buddy.Pref.Tidy = buddy.Pref.Tidy with { AutoBackupDays = days[Math.Max(0, every.SelectedIndex)], AutoBackupKeep = kept[Math.Max(0, keep.SelectedIndex)], AutoBackupMedia = autoMedia.IsChecked == true }; await Storage.SaveAsync(buddy.Pref); }
        every.SelectionChanged += async (_, _) => await SaveAuto(); keep.SelectionChanged += async (_, _) => await SaveAuto(); autoMedia.Click += async (_, _) => await SaveAuto();
        auto.Children.Add(Pastel.Text("프로그램을 켜 둔 동안 정해 둔 간격으로 설정·대화 기록을 설정 폴더의 backups\\auto 에 저장합니다. 오래된 것은 저절로 지워져요.", 12));
        auto.Children.Add(every); auto.Children.Add(keep); auto.Children.Add(autoMedia); auto.Children.Add(autoList);
        var autoButtons = new WrapPanel();
        autoButtons.Children.Add(Pastel.Button("지금 백업", async () =>
        {
            if (busy) return; busy = true; panel.IsEnabled = false; status.Text = "자동 백업을 만드는 중…";
            try
            {
                await SaveAuto(); buddy.Game.Save(true); buddy.Adventure.Save(true); buddy.Progress.Save(true); await OrderedStore.FlushAsync(); string root = Storage.Root; var options = buddy.Pref.Tidy;
                string made = await Task.Run(() => Housekeeping.AutoBackup(root, options, DateTime.Now)); buddy.Pref.Tidy = buddy.Pref.Tidy with { LastBackup = DateTime.Now.ToString("yyyy-MM-dd") }; await Storage.SaveAsync(buddy.Pref);
                status.Text = "백업했어요 ♡ " + Path.GetFileName(made); ListAuto();
            }
            catch (Exception e) { status.Text = "백업하지 못했습니다. " + e.Message; }
            finally { busy = false; panel.IsEnabled = true; }
        }, true));
        autoButtons.Children.Add(Pastel.Button("가장 최근 자동 백업으로 되돌리기", () =>
        {
            var files = Housekeeping.AutoBackups(Storage.Root); if (files.Length == 0) { status.Text = "되돌릴 자동 백업이 없어요."; return; }
            if (MessageBox.Show(this, files[0].LastWriteTime.ToString("yyyy-MM-dd HH:mm") + "의 자동 백업으로 설정을 되돌리고 앱을 다시 시작할까요?\n지금 설정은 backups 폴더에 따로 보관됩니다.", "자동 백업으로 되돌리기", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try { SettingsBackup.Stage(files[0].FullName); buddy.Restart(); } catch (Exception e) { status.Text = "되돌리지 못했습니다. " + e.Message; }
        }));
        autoButtons.Children.Add(Pastel.Button("백업 폴더 열기", () => { try { string folder = Path.Combine(Storage.Root, "backups"); Directory.CreateDirectory(folder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose(); } catch (Exception e) { status.Text = e.Message; } }, true));
        auto.Children.Add(autoButtons); panel.Children.Add(Pastel.Card(auto)); ListAuto();
        panel.Children.Add(status);
        Pastel.Window(this, "설정 백업 · 복원", Pastel.Scroll(panel), 640, 820);
    }
    async Task Export(bool media, bool chat, bool keys)
    {
        if (busy) return;
        var dialog = new SaveFileDialog { Title = "설정 백업 저장", Filter = "Keyboard Worker 백업 (*.zip)|*.zip", FileName = "KeyboardWorker-backup-" + DateTime.Now.ToString("yyyyMMdd") + ".zip", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        busy = true; panel.IsEnabled = false; status.Text = "백업 파일을 만드는 중…";
        try
        {
            // Write what is still only in memory before copying the folder.
            Storage.Save(buddy.Pref); buddy.Game.Save(true); buddy.Adventure.Save(true); await OrderedStore.FlushAsync();
            string root = Storage.Root, target = dialog.FileName; await Task.Run(() => SettingsBackup.Export(root, target, media, chat, keys));
            status.Text = "백업을 저장했어요 ♡\n" + target + " · " + (new FileInfo(target).Length / 1048576.0).ToString("0.0") + "MB";
        }
        catch (Exception e) { status.Text = "백업을 저장하지 못했습니다. " + e.Message; }
        finally { busy = false; panel.IsEnabled = true; }
    }
    void Import()
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Title = "설정 백업 선택", Filter = "Keyboard Worker 백업 (*.zip)|*.zip", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var info = SettingsBackup.Describe(dialog.FileName);
            string summary = info.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "에 만든 백업 · " + (info.Media ? "이미지·소리 포함" : "설정만") + (info.Chat ? " · 대화 기록 포함" : "") + (info.Keys ? " · API 키 포함(암호화)" : " · API 키 제외");
            // A backup also replaces the AI connections, and chats are sent wherever they point: show the servers, and say so when one is new.
            var hosts = SettingsBackup.ConnectionHosts(dialog.FileName); var known = buddy.Chat.Preferences.Connections.Select(c => Uri.TryCreate(c.Endpoint.Replace("{model}", "m"), UriKind.Absolute, out var uri) ? uri.Host : "").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = hosts.Where(h => !known.Contains(h)).ToArray();
            if (hosts.Length > 0) summary += "\n\nAI 연결 주소: " + string.Join(", ", hosts);
            if (added.Length > 0) summary += "\n⚠ 지금 설정에 없는 주소가 있습니다: " + string.Join(", ", added) + "\n직접 만든 백업이 아니라면 가져오지 마세요. 대화 내용이 이 주소로 전송될 수 있습니다.";
            if (MessageBox.Show(this, summary + "\n\n현재 설정을 이 백업으로 덮어쓰고 앱을 다시 시작할까요?", "설정 가져오기", MessageBoxButton.YesNo, added.Length > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            SettingsBackup.Stage(dialog.FileName); buddy.Restart();
        }
        catch (Exception e) { status.Text = "백업을 가져오지 못했습니다. " + e.Message; }
    }
}
