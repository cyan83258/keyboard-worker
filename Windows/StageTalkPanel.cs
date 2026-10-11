using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace KeyboardWorker;

// How the characters talk on their pages: the wanted length, the room for an answer, the prompt written for these talks,
// and this character's own thread (export, clear). The ordinary chat with the desktop pets is set elsewhere and is not touched here.
public sealed class StageTalkPanel : StackPanel
{
    public StageTalkPanel(Buddy buddy, Buddy.StageCharacter character, Window owner, Action<string> say)
    {
        var options = buddy.Stage.State.Talk; var mode = buddy.PageMode;
        Children.Add(Pastel.Text("캐릭터 페이지의 대화", 20));
        Children.Add(Pastel.Text("여기서 나누는 대화는 한마디·채팅과 따로 저장되고, 프롬프트와 길이도 따로 정합니다. 모든 캐릭터의 페이지에 함께 적용돼요.", 12));

        Children.Add(Pastel.Text("답변 길이", 14));
        var length = new ComboBox { ItemsSource = StageTalkOptions.Lengths.Select(l => l.Name).ToArray(), SelectedItem = options.Length }; var guide = Pastel.Text("", 12);
        void Describe() => guide.Text = StageTalkOptions.Guide(length.SelectedItem as string ?? "") + (mode.Length.Length > 0 ? "  (지금 고른 '" + mode.Name + "' 모드는 길이를 '" + mode.Length + "'로 정해 두었어요.)" : "");
        length.SelectionChanged += (_, _) => Describe(); Describe(); Children.Add(length); Children.Add(guide);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        TextBox Number(string label, int value, string unit) { row.Children.Add(new TextBlock { Text = label, Foreground = Pastel.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }); var box = new TextBox { Text = value.ToString(), Width = 80, MaxLength = 5 }; row.Children.Add(box); row.Children.Add(new TextBlock { Text = unit, Foreground = Pastel.Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 22, 0) }); return box; }
        var tokens = Number("최대 출력 토큰", options.MaxTokens, "256~16000"); var context = Number("기억하는 지난 대화", options.ContextTurns, "0~60개"); Children.Add(row);
        Children.Add(Pastel.Text("토큰은 답변 하나가 쓸 수 있는 최대 분량입니다. 생각을 먼저 하는 모델은 그 일부를 생각에 쓰므로, 답이 중간에 끊기면 늘려 주세요. 길이는 '이 정도로 써 달라'는 요청이고, 토큰은 넘을 수 없는 한도예요.", 11));

        Children.Add(Pastel.Text("대화 프롬프트", 14));
        Children.Add(Pastel.Text("넣을 수 있는 말: {name} 캐릭터 이름 · {user_name} 내 호칭 · {length} 위에서 고른 길이 설명. 캐릭터 프로필과 '캐릭터성 우선' 규칙은 이 프롬프트 앞에 항상 붙습니다.", 11));
        var prompt = new TextBox { Text = options.Prompt, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 280, MaxLength = 10000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13 }; Children.Add(prompt);
        var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("저장", () =>
        {
            if (!int.TryParse(tokens.Text, out int room) || room < 256 || room > 16000) { say("최대 출력 토큰은 256~16000 사이의 숫자로 적어 주세요."); return; }
            if (!int.TryParse(context.Text, out int memory) || memory < 0 || memory > 60) { say("기억하는 지난 대화는 0~60 사이의 숫자로 적어 주세요."); return; }
            options.Length = length.SelectedItem as string ?? "중간"; options.MaxTokens = room; options.ContextTurns = memory; options.Prompt = prompt.Text; buddy.Stage.Save(); prompt.Text = options.Prompt;
            say("대화 설정을 저장했어요 ♡ " + DateTime.Now.ToString("HH:mm:ss"));
        }));
        buttons.Children.Add(Pastel.Button("기본 프롬프트로", () => { prompt.Text = StageTalkOptions.StandardPrompt; say("'저장'을 눌러야 적용돼요."); }, true)); Children.Add(buttons);

        Children.Add(Pastel.Text("둘만의 페이지 프롬프트", 14));
        Children.Add(Pastel.Text("두 캐릭터가 나 없이 이야기하는 '둘만의 페이지'에 쓰입니다. 넣을 수 있는 말: {first_name} · {second_name} · {length} · {turns}(주고받는 횟수 — 길이에 따라 2~8). 두 사람의 프로필과 관계 설정은 앞에 항상 붙어요.", 11));
        var pairPrompt = new TextBox { Text = options.PairPrompt, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 200, MaxLength = 10000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13 }; Children.Add(pairPrompt);
        var pairButtons = new WrapPanel();
        pairButtons.Children.Add(Pastel.Button("둘만의 프롬프트 저장", () => { options.PairPrompt = pairPrompt.Text; buddy.Stage.Save(); pairPrompt.Text = options.PairPrompt; say("둘만의 페이지 프롬프트를 저장했어요 ♡"); }));
        pairButtons.Children.Add(Pastel.Button("기본으로", () => { pairPrompt.Text = StageTalkOptions.StandardPairPrompt; say("'둘만의 프롬프트 저장'을 눌러야 적용돼요."); }, true)); Children.Add(pairButtons);

        string thread = mode.Thread(character.Id);
        Children.Add(Pastel.Text(character.Name + "와(과) 나눈 대화 (" + mode.Name + ")", 14)); var count = Pastel.Text("", 12); Children.Add(count);
        void Count() => count.Text = "이 캐릭터 · 이 모드에서 나눈 대화 " + buddy.StageTalks.Turns(thread).Count + "개 (캐릭터와 모드마다 따로, 최근 " + StageLog.Keep + "개까지 보관)"; Count();
        var history = new WrapPanel();
        history.Children.Add(Pastel.Button("텍스트 파일로 내보내기…", () =>
        {
            var turns = buddy.StageTalks.Turns(thread); if (turns.Count == 0) { say("내보낼 대화가 없어요."); return; }
            var dialog = new SaveFileDialog { Filter = "텍스트 (*.txt)|*.txt|Markdown (*.md)|*.md", FileName = character.Name + "-" + mode.Name + "-" + DateTime.Now.ToString("yyyyMMdd") }; if (dialog.ShowDialog(owner) != true) return;
            string user = string.IsNullOrWhiteSpace(character.Persona.UserName) ? "나" : character.Persona.UserName;
            try { File.WriteAllText(dialog.FileName, ChatSearchWindow.Text(turns, dialog.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase), user), new System.Text.UTF8Encoding(true)); say(turns.Count + "개 대화를 내보냈어요."); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { say("저장하지 못했습니다. " + e.Message); }
        }, true));
        history.Children.Add(Pastel.Button("이 대화 지우기", () =>
        {
            if (buddy.StageTalks.Turns(thread).Count == 0) { say("지울 대화가 없어요."); return; }
            if (MessageBox.Show(owner, character.Name + "와(과) '" + mode.Name + "' 모드에서 나눈 대화를 모두 지울까요?\n되돌릴 수 없어요.", "대화 지우기", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            buddy.StageTalks.Clear(thread); Count(); say("대화를 지웠어요. 페이지를 닫았다 열면 처음부터 시작합니다.");
        }, true));
        Children.Add(history);
    }
}
