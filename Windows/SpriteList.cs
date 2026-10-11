using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace KeyboardWorker;

// One character's standing pictures: add several at once (named after their files), rename, remove. The previews can be folded away.
public sealed class SpriteList : StackPanel
{
    readonly Buddy buddy; readonly Buddy.StageCharacter character; readonly Window owner; readonly Action<string> say; readonly WrapPanel cards = new(); readonly Button heading; bool built;
    readonly CheckBox clear = new() { Content = "단색 배경이면 투명하게 만들어서 넣기", Margin = new Thickness(0, 6, 0, 2) };
    public SpriteList(Buddy buddy, Buddy.StageCharacter character, Window owner, Action<string> say)
    {
        this.buddy = buddy; this.character = character; this.owner = owner; this.say = say; var state = buddy.Stage.State;
        // Folded previews are not built (or decoded) until they are opened.
        heading = StageParts.Fold("", cards, state.FoldSprites, folded => { state.FoldSprites = folded; buddy.Stage.Save(); if (!folded && !built) Refresh(); });
        Children.Add(Pastel.Text(character.Name + "의 스탠딩 그림", 16));
        Children.Add(Pastel.Text("감정이나 상황마다 한 장씩 넣어 두면 대사에 어울리는 그림이 자동으로 나옵니다. 파일 이름이 smile · sad · angry · surprise · shy · sleepy · default (또는 웃음 · 슬픔 · 화남 …)이면 알아서 이름이 붙고, 다른 이름(예: 교복, 비 오는 날)은 그대로 상황 이름이 돼요.", 12));
        Children.Add(clear);
        Children.Add(Pastel.Text("배경이 이미 투명한 PNG는 그대로 들어갑니다. 체크하면 가장자리와 이어진 단색 배경(흰색 등)만 지워요.", 11));
        var buttons = new WrapPanel(); buttons.Children.Add(Pastel.Button("그림 추가… (여러 장 선택 가능)", async () => await Add())); Children.Add(buttons);
        Children.Add(heading); Children.Add(cards); Refresh();
    }
    async Task Add()
    {
        var dialog = new OpenFileDialog { Title = "스탠딩 그림 선택", Filter = "이미지 (PNG / JPG)|*.png;*.jpg;*.jpeg", Multiselect = true, CheckFileExists = true }; if (dialog.ShowDialog(owner) != true) return;
        int added = 0; string problem = ""; bool transparent = clear.IsChecked == true; string root = Storage.Root; owner.IsEnabled = false; say("그림을 넣는 중…");
        try
        {
            foreach (string file in dialog.FileNames)
            {
                try { string stored = await Task.Run(() => StageAssets.ImportSprite(file, root, transparent)); buddy.Stage.AddSprite(character.Id, StageTags.FromFile(file), stored); added++; }
                catch (Exception e) { problem = Path.GetFileName(file) + ": " + e.Message; if (e is InvalidOperationException) break; }
            }
        }
        finally { owner.IsEnabled = true; }
        say(added + "장을 넣었어요." + (problem.Length > 0 ? " 넣지 못한 그림 — " + problem : "")); Refresh();
    }
    void Refresh()
    {
        var sprites = buddy.Stage.Set(character.Id).Sprites; StageParts.Retitle(heading, $"미리보기 ({sprites.Count}/{StageBook.MaxSprites}장)");
        cards.Children.Clear(); built = !buddy.Stage.State.FoldSprites; if (!built) return;
        if (sprites.Count == 0) cards.Children.Add(Pastel.Text("아직 넣은 그림이 없어요.", 13));
        foreach (var sprite in sprites.ToArray())
        {
            var card = new StackPanel { Width = 132, Margin = new Thickness(0, 8, 10, 4) }; card.Children.Add(StageParts.Thumbnail(sprite.Path, true));
            var name = StageParts.Field(sprite.Tag, "감정 · 상황 이름");
            name.LostFocus += (_, _) => { string cleaned = StageTags.Clean(name.Text); if (cleaned != sprite.Tag) { sprite.Tag = cleaned; buddy.Stage.Save(); say("이름을 '" + cleaned + "'(으)로 바꿨어요."); } name.Text = sprite.Tag; };
            card.Children.Add(name); card.Children.Add(StageParts.Small("빼기", () => { sprites.Remove(sprite); buddy.Stage.Save(); Refresh(); })); cards.Children.Add(card);
        }
    }
}
