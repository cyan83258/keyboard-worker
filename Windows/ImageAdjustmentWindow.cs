using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

public sealed class ImageAdjustmentWindow : Window
{
    public ImageAdjustmentWindow(string title, BitmapSource bitmap, BitmapSource? idle, ImageAdjustment initial, Action<ImageAdjustment> save)
    {
        var p = new StackPanel { Margin = new Thickness(20) };
        p.Children.Add(Pastel.Text(title + " · 크기와 위치", 20));
        p.Children.Add(Pastel.Text("크기는 원본 비율을 유지합니다. 가로·세로 위치는 표시 영역의 %입니다. 아래 미리보기에서 바로 확인해 주세요.", 12));
        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform };
        var canvas = new Grid { Width = 280, Height = 210, ClipToBounds = true };
        if (idle != null) { var ghost = new Image { Source = idle, Stretch = Stretch.Uniform, Opacity = .22 }; PetImageLayout.Apply(ghost, 280, 210, idle); canvas.Children.Add(ghost); }
        canvas.Children.Add(image); p.Children.Add(new Border { Child = canvas, Background = Pastel.Checker(), Margin = new Thickness(0, 10, 0, 10), CornerRadius = new CornerRadius(8) });
        Slider Field(string caption, double value, double min, double max)
        {
            var label = Pastel.Text(caption + ": " + value.ToString("0") + "%"); p.Children.Add(label);
            var slider = new Slider { Value = value, Minimum = min, Maximum = max, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 5, 0, 10) }; p.Children.Add(slider);
            slider.ValueChanged += (_, _) => label.Text = caption + ": " + slider.Value.ToString("0") + "%"; return slider;
        }
        initial = initial.Normalize(); var scale = Field("크기", initial.Scale, 25, 200); var x = Field("가로 위치", initial.X, -100, 100); var y = Field("세로 위치", initial.Y, -100, 100);
        ImageAdjustment Draft() => new() { Scale = scale.Value, X = x.Value, Y = y.Value };
        void Update() => PetImageLayout.Apply(image, 280, 210, idle, Draft());
        scale.ValueChanged += (_, _) => Update(); x.ValueChanged += (_, _) => Update(); y.ValueChanged += (_, _) => Update(); Update();
        p.Children.Add(Pastel.Text("연하게 보이는 대기 이미지에 크기와 위치를 맞춰 보세요. 보정은 캐릭터와 장착한 장비에 함께 적용됩니다. 원본 파일은 수정하지 않습니다.", 12));
        var footer = new StackPanel { Margin = new Thickness(20, 0, 20, 12) };
        footer.Children.Add(Pastel.Button("기본값", () => { scale.Value = 100; x.Value = y.Value = 0; }, true));
        footer.Children.Add(Pastel.Button("보정 적용", () => { save(Draft()); Close(); }));
        var root = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(Pastel.Scroll(p));
        Pastel.Window(this, "이미지 보정", root, 480, 720);
    }
}
