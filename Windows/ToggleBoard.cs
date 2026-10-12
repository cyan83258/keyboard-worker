using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// The on/off choices (QuickToggles) as checkboxes, group by group. A group can be switched on or off as a whole, and a
// choice with finer settings has a small gear that opens them. Choices that do nothing just now, because a switch over
// them is off, are drawn pale. Used by the quick panel (all groups) and the "먼저 말하기" window (its three groups).
public sealed class ToggleBoard : StackPanel
{
    readonly Buddy buddy; readonly Action<string> say; readonly Action? changed; readonly Dictionary<string, (CheckBox Box, FrameworkElement Row, QuickToggle Toggle)> rows = new(); bool filling;
    // groups: which groups to show (null = all). changed: called after something was switched.
    public ToggleBoard(Buddy buddy, string[]? groups, Action<string> say, Action? changed = null)
    {
        this.buddy = buddy; this.say = say; this.changed = changed;
        foreach (var group in QuickToggles.All.Where(t => groups == null || groups.Contains(t.Group)).GroupBy(t => t.Group))
        {
            var head = new DockPanel { Margin = new Thickness(0, 12, 0, 4) }; var members = group.ToArray();
            // The two switches over everything are not switched together; every other group is.
            if (group.Key != QuickToggles.Speak)
            {
                var both = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(both, Dock.Right); head.Children.Add(both);
                both.Children.Add(Small("모두 켜기", () => Switch(members, true))); both.Children.Add(Small("모두 끄기", () => Switch(members, false)));
            }
            head.Children.Add(new TextBlock { Text = group.Key, Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center }); Children.Add(head);
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            foreach (var toggle in members)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 10, 2) }; var box = new CheckBox { Content = toggle.Title, VerticalAlignment = VerticalAlignment.Center };
                if (toggle.More != null)
                {
                    var more = new Button { Content = "⚙", FontSize = 13, Padding = new Thickness(5, 0, 5, 1), Foreground = Pastel.Ink, Opacity = .65, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "세부 설정 열기", VerticalAlignment = VerticalAlignment.Center };
                    Pastel.Ghost(more); more.Click += (_, _) => { toggle.More(buddy); Fill(); }; DockPanel.SetDock(more, Dock.Right); row.Children.Add(more);
                }
                row.Children.Add(box); rows[toggle.Key] = (box, row, toggle); grid.Children.Add(row);
                box.Click += (_, _) => { if (!filling) Switch(new[] { toggle }, box.IsChecked == true); };
            }
            Children.Add(grid);
        }
        Fill();
    }
    static Button Small(string text, Action action) { var button = Pastel.Button(text, action, true); button.Padding = new Thickness(9, 2, 9, 3); button.Margin = new Thickness(5, 0, 0, 0); button.FontSize = 11.5; return button; }
    void Switch(QuickToggle[] toggles, bool on)
    {
        try { QuickToggles.Apply(buddy, toggles.ToDictionary(t => t.Key, _ => on)); say(toggles.Length == 1 ? toggles[0].Title + (on ? " 켬" : " 끔") : toggles[0].Group + (on ? " · 모두 켰어요" : " · 모두 껐어요")); }
        catch (Exception e) { say(e.Message); }
        Fill(); changed?.Invoke();
    }
    // The checkboxes as the settings are now.
    public void Fill()
    {
        filling = true;
        foreach (var (box, row, toggle) in rows.Values) { box.IsChecked = toggle.Get(buddy); bool live = QuickToggles.Live(buddy, toggle); row.Opacity = live ? 1 : .45; row.ToolTip = live ? null : "위의 전체 스위치가 꺼져 있어서 지금은 동작하지 않아요"; }
        filling = false;
    }
}

// Everything the pets say first, in one window: the two switches over all of it, each kind of line with its own switch,
// and why the pets are (not) about to speak. The finer settings of each kind open from its gear.
public sealed class SpeechWindow : Window
{
    public SpeechWindow(Buddy buddy)
    {
        var p = new StackPanel { Margin = new Thickness(20, 14, 20, 16) }; var status = Pastel.Text("", 12);
        p.Children.Add(Pastel.Text("먼저 말하기", 22));
        p.Children.Add(Pastel.Text("캐릭터가 먼저 건네는 말을 여기서 한꺼번에 켜고 끕니다. 위의 두 스위치가 전체를 좌우해요: '먼저 말하기'를 끄면 캐릭터는 말을 걸 때만 대답하고, 'AI로 대사 만들기'를 끄면 대사집에 내가 쓴 대사만 나옵니다. 써 둔 대사도 없고 AI도 꺼져 있으면 아무 말도 하지 않아요.", 12));
        var board = new ToggleBoard(buddy, QuickToggles.Speaking, text => status.Text = text); p.Children.Add(board);
        p.Children.Add(AutomaticStatusCard.Build(this, buddy, "랜덤 한마디 지금 한 번 말해보기", false));
        p.Children.Add(status);
        var footer = new WrapPanel();
        footer.Children.Add(Pastel.Button("대사집 열기", buddy.OpenLineBook, true)); footer.Children.Add(Pastel.Button("AI 대사 간격 · 프롬프트", buddy.OpenAutomatic, true)); footer.Children.Add(Pastel.Button("닫기", Close)); p.Children.Add(footer);
        Pastel.Window(this, "먼저 말하기", Pastel.Scroll(p), 600, 760); Activated += (_, _) => board.Fill();
    }
}
