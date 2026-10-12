using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// How the characters talk: the wanted length, the room for an answer and the prompt. It edits one set of options -
// the character pages' (with the page for two's prompt) or, with texting, the text messages'. The two sets are separate;
// the ordinary chat with the desktop pets is set elsewhere and is not touched here.
public sealed class StageTalkPanel : StackPanel
{
    // What only text messages have: the texting way of writing, how many messages are kept, and the picture behind them.
    void Texting(Buddy buddy, StageTalkOptions options, Window owner, Action<string> say)
    {
        var state = buddy.Stage.State;
        Children.Add(Pastel.Text("문자 말투", 14));
        var slang = new CheckBox { Content = "문자에서만 쓰는 표현 허용 (ㅋㅋ, ㅎㅎ, ㅠㅠ, ^^ 등)", IsChecked = options.Slang, Margin = new Thickness(0, 2, 0, 2) }; Children.Add(slang);
        slang.Click += (_, _) => { options.Slang = slang.IsChecked == true; buddy.Stage.Save(); say(options.Slang ? "캐릭터에게 어울릴 때 문자 표현을 써요." : "문자 표현 없이 평소 말투로만 써요."); };
        Children.Add(Pastel.Text("켜 두어도 캐릭터의 성격과 말투에 어울릴 때만 씁니다. 끄면 어떤 캐릭터도 쓰지 않아요. 이모티콘은 이 설정과 상관없이 가끔만 쓰도록 되어 있습니다.", 11));

        Children.Add(Pastel.Text("문자 보관", 14));
        var row = new StackPanel { Orientation = Orientation.Horizontal }; var limit = new ComboBox { ItemsSource = StageLog.Limits, SelectedItem = StageLog.Limits.Contains(options.KeepTurns) ? options.KeepTurns : StageLog.Keep, MinWidth = 90 };
        row.Children.Add(new TextBlock { Text = "방마다 최근", Foreground = Pastel.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }); row.Children.Add(limit);
        row.Children.Add(new TextBlock { Text = "개까지 보관 (넘으면 오래된 것부터 자동으로 지워져요. 모아 둔 것은 남아요.)", Foreground = Pastel.Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) }); Children.Add(row);
        limit.SelectionChanged += (_, _) => { if (limit.SelectedItem is int value) { options.KeepTurns = value; buddy.Stage.Save(); say("문자는 방마다 최근 " + value + "개까지 보관해요."); } };

        Children.Add(Pastel.Text("문자방 배경", 14));
        var now = Pastel.Text("", 12); void Show() => now.Text = state.TextBackdrop.Length > 0 ? "배경 사진을 쓰는 중이에요." : "배경 없이 테마 색으로 보여요."; Show();
        var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("배경 사진 고르기…", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "문자방 배경 선택", Filter = "이미지 (PNG / JPG)|*.png;*.jpg;*.jpeg", CheckFileExists = true }; if (dialog.ShowDialog(owner) != true) return;
            string root = Storage.Root, file = dialog.FileName; owner.IsEnabled = false; say("배경을 넣는 중…");
            // The picture is stored shrunk to what a screen needs, like the backgrounds of the pages.
            try { state.TextBackdrop = await System.Threading.Tasks.Task.Run(() => StageAssets.ImportBackground(file, root)); buddy.Stage.Save(); say("문자방 배경을 바꿨어요."); }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or System.IO.InvalidDataException or System.IO.FileFormatException) { say("배경을 넣지 못했어요. " + e.Message); }
            finally { owner.IsEnabled = true; Show(); }
        }));
        buttons.Children.Add(Pastel.Button("배경 없애기", () => { state.TextBackdrop = ""; buddy.Stage.Save(); Show(); say("배경을 없앴어요."); }, true)); Children.Add(buttons); Children.Add(now);
    }
    public StageTalkPanel(Buddy buddy, bool texting, Window owner, Action<string> say)
    {
        var options = texting ? buddy.Stage.State.Texting : buddy.Stage.State.Talk; var mode = texting ? buddy.TextModeNow : buddy.StageModeNow;
        string standard = texting ? StageTalkOptions.StandardTextPrompt : StageTalkOptions.StandardPrompt;
        Children.Add(Pastel.Text(texting ? "문자 메시지의 대화" : "캐릭터 페이지의 대화", 20));
        Children.Add(Pastel.Text(texting ? "문자 메시지의 프롬프트와 길이입니다. 캐릭터 페이지(마주 보기)와는 설정도, 방도, 대화도 따로예요. 모든 캐릭터에게 함께 적용돼요."
            : "여기서 나누는 대화는 한마디·채팅·문자 메시지와 따로 저장되고, 프롬프트와 길이도 따로 정합니다. 모든 캐릭터의 페이지에 함께 적용돼요.", 12));

        Children.Add(Pastel.Text(texting ? "한 번에 보내는 분량" : "답변 길이", 14));
        var length = new ComboBox { ItemsSource = StageTalkOptions.Lengths.Select(l => l.Name).ToArray(), SelectedItem = options.Length }; var guide = Pastel.Text("", 12);
        void Describe() => guide.Text = StageTalkOptions.Guide(length.SelectedItem as string ?? "") + (mode.Length.Length > 0 ? "  (지금 고른 '" + mode.Name + "' 모드는 길이를 '" + mode.Length + "'로 정해 두었어요.)" : "");
        length.SelectionChanged += (_, _) => Describe(); Describe(); Children.Add(length); Children.Add(guide);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        TextBox Number(string label, int value, string unit) { row.Children.Add(new TextBlock { Text = label, Foreground = Pastel.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }); var box = new TextBox { Text = value.ToString(), Width = 80, MaxLength = 5 }; row.Children.Add(box); row.Children.Add(new TextBlock { Text = unit, Foreground = Pastel.Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 22, 0) }); return box; }
        var tokens = Number("최대 출력 토큰", options.MaxTokens, "256~16000"); var context = Number("기억하는 지난 대화", options.ContextTurns, "0~60개"); Children.Add(row);
        Children.Add(Pastel.Text("토큰은 답변 하나가 쓸 수 있는 최대 분량입니다. 생각을 먼저 하는 모델은 그 일부를 생각에 쓰므로, 답이 중간에 끊기면 늘려 주세요. 길이는 '이 정도로 써 달라'는 요청이고, 토큰은 넘을 수 없는 한도예요. 더 오래된 일은 방의 '기억 요약'이 기억합니다.", 11));

        Children.Add(Pastel.Text(texting ? "문자 프롬프트" : "대화 프롬프트", 14));
        Children.Add(Pastel.Text("넣을 수 있는 말: {name} 캐릭터 이름 · {user_name} 내 호칭 · {length} 위에서 고른 길이 설명. 캐릭터 프로필과 '캐릭터성 우선' 규칙은 이 프롬프트 앞에 항상 붙습니다. 방마다 다른 지시는 방 설정(⚙)의 작가 노트에 적어요.", 11));
        var prompt = new TextBox { Text = options.Prompt, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 280, MaxLength = 10000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13 }; Children.Add(prompt);
        var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("저장", () =>
        {
            if (!int.TryParse(tokens.Text, out int room) || room < 256 || room > 16000) { say("최대 출력 토큰은 256~16000 사이의 숫자로 적어 주세요."); return; }
            if (!int.TryParse(context.Text, out int memory) || memory < 0 || memory > 60) { say("기억하는 지난 대화는 0~60 사이의 숫자로 적어 주세요."); return; }
            options.Length = length.SelectedItem as string ?? "중간"; options.MaxTokens = room; options.ContextTurns = memory; options.Prompt = prompt.Text; buddy.Stage.Save(); prompt.Text = options.Prompt;
            say("대화 설정을 저장했어요 ♡ " + DateTime.Now.ToString("HH:mm:ss"));
        }));
        buttons.Children.Add(Pastel.Button("기본 프롬프트로", () => { prompt.Text = standard; say("'저장'을 눌러야 적용돼요."); }, true)); Children.Add(buttons);
        if (texting) { Texting(buddy, options, owner, say); return; }

        Children.Add(Pastel.Text("둘만의 페이지 프롬프트", 14));
        Children.Add(Pastel.Text("두 캐릭터가 나 없이 이야기하는 '둘만의 페이지'에 쓰입니다. 넣을 수 있는 말: {first_name} · {second_name} · {length} · {turns}(주고받는 횟수 — 길이에 따라 2~8). 두 사람의 프로필과 관계 설정은 앞에 항상 붙어요.", 11));
        var pairPrompt = new TextBox { Text = options.PairPrompt, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 200, MaxLength = 10000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13 }; Children.Add(pairPrompt);
        var pairButtons = new WrapPanel();
        pairButtons.Children.Add(Pastel.Button("둘만의 프롬프트 저장", () => { options.PairPrompt = pairPrompt.Text; buddy.Stage.Save(); pairPrompt.Text = options.PairPrompt; say("둘만의 페이지 프롬프트를 저장했어요 ♡"); }));
        pairButtons.Children.Add(Pastel.Button("기본으로", () => { pairPrompt.Text = StageTalkOptions.StandardPairPrompt; say("'둘만의 프롬프트 저장'을 눌러야 적용돼요."); }, true)); Children.Add(pairButtons);
    }
}
