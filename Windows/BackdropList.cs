using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace KeyboardWorker;

// The backgrounds shared by every character page: each has a title (what the AI chooses by) and a folder to sort it into.
// Folders and the whole list can be folded away; a folded part is not built until it is opened.
public sealed class BackdropList : StackPanel
{
    readonly Buddy buddy; readonly Window owner; readonly Action<string> say; readonly StackPanel groups = new(); readonly Button heading; bool built;
    readonly HashSet<string> folded = new(StringComparer.OrdinalIgnoreCase);   // folders folded away, while this window is open
    readonly ComboBox into = new() { IsEditable = false, MinWidth = 150, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "새 배경을 넣을 폴더" };
    readonly CheckBox auto = new() { Content = "대화 상황에 맞춰 캐릭터가 배경을 자동으로 바꾸기", Margin = new Thickness(0, 6, 0, 2) };
    const string Loose = "분류 없음";
    public BackdropList(Buddy buddy, Window owner, Action<string> say)
    {
        this.buddy = buddy; this.owner = owner; this.say = say; var state = buddy.Stage.State;
        heading = StageParts.Fold("", groups, state.FoldBackdrops, now => { state.FoldBackdrops = now; buddy.Stage.Save(); if (!now && !built) Refresh(); });
        Children.Add(Pastel.Text("배경 (모든 캐릭터가 함께 써요)", 16));
        Children.Add(Pastel.Text("제목은 AI가 배경을 고를 때 보는 이름이에요 (예: 교실 저녁, 비 오는 거리, 내 방). 파일 이름이 그대로 제목이 되고 아래에서 고칠 수 있습니다. 폴더 칸에 같은 이름을 적으면 한 묶음으로 모여요. 썸네일을 누르면 그 배경으로 바뀝니다.", 12));
        auto.IsChecked = state.AutoBackdrop; auto.Click += (_, _) => { state.AutoBackdrop = auto.IsChecked == true; buddy.Stage.Save(); }; Children.Add(auto);
        Children.Add(Pastel.Text("켜 두면 대화 속 장소·시간·분위기가 달라졌을 때 캐릭터가 어울리는 배경을 고릅니다 (배경이 2장 이상일 때). 끄면 직접 고른 배경이 그대로 있어요.", 11));
        var buttons = new WrapPanel(); buttons.Children.Add(Pastel.Button("배경 추가… (여러 장 선택 가능)", async () => await Add())); buttons.Children.Add(into);
        var none = Pastel.Button("배경 없이 보기", () => { state.Backdrop = ""; buddy.Stage.Save(); Refresh(); }, true); none.Margin = new Thickness(8, 0, 0, 0); buttons.Children.Add(none); Children.Add(buttons);
        Children.Add(heading); Children.Add(groups); Refresh();
    }
    async Task Add()
    {
        var dialog = new OpenFileDialog { Title = "배경 선택", Filter = "이미지 (PNG / JPG)|*.png;*.jpg;*.jpeg", Multiselect = true, CheckFileExists = true }; if (dialog.ShowDialog(owner) != true) return;
        int added = 0; string problem = "", root = Storage.Root, group = into.SelectedItem as string is { } chosen && chosen != Loose ? chosen : ""; owner.IsEnabled = false; say("배경을 넣는 중…");
        try
        {
            foreach (string file in dialog.FileNames)
            {
                try { string stored = await Task.Run(() => StageAssets.ImportBackground(file, root)); buddy.Stage.AddBackdrop(StageTags.BackdropTitle(file), group, stored); added++; }
                catch (Exception e) { problem = Path.GetFileName(file) + ": " + e.Message; if (e is InvalidOperationException) break; }
            }
        }
        finally { owner.IsEnabled = true; }
        say(added + "장을 넣었어요." + (problem.Length > 0 ? " 넣지 못한 배경 — " + problem : "")); Refresh();
    }
    void Refresh()
    {
        var state = buddy.Stage.State; StageParts.Retitle(heading, $"미리보기 (내 배경 {state.Backdrops.Count}/{StageBook.MaxBackdrops}장" + (StageBook.BuiltIn.Count > 0 ? $" · 기본 제공 {StageBook.BuiltIn.Count}장)" : ")"));
        string picked = into.SelectedItem as string ?? Loose; var names = new[] { Loose }.Concat(state.Backdrops.Select(b => b.Group).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)).ToArray(); into.ItemsSource = names; into.SelectedItem = names.Contains(picked) ? picked : Loose;
        groups.Children.Clear(); built = !state.FoldBackdrops; if (!built) return;
        if (buddy.Stage.Backdrops.Count == 0) groups.Children.Add(Pastel.Text("아직 넣은 배경이 없어요. 없으면 테마 색 바탕이 보여요.", 13));
        foreach (string group in buddy.Stage.Groups)
        {
            var members = buddy.Stage.Backdrops.Where(b => string.Equals(b.Group, group, StringComparison.OrdinalIgnoreCase)).ToArray(); var cards = new WrapPanel(); string key = group;
            var title = StageParts.Fold((group.Length == 0 ? Loose : "📁 " + group) + " (" + members.Length + ")", cards, folded.Contains(key), now => { if (now) folded.Add(key); else { folded.Remove(key); if (cards.Children.Count == 0) Fill(cards, members); } });
            title.FontSize = 13; title.Margin = new Thickness(6, 8, 0, 0); groups.Children.Add(title); groups.Children.Add(cards);
            if (!folded.Contains(key)) Fill(cards, members);
        }
    }
    void Fill(WrapPanel cards, StageBackdrop[] members)
    {
        var state = buddy.Stage.State;
        foreach (var backdrop in members)
        {
            bool used = string.Equals(state.Backdrop, backdrop.Path, StringComparison.OrdinalIgnoreCase); var card = new StackPanel { Width = 160, Margin = new Thickness(6, 6, 6, 6) };
            var picture = StageParts.Thumbnail(backdrop.Path, false, 160, 96); picture.Cursor = System.Windows.Input.Cursors.Hand; picture.ToolTip = "이 배경으로 바꾸기"; if (used) { picture.BorderBrush = Pastel.Hot; picture.BorderThickness = new Thickness(3); }
            picture.MouseLeftButtonUp += (_, _) => { state.Backdrop = backdrop.Path; buddy.Stage.Save(); Refresh(); };
            // A background that came with the program is only chosen, not edited: its title is its file name.
            if (StageBook.IsBuiltIn(backdrop))
            {
                card.Children.Add(picture); card.Children.Add(new TextBlock { Text = backdrop.Title, Foreground = Pastel.Ink, FontSize = 12, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 5, 0, 0), ToolTip = "프로그램에 들어 있는 배경 · AI가 이 이름으로 고릅니다" });
                card.Children.Add(new TextBlock { Text = used ? "사용 중" : "", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(2, 2, 0, 0) }); cards.Children.Add(card); continue;
            }
            var title = StageParts.Field(backdrop.Title, "제목 (AI가 이 이름으로 고릅니다)"); var folder = StageParts.Field(backdrop.Group, "폴더 (비우면 분류 없음)");
            title.LostFocus += (_, _) => { string cleaned = StageTags.Clean(title.Text, "배경"); if (cleaned != backdrop.Title) { backdrop.Title = cleaned; buddy.Stage.Save(); say("제목을 '" + cleaned + "'(으)로 바꿨어요."); } title.Text = backdrop.Title; };
            folder.LostFocus += (_, _) => { string cleaned = SafeFiles.OneLine(folder.Text, 20); if (cleaned != backdrop.Group) { backdrop.Group = cleaned; buddy.Stage.Save(); say(cleaned.Length == 0 ? "폴더에서 뺐어요." : "'" + cleaned + "' 폴더로 옮겼어요."); Dispatcher.BeginInvoke(Refresh); } };
            var row = new DockPanel(); var remove = StageParts.Small("빼기", () => { state.Backdrops.Remove(backdrop); buddy.Stage.Save(); Refresh(); }); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            row.Children.Add(new TextBlock { Text = used ? "사용 중" : "", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            card.Children.Add(picture); card.Children.Add(title); card.Children.Add(folder); card.Children.Add(row); cards.Children.Add(card);
        }
    }
}
