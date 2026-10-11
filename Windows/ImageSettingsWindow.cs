using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using System.Threading.Tasks;

namespace KeyboardWorker;

public sealed class ImageSettingsWindow : Window
{
    readonly Buddy buddy;
    readonly Dictionary<string, string> paths;
    readonly Dictionary<string, ImageAdjustment> adjustments;
    readonly HashSet<string> selectedAgain = new();
    readonly Dictionary<string, Action> refresh = new();
    readonly CheckBox advanced;
    readonly CheckBox flip = new() { Content = "이 캐릭터의 이미지를 모두 좌우반전" };
    readonly List<Image> thumbnails = new();
    readonly TextBlock status = Pastel.Text("");
    bool saving, closed;
    public ImageSettingsWindow(Buddy buddy)
    {
        this.buddy = buddy; paths = new(buddy.Pref.ImagePaths); adjustments = new(buddy.Pref.ImageAdjustments);
        var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel();
        footer.Children.Add(status);
        footer.Children.Add(Pastel.Button("저장하고 닫기", async () => await Save()));
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var tabs = new TabControl { Background = Pastel.Paper, BorderBrush = Pastel.Pink }; root.Children.Add(tabs);
        var basic = new StackPanel { Margin = new Thickness(12) };
        basic.Children.Add(Pastel.Text("상황마다 이미지를 직접 골라 주세요 ♡", 20));
        flip.IsChecked = buddy.Pref.FlipImages; flip.Checked += (_, _) => ShowFlip(); flip.Unchecked += (_, _) => ShowFlip(); basic.Children.Add(flip);
        basic.Children.Add(Pastel.Text("기본·고급·음악 이미지와 장식이 함께 뒤집힙니다. 원본 파일은 바꾸지 않으며, 캐릭터 프리셋마다 따로 저장돼요.", 12));
        var basicSlots = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var slot in ImageAssets.Basic) basicSlots.Children.Add(Slot(slot.Key, slot.Label));
        basic.Children.Add(basicSlots);
        basic.Children.Add(Pastel.Button("기본 이미지 모두 해제", () => { foreach (var slot in ImageAssets.Basic) { paths.Remove(slot.Key); refresh[slot.Key](); } }, true));
        basic.Children.Add(Guide()); tabs.Items.Add(new TabItem { Header = "기본 이미지", Content = Pastel.Scroll(basic), Padding = new Thickness(14, 8, 14, 8) });
        var extras = new StackPanel { Margin = new Thickness(12) };
        advanced = new CheckBox { Content = "고급 이미지 반응 사용", IsChecked = buddy.Pref.AdvancedImages }; extras.Children.Add(advanced);
        extras.Children.Add(Pastel.Text("쓰다듬기와 대화 상황에 맞춰 이미지를 바꿉니다.\n대화 표정은 기존 AI 응답과 함께 받아 추가 API 요청 없이 반영해요. 표정 판단은 대화 맥락에 따라 달라질 수 있습니다."));
        var slots = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 }; foreach (var slot in ImageAssets.Advanced) slots.Children.Add(Slot(slot.Key, slot.Label));
        extras.Children.Add(slots);
        extras.Children.Add(Pastel.Button("고급 이미지 모두 해제", () => { foreach (var slot in ImageAssets.Advanced) { paths.Remove(slot.Key); refresh[slot.Key](); } }, true)); extras.Children.Add(Guide());
        advanced.Checked += (_, _) => slots.Visibility = Visibility.Visible; advanced.Unchecked += (_, _) => slots.Visibility = Visibility.Collapsed;
        slots.Visibility = advanced.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        tabs.Items.Add(new TabItem { Header = "고급 이미지", Content = Pastel.Scroll(extras), Padding = new Thickness(14, 8, 14, 8) });
        var music = new StackPanel { Margin = new Thickness(12) };
        music.Children.Add(Pastel.Text("음악 재생 중의 친구 모습 ♫", 21));
        music.Children.Add(Pastel.Text("음악 감지와 음악 전용 이미지를 켜면 자동으로 전환됩니다. 등록하지 않은 상황은 일반 이미지를 사용합니다.", 12));
        var musicSlots = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var slot in ImageAssets.Music) musicSlots.Children.Add(Slot(slot.Key, slot.Label));
        music.Children.Add(musicSlots);
        music.Children.Add(Guide());
        tabs.Items.Add(new TabItem { Header = "음악 듣기", Content = Pastel.Scroll(music), Padding = new Thickness(14, 8, 14, 8) });
        ShowFlip(); Pastel.Window(this, "캐릭터 이미지", root, 800, 780); MinWidth = 700;
        Closed += (_, _) => closed = true;
    }
    void ShowFlip() { foreach (var thumbnail in thumbnails) thumbnail.RenderTransform = new ScaleTransform(flip.IsChecked == true ? -1 : 1, 1); }
    static UIElement Guide() => Pastel.Card(Pastel.Text("이미지 크기 안내\n권장: 512 × 512px (가로형 캐릭터는 512 × 384px도 가능)\n지원: PNG / JPG / JPEG · 장당 20MB 이하 · 가로·세로 각각 4096px 이하\n대기 이미지와 같은 표시 영역 안에 모든 표정을 비율을 유지해 넣습니다. 가로로 긴 타이핑 이미지도 표시 영역이 커지지 않습니다.\n캐릭터 자체 크기와 위치까지 맞추려면 같은 캔버스 크기·여백·캐릭터 위치로 준비해 주세요.\n배경을 투명하게 하려면 PNG를 사용하세요. JPG는 배경도 함께 표시됩니다.\n등록하지 않은 상황은 기본 이미지 또는 내장 캐릭터로 표시합니다.", 12), true);
    UIElement Slot(string key, string title)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var thumbnail = new Image { Width = 64, Height = 64, Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(.5, .5) }; thumbnails.Add(thumbnail);
        var preview = new Border { Background = Pastel.Checker(), Child = thumbnail, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 10, 0) }; grid.Children.Add(preview);
        var info = new StackPanel();
        info.Children.Add(Pastel.Text(title, 15));
        var label = Pastel.Text("", 12);
        label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        info.Children.Add(label);
        var buttons = new WrapPanel(); buttons.Children.Add(Pastel.Button("선택…", () =>
        {
            var dialog = new OpenFileDialog { Title = title + " 이미지 선택", Filter = "PNG / JPG 이미지|*.png;*.jpg;*.jpeg", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            try { ImageAssets.Validate(dialog.FileName); paths[key] = dialog.FileName; selectedAgain.Add(key); refresh[key](); status.Text = "새 이미지 선택 완료 · 저장하면 교체됩니다."; }
            catch (Exception e) { status.Text = e.Message; }
        })); buttons.Children.Add(Pastel.Button("해제", () => { paths.Remove(key); refresh[key](); }, true)); info.Children.Add(buttons);
        buttons.Children.Add(Pastel.Button("크기·위치 보정", () =>
        {
            if (!paths.TryGetValue(key, out var selected)) { status.Text = "먼저 이 상황의 이미지를 선택해 주세요."; return; }
            try
            {
                var bitmap = ImageAssets.Read(selected, 500);
                var idleImage = paths.TryGetValue("idle", out var idlePath) ? ImageAssets.Read(idlePath, 500) : null;
                var editor = new ImageAdjustmentWindow(title, bitmap, idleImage, adjustments.GetValueOrDefault(key) ?? new(), value => adjustments[key] = value) { Owner = this, ShowInTaskbar = false };
                editor.ShowDialog(); status.Text = "보정 값을 설정했어요. 저장하고 닫기를 눌러 적용해 주세요.";
            }
            catch (Exception e) { status.Text = e.Message; }
        }, true));
        Grid.SetColumn(info, 1); grid.Children.Add(info);
        refresh[key] = () =>
        {
            thumbnail.Source = null;
            if (!paths.TryGetValue(key, out var path)) { label.Text = "미등록 · 기본 이미지 사용"; return; }
            try { var dimensions = ImageAssets.Validate(path); thumbnail.Source = ImageAssets.Read(path, 120); label.Text = $"{Path.GetFileName(path)}\n{dimensions.Width} × {dimensions.Height}px"; }
            catch { label.Text = "파일을 읽지 못했습니다. 다시 선택해 주세요."; }
        };
        refresh[key](); var card = Pastel.Card(grid); card.Padding = new Thickness(12); card.Margin = new Thickness(4); return card;
    }
    async Task Save()
    {
        if (saving) return; saving = true; IsEnabled = false; status.Text = "이미지를 저장하는 중…";
        try
        {
            var selected = new Dictionary<string, string>(paths);
            var previousPaths = new Dictionary<string, string>(buddy.Pref.ImagePaths);
            bool enabled = advanced.IsChecked == true, mirrored = flip.IsChecked == true;
            var imported = await ImageAssets.ImportSelectionAsync(selected, previousPaths, new HashSet<string>(selectedAgain), Storage.Root);
            if (closed) return;
            var next = buddy.Pref.Snapshot();
            next.ImagePaths = imported;
            next.ImageAdjustments = new(adjustments);
            next.SkinPath = "";
            next.AdvancedImages = enabled;
            next.FlipImages = mirrored;
            buddy.Apply(next, configureKeys: false, persist: false, reloadImages: true); await Storage.SaveAsync(buddy.Pref); await buddy.Presets.SaveAppearanceAsync(next); Close();
        }
        catch (Exception e) { status.Text = "이미지를 저장하지 못했습니다. " + e.Message; }
        finally { saving = false; if (!closed) IsEnabled = true; }
    }
    public void CaptureAdvanced(string path)
    {
        var root = (Border)Content; var layout = (Grid)root.Child; var dock = (DockPanel)layout.Children[1]; var tabs = dock.Children.OfType<TabControl>().Single();
        tabs.SelectedIndex = 1; advanced.IsChecked = true; UpdateLayout(); Pastel.Capture(this, path);
    }
}
