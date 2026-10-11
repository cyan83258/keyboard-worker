using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace KeyboardWorker;

// The "추가 모드" page of the settings: mod files the user made or was given. Each can be switched on and off, looked at and removed.
public sealed class ModsPanel : StackPanel
{
    readonly Buddy buddy; readonly Window owner; readonly Action<string> say; readonly StackPanel list = new();
    public ModsPanel(Buddy buddy, Window owner, Action<string> say)
    {
        this.buddy = buddy; this.owner = owner; this.say = say;
        Children.Add(Pastel.Text("모드 파일(.json)로 테마 · 대사 팩 · 상황 프로필 · 캐릭터 페이지 모드를 더할 수 있어요. 파일로 넣거나 GitHub 주소로 설치합니다. 모드는 내용만 담은 데이터 파일이라 프로그램 코드를 실행하지 않습니다.", 12));
        var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("모드 파일 추가…", () =>
        {
            var dialog = new OpenFileDialog { Title = "모드 파일 선택", Filter = "Keyboard Worker 모드 (*.json)|*.json", Multiselect = true, CheckFileExists = true }; if (dialog.ShowDialog(owner) != true) return;
            string result = "";
            foreach (string file in dialog.FileNames) { try { var mod = ModStore.Current.Install(file); result += "'" + mod.Name + "' 추가 (" + mod.Summary + ")  "; } catch (Exception e) { result += Path.GetFileName(file) + ": " + e.Message + "  "; } }
            Changed(result.Trim());
        }));
        buttons.Children.Add(Pastel.Button("예시 모드 파일 만들기…", () =>
        {
            var dialog = new SaveFileDialog { Title = "예시 모드 저장", Filter = "Keyboard Worker 모드 (*.json)|*.json", FileName = "my-first-mod.json" }; if (dialog.ShowDialog(owner) != true) return;
            try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(ModFile.Sample(), ModFile.Writing), new System.Text.UTF8Encoding(false)); say("예시를 저장했어요. 메모장으로 열어 고친 뒤 '모드 파일 추가'로 넣어 보세요."); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { say("저장하지 못했습니다. " + e.Message); }
        }, true));
        buttons.Children.Add(Pastel.Button("모드 폴더 열기", () => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ModStore.Current.Folder) { UseShellExecute = true })?.Dispose(); } catch (Exception e) { say(e.Message); } }, true));
        Children.Add(buttons);
        // From GitHub: a repository, a folder or one mod file. Only GitHub's own addresses are fetched, and what arrives is read as data.
        var link = new StackPanel(); link.Children.Add(Pastel.Text("GitHub에서 설치", 14));
        link.Children.Add(Pastel.Text("모드 파일이 올라가 있는 GitHub 주소를 붙여 넣으세요. 저장소 주소(github.com/이름/저장소)를 넣으면 그 안의 모드 파일을 모두, 파일 주소를 넣으면 그 모드 하나를 설치합니다.", 12));
        var row = new DockPanel(); var address = new TextBox { MaxLength = 500, Padding = new Thickness(8, 5, 8, 5) };
        Button? get = null; get = Pastel.Button("내려받아 설치", async () =>
        {
            if (address.Text.Trim().Length == 0) { say("GitHub 주소를 붙여 넣어 주세요."); return; }
            get!.IsEnabled = false; say("GitHub에서 받는 중…");
            try
            {
                var result = await ModDownloads.Install(ModStore.Current, address.Text, System.Threading.CancellationToken.None); address.Clear();
                Changed(string.Join("  ", result.Installed.Select(m => "'" + m.Name + "' 설치 (" + m.Summary + ")")) + (result.Problems.Count > 0 ? "  읽지 못한 파일 — " + string.Join(" / ", result.Problems) : ""));
            }
            catch (Exception e) { say("설치하지 못했습니다. " + e.Message); }
            finally { get!.IsEnabled = true; }
        });
        get.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(get, Dock.Right); row.Children.Add(get); row.Children.Add(address); link.Children.Add(row);
        var linkCard = Pastel.Card(link); linkCard.Padding = new Thickness(14, 9, 14, 12); Children.Add(linkCard);
        Children.Add(list);
        var where = new StackPanel(); where.Children.Add(Pastel.Text("모드가 더한 것은 여기에 나타나요", 14));
        where.Children.Add(Pastel.Text("· 테마 → 설정 → 화면·소리의 테마 목록 (설정 창을 다시 열면 보여요)\n· 대사 팩 → 홈 → 상점, 대사집의 '대사 팩'\n· 상황 프로필 → 우클릭 → 상황 프로필, 빠른 설정\n· 페이지 모드 → 캐릭터 페이지 위쪽의 모드 선택\n\n모드 파일 형식은 사용 설명서의 '추가 모드 만들기'에 있어요. 모르는 사람이 준 모드는 '내용 보기'로 한 번 읽어 보세요 — 페이지 모드의 지시문과 대사는 그대로 AI에 전달됩니다.", 12));
        Children.Add(Pastel.Card(where, true)); Refresh();
    }
    void Changed(string message) { buddy.ModsChanged(); Refresh(); say(message); }
    void Refresh()
    {
        list.Children.Clear(); var store = ModStore.Current;
        if (store.Mods.Count == 0) list.Children.Add(Pastel.Text("아직 추가한 모드가 없어요. '예시 모드 파일 만들기'로 형식을 살펴볼 수 있어요.", 13));
        foreach (var mod in store.Mods)
        {
            var body = new StackPanel(); var head = new DockPanel(); var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); head.Children.Add(actions);
            void Small(string text, Action action) { var button = Pastel.Button(text, action, true); button.Padding = new Thickness(9, 3, 9, 3); button.Margin = new Thickness(5, 0, 0, 0); button.FontSize = 12; actions.Children.Add(button); }
            // A mod that came from GitHub can be fetched again from the same address.
            if (mod.File.Source.Length > 0) Small("새로 받기", async () =>
            {
                say("'" + mod.File.Name + "'을(를) 다시 받는 중…");
                try { var fresh = await ModDownloads.Update(store, mod, System.Threading.CancellationToken.None); Changed("'" + fresh.Name + "'을(를) 새로 받았어요" + (fresh.Version.Length > 0 ? " (v" + fresh.Version + ")" : "") + "."); }
                catch (Exception e) { say("받지 못했습니다. " + e.Message); }
            });
            Small("내용 보기", () => View(mod));
            Small("삭제", () =>
            {
                if (MessageBox.Show(owner, "'" + mod.File.Name + "' 모드를 삭제할까요?\n이 모드의 테마를 쓰고 있었다면 다음에 켤 때 기본 테마로 돌아갑니다.", "모드 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                try { store.Remove(mod.File.Id); Changed("'" + mod.File.Name + "' 모드를 삭제했어요."); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { say("삭제하지 못했습니다. " + e.Message); }
            });
            var use = new CheckBox { Content = mod.File.Name + (mod.File.Version.Length > 0 ? "  v" + mod.File.Version : ""), IsChecked = mod.Enabled, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            use.Click += (_, _) => { try { store.Enable(mod.File.Id, use.IsChecked == true); Changed("'" + mod.File.Name + "' " + (use.IsChecked == true ? "켬" : "끔")); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { say(e.Message); } };
            head.Children.Add(use); body.Children.Add(head);
            body.Children.Add(new TextBlock { Text = mod.File.Summary + (mod.File.Author.Length > 0 ? "  ·  만든 사람 " + mod.File.Author : "") + (mod.File.Source.Length > 0 ? "  ·  GitHub에서 설치" : ""), Foreground = Pastel.Hot, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
            if (mod.File.Description.Length > 0) body.Children.Add(new TextBlock { Text = mod.File.Description, Foreground = Pastel.Ink, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            var card = Pastel.Card(body, !mod.Enabled); card.Padding = new Thickness(13, 9, 12, 10); card.Margin = new Thickness(0, 6, 4, 2); list.Children.Add(card);
        }
        foreach (string problem in store.Problems) list.Children.Add(Pastel.Text("읽지 못한 파일 — " + problem, 12));
    }
    void View(InstalledMod mod)
    {
        var dialog = new Window { Owner = owner, ShowInTaskbar = false }; var body = new DockPanel { Margin = new Thickness(18) };
        var close = Pastel.Button("닫기", dialog.Close, true); DockPanel.SetDock(close, Dock.Bottom); body.Children.Add(close);
        var note = Pastel.Text(Path.GetFileName(mod.Path) + " · " + mod.File.Summary, 12); DockPanel.SetDock(note, Dock.Top); body.Children.Add(note);
        body.Children.Add(new TextBox { Text = ModStore.Text(mod), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas, Malgun Gothic"), FontSize = 12.5 });
        Pastel.Window(dialog, mod.File.Name, body, 680, 720); dialog.ShowDialog();
    }
}
