using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// The lines each pet says without AI, by situation: nudges, petting, random lines, greetings, remarks about programs,
// rests, music, anniversaries, and what the two pets say to each other.
public sealed class LineBookWindow : Window
{
    sealed record Who(string Id, string Name, bool Main);
    sealed record Row(string Key, string Label);
    readonly Dictionary<string, Dictionary<string, string>> drafts = new();
    public LineBookWindow(Buddy buddy)
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel(); var status = Pastel.Text("", 12); footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(Pastel.Text("대사집 · AI 없이도 풍성하게 ♡", 22));
        header.Children.Add(Pastel.Text("상황마다 캐릭터가 할 말을 직접 적어 두면, AI가 꺼져 있거나 쉬는 동안에도 그 대사로 말을 걸어요. 캐릭터마다 따로 저장됩니다.", 12));
        var people = new List<Who> { new(buddy.CharacterId, "대표 · " + buddy.Chat.Preferences.Profile.Name, true) };
        people.AddRange(buddy.Presets.Items.Where(p => p.Id != buddy.Pref.ActivePresetId).OrderByDescending(p => p.Id == buddy.Pref.Duo.PresetId).Select(p => new Who(p.Id, (p.Id == buddy.Pref.Duo.PresetId ? "둘째 · " : "프리셋 · ") + p.Name, false)));
        var who = new ComboBox { DisplayMemberPath = "Name", ItemsSource = people, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) }; header.Children.Add(who);

        Dictionary<string, string> Load(Who person)
        {
            if (drafts.TryGetValue(person.Id, out var known)) return known;
            var preset = person.Main ? null : buddy.Presets.Items.FirstOrDefault(p => p.Id == person.Id);
            var book = person.Main ? buddy.Pref.LineBook : preset?.LineBook; string nudge = person.Main ? buddy.Pref.Lines : preset?.NudgeLines ?? "", pat = person.Main ? buddy.Pref.PatLines : preset?.PatLines ?? new Preferences().PatLines;
            return drafts[person.Id] = LineBook.Situations.ToDictionary(s => s.Key, s => LineBook.Text(book, nudge, pat, s.Key));
        }

        var list = new ListBox { Width = 300, Margin = new Thickness(0, 0, 14, 0), DisplayMemberPath = "Label", Background = Pastel.Paper, Foreground = Pastel.Ink, BorderBrush = Pastel.Pink };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        DockPanel.SetDock(list, Dock.Left); root.Children.Add(list);
        var editor = new DockPanel(); root.Children.Add(editor);
        var title = Pastel.Text("", 17); title.Foreground = Pastel.Hot; title.FontWeight = FontWeights.Bold; DockPanel.SetDock(title, Dock.Top); editor.Children.Add(title);
        var help = Pastel.Text("", 12); DockPanel.SetDock(help, Dock.Top); editor.Children.Add(help);
        var common = Pastel.Text(LineBook.CommonHelp, 11); common.Opacity = .85; DockPanel.SetDock(common, Dock.Bottom); editor.Children.Add(common);
        var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 10000, MinHeight = 160 }; editor.Children.Add(text);

        string current = LineBook.Nudge; bool loading = false;
        Who Person() => (Who)who.SelectedItem;
        void Fill()
        {
            var texts = Load(Person()); loading = true;
            list.ItemsSource = LineBook.Situations.Select(s => new Row(s.Key, (string.IsNullOrWhiteSpace(texts[s.Key]) ? "○ " : "● ") + s.Group + " · " + s.Title)).ToArray();
            list.SelectedIndex = Math.Max(0, Array.FindIndex(LineBook.Situations, s => s.Key == current)); loading = false; ShowSituation();
        }
        void ShowSituation()
        {
            var situation = LineBook.Find(current) ?? LineBook.Situations[0]; loading = true;
            title.Text = situation.Group + " · " + situation.Title; help.Text = situation.Help; text.Text = Load(Person())[situation.Key]; loading = false;
        }
        text.TextChanged += (_, _) => { if (!loading) Load(Person())[current] = text.Text; };
        list.SelectionChanged += (_, _) => { if (loading || list.SelectedItem is not Row row) return; current = row.Key; ShowSituation(); };
        who.SelectionChanged += (_, _) => Fill();

        var options = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var local = new CheckBox { Content = "직접 쓴 랜덤 한마디 말하기", IsChecked = buddy.Pref.LocalRandom, Margin = new Thickness(0, 4, 14, 4), VerticalAlignment = VerticalAlignment.Center };
        var min = new TextBox { Text = buddy.Pref.LocalRandomMinMinutes.ToString(), Width = 52 }; var max = new TextBox { Text = buddy.Pref.LocalRandomMaxMinutes.ToString(), Width = 52 };
        var greetings = new CheckBox { Content = "시간대 인사 하기", IsChecked = buddy.Pref.Greetings, Margin = new Thickness(18, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
        TextBlock Word(string value) => new() { Text = value, Foreground = Pastel.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), FontSize = 13 };
        options.Children.Add(local); options.Children.Add(Word("간격")); options.Children.Add(min); options.Children.Add(Word("~")); options.Children.Add(max); options.Children.Add(Word("분")); options.Children.Add(greetings);
        footer.Children.Insert(0, options);
        var note = Pastel.Text("AI 랜덤 한마디가 켜져 있고 요청이 가능한 때에는 AI가 말하고, 그렇지 않을 때 이 대사가 나옵니다. 프로그램 한마디·음악·두 친구 대화는 '주변 알아보기'와 '두 친구' 설정이 켜져 있어야 해요.", 11); note.Opacity = .85; footer.Children.Insert(1, note);

        var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("저장", async () =>
        {
            try
            {
                if (!int.TryParse(min.Text, out int low) || low < 1 || low > 1440 || !int.TryParse(max.Text, out int high) || high < low || high > 1440) throw new InvalidOperationException("간격은 1~1440분, 최대가 최소 이상이 되게 입력해 주세요.");
                IsEnabled = false;
                foreach (var person in people)
                {
                    if (!drafts.TryGetValue(person.Id, out var texts)) continue;
                    var book = LineBook.Normalize(texts.ToDictionary(p => p.Key, p => p.Value));
                    if (person.Main)
                    {
                        var next = buddy.Pref.Snapshot(); next.Lines = texts[LineBook.Nudge]; next.PatLines = texts[LineBook.Pat]; next.LineBook = book;
                        next.LocalRandom = local.IsChecked == true; next.LocalRandomMinMinutes = low; next.LocalRandomMaxMinutes = high; next.Greetings = greetings.IsChecked == true;
                        buddy.Apply(next, configureKeys: false, persist: false); await buddy.Presets.SaveDialoguesAsync(next); await Storage.SaveAsync(buddy.Pref);
                    }
                    else await buddy.Presets.SaveLinesAsync(person.Id, texts[LineBook.Nudge], texts[LineBook.Pat], book);
                }
                if (!drafts.ContainsKey(buddy.CharacterId))
                {
                    var next = buddy.Pref.Snapshot(); next.LocalRandom = local.IsChecked == true; next.LocalRandomMinMinutes = low; next.LocalRandomMaxMinutes = high; next.Greetings = greetings.IsChecked == true;
                    buddy.Apply(next, configureKeys: false, persist: false); await Storage.SaveAsync(buddy.Pref);
                }
                status.Text = "저장했어요 ♡ " + DateTime.Now.ToString("HH:mm:ss"); Fill();
            }
            catch (Exception e) { status.Text = e.Message; }
            finally { IsEnabled = true; }
        }));
        buttons.Children.Add(Pastel.Button("이 대사 한 번 말해보기", () =>
        {
            var person = Person(); bool second = !person.Main; if (second && buddy.DisplayedCompanion?.Preset.Id != person.Id) { status.Text = "화면에 나와 있는 친구만 말해볼 수 있어요."; return; }
            var lines = Affection.Available(text.Text.Split('\n'), Affection.Stages); if (lines.Length == 0) { status.Text = "이 상황에 적힌 대사가 없어요."; return; }
            string line = lines[Random.Shared.Next(lines.Length)];
            if (current == "pair") status.Text = "주고받는 대화: " + string.Join("  →  ", LineBook.Turns(line).Select(t => (t.Second ? "둘째" : "대표") + " “" + t.Text + "”"));
            else { buddy.SayTo(person.Id, line, PetMood.Guess(line)); status.Text = "말풍선으로 보여줬어요. ({user} 같은 치환과 저장은 '저장' 후 실제 상황에서 적용돼요.)"; }
        }, true));
        buttons.Children.Add(Pastel.Button("빈 상황에 예시 대사 채우기", () =>
        {
            var texts = Load(Person()); int filled = 0;
            foreach (var sample in LineBook.Samples) if (string.IsNullOrWhiteSpace(texts[sample.Key])) { texts[sample.Key] = sample.Value; filled++; }
            status.Text = filled == 0 ? "비어 있는 상황이 없어요." : filled + "개 상황에 예시를 넣었어요. 캐릭터 말투로 고친 뒤 '저장'을 눌러 주세요."; Fill();
        }, true));
        buttons.Children.Add(Pastel.Button("대사 팩 (코인)", () =>
        {
            var dialog = new Window { Owner = this, ShowInTaskbar = false }; var body = new StackPanel { Margin = new Thickness(20) };
            void Build(string note)
            {
                body.Children.Clear(); body.Children.Add(Pastel.Text("대사 팩", 22));
                body.Children.Add(Pastel.Text($"한 가지 말투로 묶은 대사 모음이에요. 코인으로 한 번 사면 어느 캐릭터의 대사집에든 넣을 수 있고, 넣은 뒤에는 자유롭게 고칠 수 있습니다.\n보유 {buddy.Game.State.Coins:N0}코인 · 지금 고른 캐릭터: {Person().Name}", 12));
                if (note.Length > 0) { var said = Pastel.Text(note, 13); said.Foreground = Pastel.Hot; body.Children.Add(said); }
                foreach (var pack in LinePacks.All)
                {
                    bool owned = buddy.OwnsPack(pack); var card = new StackPanel(); var head = new DockPanel();
                    var action = owned
                        ? Pastel.Button("이 캐릭터 대사집에 넣기", () => { int added = LinePacks.AddTo(Load(Person()), pack); Fill(); Build(added == 0 ? "이미 모두 들어 있어요." : $"'{pack.Name}'의 대사 {added}줄을 넣었어요. 대사집에서 '저장'을 눌러야 적용돼요."); status.Text = "대사 팩을 넣었어요. '저장'을 눌러 주세요."; })
                        : Pastel.Button("구매 · " + pack.Price + "코인", () => { try { buddy.Buy(LinePacks.Key(pack.Id), pack.Price); Build("'" + pack.Name + "'을(를) 샀어요 ♡ 이제 대사집에 넣을 수 있어요."); } catch (Exception e) { Build(e.Message); } }, true);
                    action.Padding = new Thickness(10, 4, 10, 4); action.FontSize = 12; DockPanel.SetDock(action, Dock.Right); head.Children.Add(action);
                    head.Children.Add(new TextBlock { Text = (owned ? "✓ " : "") + pack.Name + " · " + pack.Lines.Sum(l => l.Value.Split('\n').Length) + "줄", Foreground = Pastel.Ink, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
                    card.Children.Add(head); card.Children.Add(new TextBlock { Text = pack.Description, Foreground = Pastel.Ink, FontSize = 12, Margin = new Thickness(0, 3, 0, 0) });
                    card.Children.Add(new TextBlock { Text = "예) " + string.Join("  /  ", pack.Lines.Values.Take(3).Select(v => v.Split('\n')[0])), Foreground = Pastel.Ink, FontSize = 12, Opacity = .85, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
                    var border = Pastel.Card(card, owned); border.Padding = new Thickness(12, 8, 12, 9); border.Margin = new Thickness(0, 0, 4, 6); body.Children.Add(border);
                }
                body.Children.Add(Pastel.Button("닫기", dialog.Close, true));
            }
            Build(""); Pastel.Window(dialog, "대사 팩", Pastel.Scroll(body), 600, 720); dialog.ShowDialog();
        }, true));
        buttons.Children.Add(Pastel.Button("닫기", Close, true)); footer.Children.Add(buttons);
        Pastel.Window(this, "대사집", root, 900, 780); Fill();
    }
}
