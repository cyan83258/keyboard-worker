using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KeyboardWorker;

// "지금 상태" card shared by the automatic-line and pair-interaction settings: the latest result, when the next line is due
// (or why the pets are resting), and a button that tries one line right now.
public static class AutomaticStatusCard
{
    public static UIElement Build(Window host, Buddy buddy, string action, bool together)
    {
        var live = Pastel.Text("", 12); var card = new StackPanel(); card.Children.Add(Pastel.Text("지금 상태", 15)); card.Children.Add(live);
        card.Children.Add(Pastel.Button(action, () => live.Text = buddy.TestAutomatic(together), true));
        card.Children.Add(Pastel.Text("저장된 설정으로 AI에 한 번 요청합니다 (사용량 1회). 실패하면 이유가 위에 표시돼요.", 12));
        void Show() => live.Text = "최근 결과: " + buddy.AutomaticStatus + "\n" + buddy.AutomaticOutlook;
        var watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; watch.Tick += (_, _) => Show(); Show(); watch.Start(); host.Closed += (_, _) => watch.Stop();
        return Pastel.Card(card, true);
    }
}
