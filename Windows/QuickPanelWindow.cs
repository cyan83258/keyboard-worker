using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// One on/off choice that the quick panel shows as a checkbox and a situation profile can remember.
// Set changes the settings; Special is for choices kept somewhere else (game badge, second pet, AI lines).
public sealed record QuickToggle(string Key, string Group, string Title, Func<Buddy, bool> Get, Action<Preferences, bool>? Set, Action<Buddy, bool>? Special = null);
public static class QuickToggles
{
    const string Shown = "화면에 보이는 것", Sound = "소리", Talk = "먼저 말 걸기", Motion = "움직임과 동작";
    static void Game(Buddy buddy, Action<GameState> change) { var options = buddy.Game.State.Snapshot(); change(options); buddy.Game.Options(options); }
    static void Auto(Buddy buddy, Func<AutomaticOptions, AutomaticOptions> change) { var chat = buddy.Chat.Preferences.Snapshot(); chat.Automatic = change(chat.Automatic); buddy.Chat.Apply(chat); }
    public static readonly QuickToggle[] All =
    {
        new("quick", Shown, "한마디 버튼", b => b.Pref.Desktop.QuickButton, (p, v) => p.Desktop.QuickButton = v),
        new("timer", Shown, "오늘 작업 시간 (타이머)", b => b.Pref.Desktop.ShowWorkOnPet, (p, v) => p.Desktop.ShowWorkOnPet = v),
        new("badge", Shown, "레벨 · 코인", b => b.Game.State.ShowBadge, null, (b, v) => Game(b, g => g.ShowBadge = v)),
        new("todo", Shown, "할 일 한 줄 메모", b => b.Pref.ShowTodo, (p, v) => p.ShowTodo = v),
        new("player", Shown, "뮤직플레이어", b => b.Pref.Music.PlayerVisible, (p, v) => p.Music = p.Music with { PlayerVisible = v }),
        new("accessories", Shown, "장식 아이템", b => b.Game.State.ShowAccessories, null, (b, v) => Game(b, g => g.ShowAccessories = v)),
        new("companion", Shown, "둘째 친구", b => b.Pref.Duo.Enabled, null, (b, v) => { if (v != b.Pref.Duo.Enabled) b.SetCompanion(v); }),
        new("typing", Sound, "타이핑 소리", b => b.Pref.Sound, (p, v) => p.Sound = v),
        new("voice", Sound, "캐릭터 음성", b => b.Pref.Voice.Enabled, (p, v) => p.Voice = p.Voice with { Enabled = v }),
        new("local", Talk, "직접 쓴 랜덤 한마디", b => b.Pref.LocalRandom, (p, v) => p.LocalRandom = v),
        new("greetings", Talk, "시간대 인사", b => b.Pref.Greetings, (p, v) => p.Greetings = v),
        new("remarks", Talk, "프로그램 보고 한마디", b => b.Pref.Awareness.Comments, (p, v) => p.Awareness = p.Awareness with { Comments = v }),
        new("ai-random", Talk, "AI 랜덤 한마디", b => b.Chat.Preferences.Automatic.RandomLines, null, (b, v) => Auto(b, a => a with { RandomLines = v })),
        new("ai-nudge", Talk, "AI 재촉", b => b.Chat.Preferences.Automatic.AiNudge, null, (b, v) => Auto(b, a => a with { AiNudge = v })),
        new("duo-talk", Talk, "두 친구의 AI 대화", b => b.Pref.Duo.Interactions, (p, v) => p.Duo = p.Duo with { Interactions = v }),
        new("bounce", Motion, "타이핑할 때 몸 흔들기", b => b.Pref.Desktop.TypingBounce, (p, v) => p.Desktop.TypingBounce = v),
        new("hearts", Motion, "쓰다듬기 하트", b => b.Pref.PettingHearts, (p, v) => p.PettingHearts = v),
        new("through", Motion, "마우스 클릭 통과", b => b.Pref.ClickThrough, (p, v) => p.ClickThrough = v),
        new("locked", Motion, "위치 고정", b => b.Pref.Locked, (p, v) => p.Locked = v),
        new("fullscreen", Motion, "전체 화면에서 숨기기", b => b.Pref.HideFullscreen, (p, v) => p.HideFullscreen = v),
        new("notes", Shown, "설명 글 펼쳐 보기 (? 대신 글로)", b => b.Pref.ShowNotes, (p, v) => p.ShowNotes = v),
        new("game-light", Motion, "게임 중 가볍게", b => b.Pref.Awareness.GameLight, (p, v) => p.Awareness = p.Awareness with { GameLight = v })
    };
    public static QuickToggle? Find(string key) => All.FirstOrDefault(t => t.Key == key);
    public static Dictionary<string, bool> Capture(Buddy buddy) => All.ToDictionary(t => t.Key, t => t.Get(buddy));
    // Sets the given choices; keys a profile does not mention stay as they are.
    public static void Apply(Buddy buddy, IReadOnlyDictionary<string, bool> values)
    {
        var next = buddy.Pref.Snapshot(); bool changed = false;
        foreach (var toggle in All) if (toggle.Set != null && values.TryGetValue(toggle.Key, out bool value) && toggle.Get(buddy) != value) { toggle.Set(next, value); changed = true; }
        if (changed) buddy.Apply(next, configureKeys: false);
        foreach (var toggle in All) if (toggle.Special != null && values.TryGetValue(toggle.Key, out bool value) && toggle.Get(buddy) != value) toggle.Special(buddy, value);
    }
}

// Everything that is switched on and off often, in one place: what shows around the pet, sounds, who speaks first,
// the to-do line and the situation profiles. Each checkbox applies at once.
public sealed class QuickPanelWindow : Window
{
    readonly Buddy buddy; readonly Dictionary<string, CheckBox> boxes = new(); readonly WrapPanel modes = new(); readonly TextBox todo = new() { MaxLength = 120 }; readonly TextBlock status = Pastel.Text("", 12);
    readonly CheckBox todoAi = new() { Content = "AI에게도 할 일을 알려 주기", Margin = new Thickness(0, 4, 0, 0) }; bool filling;
    public QuickPanelWindow(Buddy buddy)
    {
        this.buddy = buddy; var p = new StackPanel { Margin = new Thickness(20, 14, 20, 16) };
        p.Children.Add(Pastel.Text("빠른 설정", 22));
        p.Children.Add(Pastel.Text("체크하면 바로 적용돼요.", 12));

        var memo = new StackPanel(); memo.Children.Add(Pastel.Text("할 일 한 줄 메모", 15));
        var row = new DockPanel(); var memoButtons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(memoButtons, Dock.Right);
        void Small(Panel panel, Button button) { button.Padding = new Thickness(10, 4, 10, 4); button.Margin = new Thickness(5, 0, 0, 0); button.FontSize = 12; panel.Children.Add(button); }
        Small(memoButtons, Pastel.Button("적기", () => { buddy.SetTodo(todo.Text); status.Text = buddy.Pref.Todo.Length > 0 ? "캐릭터 아래에 적어 뒀어요." : "메모를 비웠어요."; Fill(); }));
        Small(memoButtons, Pastel.Button("완료 ✓", () => { if (buddy.Pref.Todo.Length == 0) { status.Text = "완료할 메모가 없어요."; return; } buddy.CompleteTodo(); status.Text = "해냈어요 ♡"; Fill(); }, true));
        row.Children.Add(memoButtons); row.Children.Add(todo); memo.Children.Add(row); memo.Children.Add(todoAi);
        todo.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { buddy.SetTodo(todo.Text); status.Text = "캐릭터 아래에 적어 뒀어요."; Fill(); } };
        todoAi.Click += (_, _) => { if (filling) return; var next = buddy.Pref.Snapshot(); next.TodoToAi = todoAi.IsChecked == true; buddy.Apply(next, configureKeys: false); };
        var memoCard = Pastel.Card(memo, true); memoCard.Padding = new Thickness(14, 8, 14, 12); p.Children.Add(memoCard);

        var profile = new StackPanel(); profile.Children.Add(Pastel.Text("상황 프로필", 15));
        profile.Children.Add(Pastel.Text("아래 체크 상태를 묶어 두고 한 번에 바꿉니다. 누르면 그 프로필로 전환돼요.", 12)); profile.Children.Add(modes);
        var saveRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) }; var name = new TextBox { MaxLength = 30, ToolTip = "프로필 이름" }; var saveButtons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(saveButtons, Dock.Right);
        Small(saveButtons, Pastel.Button("지금 상태를 저장", () => { try { var saved = buddy.SaveMode(name.Text.Length > 0 ? name.Text : Active()?.Name ?? ""); status.Text = "'" + saved.Name + "' 프로필에 지금 상태를 담았어요."; name.Clear(); Fill(); } catch (Exception e) { status.Text = e.Message; } }));
        Small(saveButtons, Pastel.Button("삭제", () =>
        {
            var mode = Active(); if (mode == null) { status.Text = "지울 프로필을 먼저 눌러 고르세요."; return; }
            if (MessageBox.Show(this, "'" + mode.Name + "' 프로필을 삭제할까요?", "상황 프로필", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            buddy.DeleteMode(mode.Id); status.Text = "프로필을 삭제했어요."; Fill();
        }, true));
        saveRow.Children.Add(saveButtons); saveRow.Children.Add(name); profile.Children.Add(saveRow);
        profile.Children.Add(Pastel.Text("이름을 비우고 저장하면 지금 고른 프로필을 덮어씁니다. 새 이름을 적으면 새 프로필이 생겨요.", 11));
        var profileCard = Pastel.Card(profile); profileCard.Padding = new Thickness(14, 8, 14, 12); p.Children.Add(profileCard);

        foreach (var group in QuickToggles.All.GroupBy(t => t.Group))
        {
            p.Children.Add(new TextBlock { Text = group.Key, Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 10, 0, 4) });
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            foreach (var toggle in group)
            {
                var box = new CheckBox { Content = toggle.Title, Margin = new Thickness(0, 3, 8, 3) }; boxes[toggle.Key] = box; grid.Children.Add(box);
                box.Click += (_, _) =>
                {
                    if (filling) return;
                    try { QuickToggles.Apply(buddy, new Dictionary<string, bool> { [toggle.Key] = box.IsChecked == true }); status.Text = toggle.Title + (box.IsChecked == true ? " 켬" : " 끔"); }
                    catch (Exception e) { status.Text = e.Message; }
                    Fill();
                };
            }
            p.Children.Add(grid);
        }
        p.Children.Add(Pastel.Text("둘째 친구는 '친구 배치'에서 고른 프리셋이 있어야 나와요. 숨기기/보이기·조용한 모드·휴식은 우클릭 메뉴와 단축키에 있어요.", 11));
        p.Children.Add(status);
        var footer = new WrapPanel(); footer.Children.Add(Pastel.Button("전체 설정 열기", buddy.OpenSettings, true)); footer.Children.Add(Pastel.Button("닫기", Close)); p.Children.Add(footer);
        Pastel.Window(this, "빠른 설정", Pastel.Scroll(p), 520, 860); ShowInTaskbar = false; Activated += (_, _) => Fill(); Fill();
    }
    // Only the user's own profiles can be overwritten or deleted here; those of mods belong to their files.
    ModeProfile? Active() => buddy.Pref.Modes.FirstOrDefault(m => m.Id == buddy.Pref.ActiveMode);
    void Fill()
    {
        filling = true;
        foreach (var toggle in QuickToggles.All) boxes[toggle.Key].IsChecked = toggle.Get(buddy);
        if (!todo.IsKeyboardFocused) todo.Text = buddy.Pref.Todo; todoAi.IsChecked = buddy.Pref.TodoToAi;
        modes.Children.Clear(); var now = QuickToggles.Capture(buddy);
        foreach (var mode in buddy.AllModes)
        {
            // A profile is "on" while every choice it remembers still matches.
            bool matches = mode.Values.All(v => !now.TryGetValue(v.Key, out bool value) || value == v.Value);
            var button = Pastel.Button(mode.Name, () => { buddy.ApplyMode(mode.Id); status.Text = "'" + mode.Name + "' 프로필로 바꿨어요."; Fill(); }, true);
            button.Padding = new Thickness(12, 5, 12, 5); button.Margin = new Thickness(0, 4, 6, 0); Pastel.Select(button, mode.Id == buddy.Pref.ActiveMode && matches);
            if (mode.Id == buddy.Pref.ActiveMode && !matches) button.Content = mode.Name + " (바뀜)";
            modes.Children.Add(button);
        }
        if (buddy.Pref.Modes.Count == 0) modes.Children.Add(Pastel.Text("저장한 프로필이 없어요.", 12));
        filling = false;
    }
}
