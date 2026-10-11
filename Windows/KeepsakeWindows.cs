using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace KeyboardWorker;

// Anniversaries and day counters: birthdays that come back every year, and "days since" dates.
public sealed class AnniversaryWindow : Window
{
    readonly Buddy buddy; readonly StackPanel list = new(); readonly TextBlock status = Pastel.Text("", 12);
    public AnniversaryWindow(Buddy buddy)
    {
        this.buddy = buddy; var p = new StackPanel { Margin = new Thickness(22) };
        p.Children.Add(Pastel.Text("기념일 · 디데이 ♡", 22));
        p.Children.Add(Pastel.Text("기념일 당일에는 축하 카드와 함께 캐릭터가 한마디 해요 (대사집의 '기념일' 대사). 날짜를 세는 기념일은 100일마다, 그리고 매년 같은 날에도 축하합니다.", 12));
        p.Children.Add(list);
        var form = new StackPanel(); form.Children.Add(Pastel.Text("새 기념일", 15));
        var name = new TextBox { MaxLength = 40 }; var date = new TextBox { Text = DateTime.Now.ToString("yyyy-MM-dd"), MaxLength = 10 };
        var yearly = new CheckBox { Content = "매년 돌아오는 날 (생일 등) · 끄면 그날부터 며칠째인지 셉니다", Margin = new Thickness(0, 6, 0, 6) };
        form.Children.Add(Pastel.Text("이름", 12)); form.Children.Add(name); form.Children.Add(Pastel.Text("날짜 (예: 2026-03-14)", 12)); form.Children.Add(date); form.Children.Add(yearly);
        var actions = new WrapPanel();
        actions.Children.Add(Pastel.Button("추가", () =>
        {
            var item = new Anniversary { Name = name.Text.Trim(), Date = date.Text.Trim(), Yearly = yearly.IsChecked == true };
            if (item.Name.Length == 0) { status.Text = "기념일 이름을 적어 주세요."; return; }
            if (Anniversaries.Day(item) is not DateTime day || day.Year < 1900 || day.Year > 2200) { status.Text = "날짜를 2026-03-14 같은 형식으로 적어 주세요."; return; }
            if (buddy.Adventure.State.Anniversaries.Count >= 50) { status.Text = "기념일은 50개까지 등록할 수 있어요."; return; }
            buddy.Adventure.State.Anniversaries.Add(item); buddy.Adventure.Touch(); name.Clear(); status.Text = item.Name + " 등록 ♡"; Refresh();
        }));
        actions.Children.Add(Pastel.Button("처음 만난 날 넣기", () =>
        {
            var friend = buddy.Adventure.Friend(buddy.CharacterId, buddy.Chat.Preferences.Profile.Name);
            name.Text = friend.Name + "와(과) 처음 만난 날"; date.Text = friend.Met; yearly.IsChecked = false; status.Text = "이 프로그램에서 처음 만난 날이에요. 날짜를 고친 뒤 '추가'를 눌러 주세요.";
        }, true));
        form.Children.Add(actions); p.Children.Add(Pastel.Card(form, true)); p.Children.Add(status);
        var footer = new WrapPanel(); footer.Children.Add(Pastel.Button("대사집에서 기념일 대사 쓰기", buddy.OpenLineBook, true)); footer.Children.Add(Pastel.Button("닫기", Close, true)); p.Children.Add(footer);
        Pastel.Window(this, "기념일 · 디데이", Pastel.Scroll(p), 560, 760); Refresh();
    }
    void Refresh()
    {
        list.Children.Clear(); var today = DateTime.Now.Date; var items = buddy.Adventure.State.Anniversaries;
        if (items.Count == 0) { list.Children.Add(Pastel.Text("아직 등록한 기념일이 없어요.", 13)); return; }
        // The nearest celebration first.
        int Order(Anniversary item) => Anniversaries.Day(item) is not DateTime day ? int.MaxValue : item.Yearly ? (Anniversaries.Next(day, today) - today).Days : day > today ? (day - today).Days : 100 - ((today - day).Days + 1) % 100;
        foreach (var item in items.OrderBy(Order).ToArray())
        {
            var row = new DockPanel(); var remove = Pastel.Button("삭제", () =>
            {
                if (MessageBox.Show(this, "'" + item.Name + "' 기념일을 삭제할까요?", "기념일", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                items.RemoveAll(a => a.Id == item.Id); buddy.Adventure.Touch(); Refresh();
            }, true);
            remove.Padding = new Thickness(9, 3, 9, 3); remove.FontSize = 12; remove.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            var count = new TextBlock { Text = Anniversaries.DDay(item, today), Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 18, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
            DockPanel.SetDock(count, Dock.Left); row.Children.Add(count);
            var words = new StackPanel(); words.Children.Add(new TextBlock { Text = item.Name, Foreground = Pastel.Ink, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
            words.Children.Add(new TextBlock { Text = item.Date + (item.Yearly ? " · 매년" : " · 날짜 세기"), Foreground = Pastel.Ink, FontSize = 12, Opacity = .85 }); row.Children.Add(words);
            var card = Pastel.Card(row); card.Padding = new Thickness(14, 9, 12, 9); card.Margin = new Thickness(0, 0, 4, 6); list.Children.Add(card);
        }
    }
}

// Lines the user kept with the ♡ on a bubble: a collection per character that stays after the program closes.
public sealed class CollectionWindow : Window
{
    readonly Buddy buddy; readonly StackPanel list = new(); readonly ComboBox filter = new(); readonly TextBlock count = Pastel.Text("", 12);
    public CollectionWindow(Buddy buddy)
    {
        this.buddy = buddy; var p = new StackPanel { Margin = new Thickness(22) };
        p.Children.Add(Pastel.Text("대사 도감 ♡", 22));
        p.Children.Add(Pastel.Text("말풍선의 ♡ 버튼이나 '지난 대사 다시 보기'에서 담은 대사를 모아 둡니다. 프로그램을 꺼도 남아요.", 12));
        p.Children.Add(filter); p.Children.Add(count); p.Children.Add(list);
        var actions = new WrapPanel();
        actions.Children.Add(Pastel.Button("텍스트 파일로 내보내기", () =>
        {
            var dialog = new SaveFileDialog { Filter = "텍스트|*.txt", FileName = "대사-도감-" + DateTime.Now.ToString("yyyyMMdd") }; if (dialog.ShowDialog(this) != true) return;
            try { File.WriteAllText(dialog.FileName, string.Join("\r\n\r\n", Shown().Select(l => $"[{l.At:yyyy-MM-dd HH:mm}] {l.Speaker}\r\n{l.Text}"))); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { count.Text = "저장하지 못했습니다. " + e.Message; }
        }, true));
        actions.Children.Add(Pastel.Button("지난 대사에서 담기", buddy.OpenLines, true)); actions.Children.Add(Pastel.Button("닫기", Close, true)); p.Children.Add(actions);
        filter.SelectionChanged += (_, _) => Refresh(false);
        buddy.Collection.Changed += Changed; Closed += (_, _) => buddy.Collection.Changed -= Changed;
        Pastel.Window(this, "대사 도감", Pastel.Scroll(p), 580, 760); Refresh(true);
    }
    void Changed() => Dispatcher.BeginInvoke(() => Refresh(true));
    CollectedLine[] Shown() { string wanted = filter.SelectedItem as string ?? ""; return buddy.Collection.Items.AsEnumerable().Reverse().Where(l => filter.SelectedIndex <= 0 || l.Speaker == wanted).ToArray(); }
    void Refresh(bool speakers)
    {
        if (speakers)
        {
            string chosen = filter.SelectedItem as string ?? ""; var names = new[] { "모든 캐릭터" }.Concat(buddy.Collection.Items.Select(l => l.Speaker).Distinct()).ToArray();
            filter.ItemsSource = names; filter.SelectedIndex = Math.Max(0, Array.IndexOf(names, chosen)); return;   // selecting refreshes the list
        }
        list.Children.Clear(); var lines = Shown(); count.Text = $"담은 대사 {lines.Length}개 (전체 {buddy.Collection.Items.Count} / {LineCollection.Limit})";
        if (lines.Length == 0) { list.Children.Add(Pastel.Text("아직 담은 대사가 없어요. 마음에 드는 말풍선의 ♡를 눌러 보세요.", 13)); return; }
        foreach (var line in lines.Take(300))
        {
            var entry = new StackPanel(); var head = new DockPanel(); var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right);
            void Small(Button button) { button.Padding = new Thickness(8, 2, 8, 2); button.Margin = new Thickness(4, 0, 0, 0); button.FontSize = 11; buttons.Children.Add(button); }
            Small(Pastel.Button("복사", () => { try { Clipboard.SetText(line.Text); } catch (System.Runtime.InteropServices.ExternalException) { /* another program is holding the clipboard */ } }, true));
            Small(Pastel.Button("빼기", () => buddy.Collection.Remove(line.Id)));
            head.Children.Add(buttons); head.Children.Add(new TextBlock { Text = $"♥ {line.Speaker} · {line.At:yyyy-MM-dd}", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            entry.Children.Add(head);
            entry.Children.Add(new TextBox { Text = line.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Pastel.Ink, FontSize = 14, Padding = new Thickness(0), Margin = new Thickness(0, 3, 0, 0) });
            var card = Pastel.Card(entry); card.Padding = new Thickness(12, 8, 12, 8); card.Margin = new Thickness(0, 0, 4, 6); list.Children.Add(card);
        }
        if (lines.Length > 300) list.Children.Add(Pastel.Text("최근 300개만 보여줍니다. 전체는 텍스트 파일로 내보낼 수 있어요.", 12));
    }
}
