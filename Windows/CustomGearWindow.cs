using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace KeyboardWorker;

public sealed class CustomGearWindow : Window
{
    readonly PetGame game;
    readonly CustomGear original;
    readonly TextBox name = new() { MaxLength = 80 }, width = new(), x = new(), y = new(), angle = new();
    readonly ComboBox slot = new() { ItemsSource = new[] { "머리", "얼굴", "목", "몸 / 전체 의상", "오라 / 전체 장식" } };
    readonly CheckBox behind = new() { Content = "캐릭터 뒤에 표시" }, equip = new() { Content = "저장 후 바로 장착" };
    readonly Image preview = new() { Width = 140, Height = 110, Stretch = Stretch.Uniform };
    readonly TextBlock file = Pastel.Text("이미지를 선택해 주세요."), status = Pastel.Text("");
    string selectedPath;
    bool saving, closed;
    bool explicitlySelected;
    public CustomGearWindow(PetGame game, CustomGear? item = null)
    {
        this.game = game; original = item ?? new(); selectedPath = original.ImagePath;
        var p = new StackPanel { Margin = new Thickness(24) };
        p.Children.Add(Pastel.Text(item == null ? "나만의 장비 만들기 ♡" : "커스텀 장비 수정", 23));
        p.Children.Add(Pastel.Text("등록하면 코인 비용 없이 옷장에 추가합니다. 원본 파일은 그대로 두고 프로그램 전용 폴더로 복사합니다.", 13));
        p.Children.Add(Pastel.Text("장비 이름")); name.Text = item?.Name ?? ""; p.Children.Add(name);
        p.Children.Add(Pastel.Text("장착 위치")); slot.SelectedIndex = Math.Max(0, Array.IndexOf(CustomGear.Slots, original.Slot)); p.Children.Add(slot);
        p.Children.Add(new Border { Child = preview, Background = Pastel.Checker(), CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Margin = new Thickness(0, 8, 0, 8) });
        p.Children.Add(file); p.Children.Add(Pastel.Button("PNG / JPG 이미지 선택…", async () => await Choose(), true));
        Field("너비 (캐릭터 이미지 너비의 5~200%)", width, original.WidthPercent);
        Field("가로 위치 보정 (-100~100%)", x, original.OffsetX);
        Field("세로 위치 보정 (-100~100%)", y, original.OffsetY);
        Field("회전 각도 (-180~180도)", angle, original.Rotation);
        behind.IsChecked = original.Behind; equip.IsChecked = item == null || game.State.Equipped.Values.Contains(item.Id); p.Children.Add(behind); p.Children.Add(equip);
        p.Children.Add(Pastel.Card(Pastel.Text("권장: 배경이 투명한 256×256 또는 512×512 PNG\nJPG/JPEG도 지원하지만 사각형 배경이 함께 표시됩니다. 장당 20MB 이하, 가로·세로 각각 4096px 이하입니다.\n장착 위치를 기준으로 그림의 중심을 놓습니다. 머리·얼굴·목 기준점은 꾸미기·설정에서 조절하며, 몸·오라는 캐릭터 중앙이 기준입니다. 전체 의상은 너비를 100%로 설정해 보세요.", 12), true));
        var footer = new StackPanel { Margin = new Thickness(20, 0, 20, 16) }; footer.Children.Add(status); footer.Children.Add(Pastel.Button("장비 저장", async () => await Save()));
        // For whoever ships accessories with the program: the placement worked out here, as a line of accessories.json.
        var copy = Pastel.Button("배치 값 복사", () =>
        {
            try { Clipboard.SetText(ShopGear.Describe(new CustomGear { Name = name.Text.Trim(), Slot = CustomGear.Slots[slot.SelectedIndex], WidthPercent = Number(width, 35), OffsetX = Number(x, 0), OffsetY = Number(y, 0), Rotation = Number(angle, 0), Behind = behind.IsChecked == true }, selectedPath.Length > 0 ? Path.GetFileName(selectedPath) : "파일이름.png")); status.Text = "복사했어요. 상점용 악세서리 목록(accessories.json)에 붙여 넣을 수 있어요."; }
            catch (System.Runtime.InteropServices.ExternalException) { status.Text = "지금은 복사할 수 없어요. 다시 눌러 주세요."; }
        }, true); copy.ToolTip = "지금 적힌 위치·크기를 프로그램에 함께 넣는 악세서리 목록(accessories.json)의 한 줄로 복사해요"; footer.Children.Add(copy);
        static double Number(TextBox box, double fallback) => double.TryParse(box.Text, out double value) && double.IsFinite(value) ? value : fallback;
        var root = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(Pastel.Scroll(p));
        Pastel.Window(this, "커스텀 장비", root, 660, 780); Closed += (_, _) => closed = true;
        Loaded += async (_, _) => { if (selectedPath.Length > 0) await PreviewFile(selectedPath); };
        void Field(string title, TextBox box, double value) { p.Children.Add(Pastel.Text(title)); box.Text = value.ToString("0.##"); p.Children.Add(box); }
    }
    async Task Choose()
    {
        var dialog = new OpenFileDialog { Title = "액세서리 이미지 선택", Filter = "PNG / JPG 이미지|*.png;*.jpg;*.jpeg", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) { await PreviewFile(dialog.FileName); if (selectedPath == dialog.FileName) explicitlySelected = true; }
    }
    async Task PreviewFile(string path)
    {
        try
        {
            var data = await Task.Run(() => (Size: ImageAssets.Validate(path), Image: ImageAssets.Read(path, 200)));
            if (closed) return; selectedPath = path; preview.Source = data.Image; file.Text = $"{Path.GetFileName(path)} · {data.Size.Width}×{data.Size.Height}px"; status.Text = "";
        }
        catch (Exception e) { status.Text = "이미지를 읽지 못했습니다. " + e.Message; }
    }
    async Task Save()
    {
        if (saving) return;
        try
        {
            if (string.IsNullOrWhiteSpace(name.Text) || selectedPath.Length == 0) throw new InvalidOperationException("이름과 이미지를 선택해 주세요.");
            double Value(TextBox box, double min, double max) { if (!double.TryParse(box.Text, out var v) || !double.IsFinite(v) || v < min || v > max) throw new InvalidOperationException("숫자를 표시된 범위 안에 입력해 주세요."); return v; }
            var draft = original with
            {
                Name = name.Text.Trim(),
                Slot = CustomGear.Slots[slot.SelectedIndex],
                WidthPercent = Value(width, 5, 200),
                OffsetX = Value(x, -100, 100),
                OffsetY = Value(y, -100, 100),
                Rotation = Value(angle, -180, 180),
                Behind = behind.IsChecked == true
            };
            bool wear = equip.IsChecked == true; string source = selectedPath; saving = true; IsEnabled = false; status.Text = "이미지를 저장하는 중…";
            string imported = await Task.Run(() => { ImageAssets.Validate(source); return !explicitlySelected && source == original.ImagePath ? source : ImageAssets.Import(source, Storage.Root); });
            if (closed) return; game.RegisterCustom(draft with { ImagePath = imported }, wear); Close();
        }
        catch (Exception e) { status.Text = e.Message; }
        finally { saving = false; if (!closed) IsEnabled = true; }
    }
}
