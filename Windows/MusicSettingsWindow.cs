using System;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class MusicSettingsWindow : Window
{
    public MusicSettingsWindow(Buddy buddy)
    {
        var option = buddy.Pref.Music; var p = new StackPanel { Margin = new Thickness(22) }; p.Children.Add(Pastel.Text("함께 듣는 작은 음악 ♫", 23));
        CheckBox Field(string label, bool value) { var box = new CheckBox { Content = label, IsChecked = value }; p.Children.Add(box); return box; }
        var enabled = Field("Windows 브라우저 음악 자동 감지", option.Enabled);
        var visible = Field("머리 위 뮤직플레이어 표시", option.PlayerVisible);
        var sprites = Field("재생 중 음악 전용 캐릭터 이미지 사용", option.Sprites);
        var talk = Field("두 친구가 가끔 노래에 대해 대화 (제목·가수를 AI에 전달)", option.AiConversation);
        var browser = Field("사이트가 확인되지 않아도 브라우저 미디어 허용", option.AllowBrowserMedia); var locked = Field("플레이어 위치 고정", option.Locked); var follow = Field("플레이어를 대표 친구 머리 위로 따라가기", option.FollowHead);
        p.Children.Add(Pastel.Text("YouTube Music의 Windows 미디어 세션을 읽어 제목·가수·재생 상태를 표시합니다. Windows는 브라우저의 사이트 주소를 제공하지 않으므로 다른 탭을 사용 중이면 ‘브라우저 미디어’로 표시할 수 있습니다. 정확한 사이트만 원하면 브라우저 미디어 허용을 끄세요. Chrome/Edge가 미디어 세션을 제공해야 하며 다음/이전 곡 지원은 웹사이트 상태에 따릅니다.", 12));
        p.Children.Add(Pastel.Card(Pastel.Text("현재 감지: " + buddy.Music.Status + "\n" + buddy.Music.Song.Title + " · " + buddy.Music.Song.Artist), true));
        p.Children.Add(Pastel.Text("플레이어는 직접 드래그할 수 있고 ◆로 고정합니다. 음악 전용 이미지 4개는 캐릭터 이미지 → 음악 듣기 탭에서 등록합니다. 노래 대화는 최소 30분 간격·상호작용 중 일부에만 사용하여 음악에만 매몰되지 않게 합니다.", 12));
        var status = Pastel.Text(""); p.Children.Add(status); p.Children.Add(Pastel.Button("저장하고 닫기", async () =>
        {
            try
            {
                var pref = buddy.Pref.Snapshot();
                pref.Music = option with
                {
                    Enabled = enabled.IsChecked == true,
                    PlayerVisible = visible.IsChecked == true,
                    Sprites = sprites.IsChecked == true,
                    AiConversation = talk.IsChecked == true,
                    AllowBrowserMedia = browser.IsChecked == true,
                    Locked = locked.IsChecked == true,
                    FollowHead = follow.IsChecked == true
                };
                buddy.Apply(pref, configureKeys: false, persist: false);
                await Storage.SaveAsync(buddy.Pref);
                Close();
            }
            catch (Exception e) { status.Text = e.Message; }
        }));
        Pastel.Window(this, "음악 감지 · 뮤직플레이어", Pastel.Scroll(p), 740, 680);
    }
}
