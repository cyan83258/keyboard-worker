using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class TimerWindow : Window
{
    readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    public TimerWindow(Buddy buddy)
    {
        var p = new StackPanel { Margin = new Thickness(24) }; var work = Pastel.Text("", 21); var focus = Pastel.Text("", 21);
        p.Children.Add(Pastel.Text("키보드 작업 시간", 24)); p.Children.Add(Pastel.Card(work, true));
        p.Children.Add(Pastel.Text("측정은 두 키 입력 사이의 짧은 구간만 누적합니다. 작업 전체 시간과는 다를 수 있어요."));
        var actions = new WrapPanel(); actions.Children.Add(Pastel.Button("측정 일시중지 / 재개", () => { buddy.Work.Paused = !buddy.Work.Paused; buddy.Work.Suspend(); Update(); }));
        actions.Children.Add(Pastel.Button("이번 실행 기록 초기화", () => { buddy.Work.ResetSession(); Update(); }, true));
        actions.Children.Add(Pastel.Button("오늘 기록 초기화", () => { if (MessageBox.Show(this, "오늘 측정한 시간을 초기화할까요?", "작업 시간", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { buddy.Work.ResetToday(); WorkTimeStore.Save(buddy.Work); Update(); } }, true));
        p.Children.Add(actions);
        p.Children.Add(Pastel.Text("집중 · 휴식 타이머", 24)); p.Children.Add(Pastel.Card(focus));
        p.Children.Add(Pastel.Text("집중 타이머는 입력 여부와 관계없이 정해진 시간을 셉니다. 휴식 단계에서는 재촉과 작업 시간 측정을 멈춥니다."));
        var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("집중 시작", () => { var d = buddy.Pref.Desktop; buddy.FocusTimer.Start(Environment.TickCount64, d.FocusMinutes, d.BreakMinutes, d.FocusRepeat); Update(); }));
        buttons.Children.Add(Pastel.Button("일시중지 / 재개", () => { buddy.FocusTimer.TogglePause(Environment.TickCount64); Update(); }, true));
        buttons.Children.Add(Pastel.Button("종료", () => { buddy.FocusTimer.Stop(); Update(); }, true));
        p.Children.Add(buttons);
        p.Children.Add(Pastel.Button("시간 · 측정 옵션 설정", buddy.OpenDesktopOptions, true));
        Pastel.Window(this, "작업 타이머", Pastel.Scroll(p), 640, 720);
        timer.Tick += (_, _) => Update(); timer.Start(); Closed += (_, _) => timer.Stop(); Update();
        void Update()
        {
            string activity = !buddy.Pref.Desktop.WorkEnabled || buddy.Work.Paused ? "측정 일시중지" : buddy.Work.IsActive(Environment.TickCount64, buddy.Pref.Desktop.WorkGapMs) ? "입력 중" : "다음 입력 대기";
            work.Text = $"오늘   {TypingWorkClock.Format(buddy.Work.TodayMs)}\n이번 실행   {TypingWorkClock.Format(buddy.Work.SessionMs)}\n{activity}";
            focus.Text = $"{buddy.FocusTimer.Phase}{(buddy.FocusTimer.Paused ? " · 일시중지" : "")}\n남은 시간   {TypingWorkClock.Format(buddy.FocusTimer.RemainingMs)}";
        }
    }
}
