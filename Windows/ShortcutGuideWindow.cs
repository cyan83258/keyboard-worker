using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class ShortcutGuideWindow : Window
{
    readonly Buddy buddy;
    readonly StackPanel entries = new() { Margin = new Thickness(20) };
    static readonly (string Action, string Title, string Description)[] Actions =
    {
        ("quick", "한마디 입력", "캐릭터 근처에 작은 입력창을 엽니다. 짧은 메시지를 보내면 말풍선으로 답장을 받습니다."),
        ("visibility", "캐릭터 숨기기 / 보이기", "누를 때마다 캐릭터를 숨기거나 다시 표시합니다. 프로그램은 종료되지 않습니다. 앱 예외·전체 화면 자동 숨김은 그대로 적용됩니다."),
        ("rest", "휴식 시작 / 중단", "쉬고 있지 않으면 지정한 시간만큼 휴식을 시작하고, 쉬는 중이면 중단합니다. 휴식 중에는 재촉과 작업 시간 측정을 멈춥니다."),
        ("settings", "설정 열기", "설정 창을 엽니다. 이미 열려 있으면 해당 창을 앞으로 가져옵니다."),
        ("quiet", "조용한 작업 모드 켜기 / 끄기", "소리·몸 흔들림·자동 재촉을 잠시 멈추거나 원래 설정으로 돌아갑니다. 손 반응·시간 측정·경험치는 유지됩니다.")
    };
    public ShortcutGuideWindow(Buddy buddy)
    {
        this.buddy = buddy; var root = new DockPanel();
        var footer = new WrapPanel { Margin = new Thickness(16, 4, 16, 16) };
        footer.Children.Add(Pastel.Button("단축키 변경", buddy.OpenDesktopOptions, true));
        footer.Children.Add(Pastel.Button("설명서 복사", () => Clipboard.SetText(BuildText())));
        footer.Children.Add(Pastel.Button("닫기", Close));
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(Pastel.Scroll(entries));
        Pastel.Window(this, "단축키 안내", root, 660, 770); ShowInTaskbar = false;
        Activated += (_, _) => Refresh(); Refresh();
    }
    void Refresh()
    {
        entries.Children.Clear(); entries.Children.Add(Pastel.Text("작은 친구를 부르는 방법 ♡", 23));
        entries.Children.Add(Pastel.Text("아래는 지금 저장된 단축키입니다. 키 조합은 동시에 누르세요. 다른 프로그램을 사용 중일 때도 작동합니다."));
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var item in Actions)
        {
            string combination = buddy.Pref.Desktop.Shortcuts.GetValueOrDefault(item.Action, "");
            var card = new StackPanel(); card.Children.Add(Pastel.Text(item.Title, 18));
            card.Children.Add(Pastel.Text(string.IsNullOrWhiteSpace(combination) ? "단축키 꺼짐" : combination + (buddy.ShortcutRegistered(item.Action) ? "  · 사용 중" : "  · 현재 사용 불가"), 16));
            card.Children.Add(Pastel.Text(item.Description, 13));
            if (item.Action == "rest") card.Children.Add(Pastel.Text($"현재 휴식 시간: {buddy.Pref.Desktop.QuickRestMinutes}분", 12));
            var tile = Pastel.Card(card, item.Action is "visibility" or "settings"); tile.Padding = new Thickness(12); tile.Margin = new Thickness(4); cards.Children.Add(tile);
        }
        entries.Children.Add(cards);
        entries.Children.Add(Pastel.Card(Pastel.Text("입력창 안에서\nEnter: 메시지 전송\nShift+Enter: 줄바꿈\nEsc: 한마디 입력창 닫기 (생성 중이면 요청도 중단)\n\n마우스로\n캐릭터 더블클릭: 홈 열기 (오늘 · 친구 · 수집 · 상점)\n♡ 한마디 버튼: 짧은 메시지 입력\n우클릭: 한마디 · 홈 · 빠른 설정 · 숨기기/휴식 · 설정\nCtrl + 마우스 휠 (캐릭터 위에서): 캐릭터 크기 키우기 / 줄이기\n말풍선 드래그: 말풍선 위치 옮기기", 13)));
        entries.Children.Add(Pastel.Text("변경 위치: 데스크톱 설정 → 대화·단축키\n빈칸은 해당 단축키를 끕니다. 같은 조합을 중복 지정하거나 다른 앱과 충돌하면 저장 시 안내합니다. ‘현재 사용 불가’가 보이면 다른 조합으로 저장해 주세요.", 12));
    }
    string BuildText()
    {
        var result = new StringBuilder("Keyboard Worker · 현재 단축키\n\n");
        foreach (var item in Actions)
        {
            string value = buddy.Pref.Desktop.Shortcuts.GetValueOrDefault(item.Action, "");
            result.AppendLine(item.Title + ": " + (string.IsNullOrWhiteSpace(value) ? "꺼짐" : value)); result.AppendLine(item.Description);
            if (item.Action == "rest") result.AppendLine("휴식 시간: " + buddy.Pref.Desktop.QuickRestMinutes + "분"); result.AppendLine();
        }
        result.AppendLine("Enter: 전송 / Shift+Enter: 줄바꿈 / Esc: 한마디 입력창 닫기");
        result.AppendLine("Ctrl + 마우스 휠 (캐릭터 위에서): 캐릭터 크기 조절 / 말풍선 드래그: 위치 옮기기");
        result.AppendLine("변경: 데스크톱 설정 → 대화·단축키"); return result.ToString();
    }
}
