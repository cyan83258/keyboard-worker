using System;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// What the three page windows (character page, text messages, page for two) have in common: the list of chat rooms down
// the left, and a top bar that starts with the fold button and the tabs that lead from one window to the others.
// A window puts its own choices and buttons on the bar and its own content under it (Root: dock the bottom row, then add the middle).
public sealed class PageFrame
{
    static readonly (string Kind, string Name, string Tip)[] tabs =
    {
        (StageMode.Stage, "마주 보기", "스탠딩 그림과 대사창으로 마주 보고 이야기해요"), (StageMode.Messenger, "문자", "메신저로 문자를 주고받아요 (마주 보기와는 방도 설정도 따로예요)"), (StageMode.Pair, "둘이서", "두 캐릭터가 나 없이 둘이서 이야기해요"), (StageMode.Novel, "소설", "캐릭터가 등장하는 소설을 써요 (방도 프롬프트도 API 설정도 따로예요)")
    };
    // The three windows wear the same title bar, so going from one to another does not change its colour.
    public const bool Sky = true;
    public DockPanel Shell { get; } = new();
    public DockPanel Root { get; } = new() { Margin = new Thickness(16) };
    public StageRoomPanel Rooms { get; }
    readonly DockPanel top = new() { Margin = new Thickness(0, 0, 0, 10) }; readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    // kind: which of the three this window is. busy: an answer is being made. changed: another room is open. character: who to show on the window a tab leads to.
    public PageFrame(Buddy buddy, Window owner, string kind, Func<bool> busy, Action changed, Func<string?> character)
    {
        Rooms = new StageRoomPanel(buddy, owner, busy, changed); DockPanel.SetDock(Rooms, Dock.Left); Shell.Children.Add(Rooms); Shell.Children.Add(Root);
        DockPanel.SetDock(top, Dock.Top); Root.Children.Add(top); DockPanel.SetDock(actions, Dock.Right); top.Children.Add(actions);
        top.Children.Add(new Border());   // the space between the two sides; Left() puts things before it
        var fold = StageParts.Tool("☰", "채팅방 목록 접기 / 펼치기", () => Rooms.Open = !Rooms.Open); fold.Margin = new Thickness(0, 0, 8, 0); fold.FontSize = 14; Left(fold);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
        foreach (var (to, name, tip) in tabs)
        {
            var tab = Pastel.Button(name, () => { if (!busy()) buddy.SwitchPage(owner, to, character()); }, to != kind); tab.Padding = new Thickness(11, 5, 11, 5); tab.FontSize = 12; tab.Margin = new Thickness(0, 0, 3, 0); tab.ToolTip = tip;
            if (to == kind) { tab.IsHitTestVisible = false; tab.FontWeight = FontWeights.Bold; } else tab.Opacity = .78;
            row.Children.Add(tab);
        }
        Left(row);
    }
    // Adds to the left side of the top bar, after what is there.
    public void Left(FrameworkElement part) { part.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(part, Dock.Left); top.Children.Insert(top.Children.Count - 1, part); }
    // Adds a button to the right side of the top bar.
    public Button Tool(string text, string tip, Action action) { var button = StageParts.Tool(text, tip, action); actions.Children.Add(button); return button; }
    // The row under the page: what is typed, and one button that sends - or stops while an answer is being made.
    public static (TextBox Input, Button Send, DockPanel Row) Entry(Action send, Action stop, Func<bool> busy, int max)
    {
        var input = new TextBox { MaxLength = max, Padding = new Thickness(10, 7, 10, 7), FontSize = 15, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 112, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ToolTip = "Enter로 보내고, Shift+Enter로 줄을 바꿔요" };
        var button = Pastel.Button("보내기", () => { if (busy()) stop(); else send(); }); button.Padding = new Thickness(16, 6, 16, 6); button.Margin = new Thickness(6, 0, 0, 0); button.MinWidth = 76; button.VerticalAlignment = VerticalAlignment.Bottom;
        input.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == 0) { e.Handled = true; if (!busy()) send(); } };
        var row = new DockPanel(); DockPanel.SetDock(button, Dock.Right); row.Children.Add(button); return (input, button, row);
    }
}
