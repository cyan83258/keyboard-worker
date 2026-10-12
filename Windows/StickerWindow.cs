using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace KeyboardWorker;

// The user's own emoticons for text messages: add pictures, name them (the name is all the AI knows of a picture),
// put them in order and take them away. Changes are saved as they are made.
public sealed class StickerWindow : Window
{
    readonly Buddy buddy; readonly StackPanel list = new(); readonly TextBlock status = Pastel.Text("", 12), count = Pastel.Text("", 12);
    public StickerWindow(Buddy buddy)
    {
        this.buddy = buddy; var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(Pastel.Text("내 이모티콘", 20));
        top.Children.Add(Pastel.Text("문자 메시지에서 보내는 그림 이모티콘이에요. AI는 그림을 볼 수 없고 제목으로 뜻을 알아들으니, '엄지척', '울먹이는 고양이'처럼 무슨 그림인지 제목에 적어 주세요. 파일 이름이 처음 제목이 됩니다. 그림은 작게 줄여 저장돼요.", 12));
        var buttons = new WrapPanel(); buttons.Children.Add(Pastel.Button("이모티콘 추가… (여러 장 선택 가능)", async () => await Add())); count.VerticalAlignment = VerticalAlignment.Center; count.Margin = new Thickness(10, 0, 0, 0); buttons.Children.Add(count); top.Children.Add(buttons);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(status); footer.Children.Add(Pastel.Button("닫기", Close, true));
        root.Children.Add(Pastel.Scroll(list));
        Pastel.Window(this, "내 이모티콘", root, 560, 720); MinWidth = 460; MinHeight = 420; Refresh();
    }
    async Task Add()
    {
        var dialog = new OpenFileDialog { Title = "이모티콘 선택", Filter = "이미지 (PNG / JPG)|*.png;*.jpg;*.jpeg", Multiselect = true, CheckFileExists = true }; if (dialog.ShowDialog(this) != true) return;
        int added = 0; string problem = "", root = Storage.Root; IsEnabled = false; status.Text = "이모티콘을 넣는 중…";
        try
        {
            foreach (string file in dialog.FileNames)
            {
                try { string stored = await Task.Run(() => StageAssets.ImportSticker(file, root)); buddy.Stage.AddSticker(StageTags.BackdropTitle(file), stored); added++; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or InvalidDataException or FileFormatException) { problem = Path.GetFileName(file) + ": " + e.Message; if (e is InvalidOperationException) break; }
            }
        }
        finally { IsEnabled = true; }
        status.Text = added + "개를 넣었어요." + (problem.Length > 0 ? " 넣지 못한 것 — " + problem : ""); Refresh();
    }
    void Move(StageSticker sticker, int step)
    {
        var all = buddy.Stage.State.Stickers; int at = all.IndexOf(sticker), to = at + step; if (at < 0 || to < 0 || to >= all.Count) return;
        all.RemoveAt(at); all.Insert(to, sticker); buddy.Stage.Save(); Refresh();
    }
    void Refresh()
    {
        var all = buddy.Stage.State.Stickers; list.Children.Clear(); count.Text = all.Count + " / " + StageBook.MaxStickers + "개";
        if (all.Count == 0) { list.Children.Add(Pastel.Text("아직 넣은 이모티콘이 없어요.", 13)); return; }
        foreach (var sticker in all.ToArray())
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 4, 6) };
            var picture = StageParts.Thumbnail(sticker.Path, true, 64, 64); picture.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(picture, Dock.Left); row.Children.Add(picture);
            var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(acts, Dock.Right); row.Children.Add(acts);
            var up = StageParts.Small("▲", () => Move(sticker, -1)); up.ToolTip = "앞으로"; up.IsEnabled = all.IndexOf(sticker) > 0; var down = StageParts.Small("▼", () => Move(sticker, 1)); down.ToolTip = "뒤로"; down.IsEnabled = all.IndexOf(sticker) < all.Count - 1;
            acts.Children.Add(up); acts.Children.Add(down); acts.Children.Add(StageParts.Small("삭제", () => { all.Remove(sticker); buddy.Stage.Save(); status.Text = "'" + sticker.Title + "'을(를) 지웠어요."; Refresh(); }));
            var title = StageParts.Field(sticker.Title, "제목 (AI가 이 이름으로 뜻을 알아들어요)"); title.VerticalAlignment = VerticalAlignment.Center; title.Margin = new Thickness(0, 0, 8, 0);
            title.LostFocus += (_, _) => { string cleaned = StageTags.Clean(title.Text, "이모티콘"); if (cleaned != sticker.Title) { sticker.Title = cleaned; buddy.Stage.Save(); status.Text = "제목을 '" + cleaned + "'(으)로 바꿨어요."; } title.Text = sticker.Title; };
            row.Children.Add(title); list.Children.Add(row);
        }
    }
}
