using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace KeyboardWorker;

// The font and size of the pets' lines: one choice for the speech bubbles, one for the character pages.
public sealed class FontWindow : Window
{
    // One of the two choices: a font file (or the standard font), a size, and a preview.
    sealed class Choice
    {
        public string Path; public readonly TextBox Size; readonly TextBlock name = Pastel.Text("", 13), sample = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        public Choice(Window owner, Panel parent, string title, string path, int size, Action<string> say)
        {
            Path = path; Size = new TextBox { Text = size.ToString(), Width = 70, MaxLength = 2 }; sample.Foreground = Pastel.Ink; sample.Text = "오늘도 옆에 있을게. 천천히 해도 괜찮아. Aa 123 ♡";
            var body = new StackPanel(); body.Children.Add(Pastel.Text(title, 16)); body.Children.Add(name);
            var buttons = new WrapPanel();
            buttons.Children.Add(Pastel.Button("글꼴 파일 선택… (TTF / OTF)", () =>
            {
                var dialog = new OpenFileDialog { Title = "글꼴 파일 선택", Filter = "글꼴 (TTF / OTF)|*.ttf;*.otf", CheckFileExists = true }; if (dialog.ShowDialog(owner) != true) return;
                try { Path = FontAssets.Import(dialog.FileName, Storage.Root); say("'저장'을 눌러야 적용돼요."); } catch (Exception e) { say(e.Message); }
                Show();
            }, true));
            buttons.Children.Add(Pastel.Button("기본 글꼴로", () => { Path = ""; Show(); }, true)); body.Children.Add(buttons);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(new TextBlock { Text = "글자 크기 ", Foreground = Pastel.Ink, VerticalAlignment = VerticalAlignment.Center }); row.Children.Add(Size);
            row.Children.Add(new TextBlock { Text = $"  ({FontAssets.MinSize}~{FontAssets.MaxSize})", Foreground = Pastel.Ink, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }); body.Children.Add(row);
            body.Children.Add(sample); parent.Children.Add(Pastel.Card(body)); Size.TextChanged += (_, _) => Show(); Show();
        }
        public int Points => int.TryParse(Size.Text, out int size) && size >= FontAssets.MinSize && size <= FontAssets.MaxSize ? size : throw new InvalidOperationException($"글자 크기는 {FontAssets.MinSize}~{FontAssets.MaxSize} 사이의 숫자로 적어 주세요.");
        void Show() { name.Text = "지금 글꼴: " + FontAssets.Name(Path); sample.FontFamily = FontAssets.Get(Path); if (int.TryParse(Size.Text, out int size) && size >= FontAssets.MinSize && size <= FontAssets.MaxSize) sample.FontSize = size; }
    }
    public FontWindow(Buddy buddy)
    {
        var p = new StackPanel { Margin = new Thickness(22) }; var status = Pastel.Text("", 12);
        p.Children.Add(Pastel.Text("대사 글꼴", 22));
        p.Children.Add(Pastel.Text("말풍선과 캐릭터 페이지의 대사에 쓸 글꼴을 따로 정할 수 있어요. 글꼴 파일은 설정 폴더에 복사해 두고, 한글이 들어 있지 않은 글꼴이면 한글은 기본 글꼴로 보입니다.", 12));
        var bubble = new Choice(this, p, "말풍선 대사", buddy.Pref.BubbleFontPath, buddy.Pref.Desktop.BubbleFont, text => status.Text = text);
        var stage = new Choice(this, p, "캐릭터 페이지 대사", buddy.Pref.StageFontPath, buddy.Pref.StageFontSize, text => status.Text = text);
        p.Children.Add(status); var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("저장", async () =>
        {
            try
            {
                var next = buddy.Pref.Snapshot(); next.BubbleFontPath = bubble.Path; next.Desktop.BubbleFont = bubble.Points; next.StageFontPath = stage.Path; next.StageFontSize = stage.Points;
                buddy.Apply(next, configureKeys: false, persist: false); await Storage.SaveAsync(buddy.Pref);
                // Copies of fonts that are no longer chosen are removed.
                string root = Storage.Root; string[] used = { next.BubbleFontPath, next.StageFontPath }; await Task.Run(() => FontAssets.Prune(root, used, DateTime.Now.AddHours(2)));
                status.Text = "저장했어요 ♡ " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception e) { status.Text = e.Message; }
        }));
        buttons.Children.Add(Pastel.Button("닫기", Close, true)); p.Children.Add(buttons);
        Pastel.Window(this, "대사 글꼴", Pastel.Scroll(p), 600, 720);
    }
}
