using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class RestWindow : Window
{
    readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public RestWindow(Buddy buddy)
    {
        var p = new StackPanel { Margin = new Thickness(28) }; p.Children.Add(Pastel.Text("잠깐 쉬어가도 괜찮아 ♡", 22));
        p.Children.Add(Pastel.Text("휴식 중에는 재촉을 멈춥니다. 손 반응과 타이핑 소리는 유지됩니다."));
        var remaining = Pastel.Text("", 18); p.Children.Add(Pastel.Card(remaining, true));
        p.Children.Add(Pastel.Text("휴식 시간 (1~1440분)")); var minutes = new TextBox { Text = buddy.Pref.Desktop.QuickRestMinutes.ToString() }; p.Children.Add(minutes);
        var status = Pastel.Text(""); p.Children.Add(status);
        p.Children.Add(Pastel.Button("휴식 시작 / 시간 변경", () => { if (!int.TryParse(minutes.Text, out var value) || value < 1 || value > 1440) { status.Text = "1~1440분 사이로 입력해 주세요."; return; } buddy.SetRest(value); Update(); }));
        p.Children.Add(Pastel.Button("휴식 중단", () => { buddy.StopRest(); Update(); }, true));
        p.Children.Add(Pastel.Button("닫기", Close)); Pastel.Window(this, "휴식 설정", Pastel.Scroll(p), 480, 590);
        timer.Tick += (_, _) => Update(); timer.Start(); Closed += (_, _) => timer.Stop(); Update();
        void Update() { long seconds = buddy.RestSeconds; remaining.Text = seconds > 0 ? $"쉬는 중 · {seconds / 60:00}:{seconds % 60:00} 남았어요" : "함께 작업하는 중 ☆"; }
    }
}
