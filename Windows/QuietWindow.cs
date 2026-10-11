using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KeyboardWorker;

public sealed class QuietWindow : Window
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public QuietWindow(Buddy buddy)
    {
        var p = new StackPanel { Margin = new Thickness(24) }; p.Children.Add(Pastel.Text("잠깐, 조용히 곁에 있을게 ♡", 22));
        p.Children.Add(Pastel.Text("타이핑 소리·몸 흔들림·자동 재촉·게임 알림·집중 타이머 알림을 잠시 멈춥니다. 손 반응·작업 시간·경험치·직접 보낸 채팅은 계속 동작합니다."));
        var state = Pastel.Text("", 18);
        p.Children.Add(Pastel.Card(state, true));
        p.Children.Add(Pastel.Text("조용히 있을 시간 (1~1440분)"));
        var minutes = new TextBox { Text = "30" };
        p.Children.Add(minutes);
        var unlimited = new CheckBox { Content = "직접 끌 때까지 유지", IsChecked = false }; p.Children.Add(unlimited); var status = Pastel.Text(""); p.Children.Add(status);
        p.Children.Add(Pastel.Button("조용한 모드 시작", () =>
        {
            if (unlimited.IsChecked == true) buddy.StartQuiet(0);
            else if (int.TryParse(minutes.Text, out var value) && value >= 1 && value <= 1440) buddy.StartQuiet(value);
            else status.Text = "1~1440분으로 입력해 주세요.";
            Update();
        }));
        p.Children.Add(Pastel.Button("종료하고 원래 설정으로", () => { buddy.StopQuiet(); Update(); }, true));
        p.Children.Add(Pastel.Text("기존 소리·움직임 설정은 변경하지 않습니다. 종료하거나 프로그램을 다시 실행하면 원래 설정으로 돌아갑니다. 기본 단축키: Ctrl+Alt+M", 12));
        Pastel.Window(this, "조용한 작업 모드", Pastel.Scroll(p), 560, 640); timer.Tick += (_, _) => Update(); timer.Start(); Closed += (_, _) => timer.Stop(); Update();
        void Update() { long remaining = buddy.QuietSeconds; state.Text = !buddy.IsQuiet ? "일반 작업 모드 ☆" : remaining < 0 ? "조용한 모드 · 직접 끌 때까지" : $"조용한 모드 · {remaining / 60:00}:{remaining % 60:00} 남았어요"; }
    }
}
