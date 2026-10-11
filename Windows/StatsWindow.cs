using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardWorker;

// Typing work time per day and where the time went today.
public sealed class StatsWindow : Window
{
    static readonly string[] weekdays = { "일", "월", "화", "수", "목", "금", "토" };
    public StatsWindow(Buddy buddy)
    {
        var log = buddy.Usage; var today = log.Day(WorkTimeStore.Today); var p = new StackPanel { Margin = new Thickness(22) }; p.Children.Add(Pastel.Text("함께 보낸 시간", 23));
        // A labelled bar; width is the share of the largest value in its group.
        Grid Bar(string label, long value, long largest, Brush fill, string note)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            var name = new TextBlock { Text = label, Foreground = Pastel.Ink, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = label };
            row.Children.Add(name);
            var track = new Grid { Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            double share = largest <= 0 ? 0 : Math.Clamp((double)value / largest, 0, 1);
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
            var rail = new Border { Background = Brushes.White, BorderBrush = Pastel.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };
            Grid.SetColumnSpan(rail, 2);
            track.Children.Add(rail);
            if (share > 0) track.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(7), Margin = new Thickness(1) });
            Grid.SetColumn(track, 1); row.Children.Add(track);
            var text = new TextBlock { Text = note, Foreground = Pastel.Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(text, 2);
            row.Children.Add(text);
            return row;
        }
        Brush Tone(string kind) => kind == "work" ? Pastel.Hot : kind == "game" ? Pastel.Original("#FFB14A") : Pastel.Blue;

        var now = new StackPanel(); now.Children.Add(Pastel.Text("오늘", 16));
        now.Children.Add(Pastel.Text($"타이핑 작업 시간 {UsageLog.Span(buddy.Work.TodayMs)}\n업무·창작 프로그램 {UsageLog.Span(today.KindMs("work"))} · 게임 {UsageLog.Span(today.KindMs("game"))} · 그 밖의 프로그램 {UsageLog.Span(today.KindMs("other"))}", 13));
        p.Children.Add(Pastel.Card(now, true));

        var week = new StackPanel(); week.Children.Add(Pastel.Text("최근 7일 · 타이핑 작업 시간", 16));
        var days = Enumerable.Range(0, 7).Select(back => DateTime.Now.Date.AddDays(back - 6)).Select(date => (Date: date, Log: log.Days.FirstOrDefault(d => d.Date == date.ToString("yyyy-MM-dd")))).ToArray();
        long longest = Math.Max(1, days.Max(d => d.Date == DateTime.Now.Date ? buddy.Work.TodayMs : d.Log?.WorkMs ?? 0));
        foreach (var day in days)
        {
            long worked = day.Date == DateTime.Now.Date ? buddy.Work.TodayMs : day.Log?.WorkMs ?? 0;
            week.Children.Add(Bar($"{day.Date.Month}/{day.Date.Day} ({weekdays[(int)day.Date.DayOfWeek]})", worked, longest, Pastel.Hot, day.Log == null && worked == 0 ? "기록 없음" : $"{UsageLog.Span(worked)} · 업무 {UsageLog.Span(day.Log?.KindMs("work") ?? 0)} · 게임 {UsageLog.Span(day.Log?.KindMs("game") ?? 0)}"));
        }
        p.Children.Add(Pastel.Card(week));

        var programs = new StackPanel(); programs.Children.Add(Pastel.Text("오늘 많이 쓴 프로그램", 16));
        var top = today.Apps.Where(a => a.Value.Ms >= 60000).OrderByDescending(a => a.Value.Ms).Take(10).ToArray();
        if (top.Length == 0) programs.Children.Add(Pastel.Text("아직 1분 넘게 쓴 프로그램이 없어요.", 13));
        foreach (var item in top) programs.Children.Add(Bar(item.Key, item.Value.Ms, top[0].Value.Ms, Tone(item.Value.Kind), UsageLog.Span(item.Value.Ms) + " · " + AppKinds.Label(item.Value.Kind)));
        p.Children.Add(Pastel.Card(programs));
        p.Children.Add(Pastel.Text("프로그램 시간은 그 프로그램이 맨 앞에 있고 최근 10분 안에 키보드·마우스 입력(또는 재생 중인 영상·음악)이 있었던 시간입니다. 타이핑 작업 시간은 실제 키 입력 사이의 시간만 셉니다. 기록은 이 PC에만 저장되고 90일이 지나면 지워져요. 이 기능을 넣은 날부터 쌓이기 시작합니다.", 12));
        p.Children.Add(Pastel.Button("닫기", Close)); Pastel.Window(this, "작업 통계", Pastel.Scroll(p), 760, 800);
    }
}
