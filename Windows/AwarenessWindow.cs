using System;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class AwarenessWindow : Window
{
    public AwarenessWindow(Buddy buddy)
    {
        var options = buddy.Pref.Awareness; var p = new StackPanel { Margin = new Thickness(22) }; p.Children.Add(Pastel.Text("지금 뭐 하고 있는지 알아보기", 23));
        p.Children.Add(Pastel.Text("프로그램 이름과 영상·노래 제목만 사용합니다. 화면 내용이나 입력한 글은 읽지 않아요. AI를 쓰는 대사와 채팅에서는 이 이름과 제목이 선택한 AI 제공사로 전달됩니다.", 12));

        var programs = new StackPanel(); programs.Children.Add(Pastel.Text("지금 쓰는 프로그램", 16));
        var apps = new CheckBox { Content = "맨 앞에서 쓰고 있는 프로그램을 알아보고 대화에서 언급하기", IsChecked = options.Apps }; programs.Children.Add(apps);
        var comments = new CheckBox { Content = "게임이면 핀잔, 업무·창작 프로그램이면 칭찬을 먼저 건네기", IsChecked = options.Comments }; programs.Children.Add(comments);
        programs.Children.Add(Pastel.Text("같은 프로그램에 다시 말하기까지 (5~720분)")); var minutes = new TextBox { Text = options.CommentMinutes.ToString() }; programs.Children.Add(minutes);
        programs.Children.Add(Pastel.Text("게임으로 볼 프로그램 이름 (쉼표로 구분 · 예: mygame.exe, launcher)")); var games = new TextBox { Text = options.Games, MaxLength = 2000 }; programs.Children.Add(games);
        programs.Children.Add(Pastel.Text("업무·창작으로 볼 프로그램 이름 (쉼표로 구분)")); var work = new TextBox { Text = options.Work, MaxLength = 2000 }; programs.Children.Add(work);
        programs.Children.Add(Pastel.Text("자주 쓰는 게임과 업무 프로그램은 미리 알고 있고, Steam·Epic·Riot 등 게임 폴더에 설치된 프로그램은 게임으로 봅니다. 잘못 알아보는 프로그램만 위에 적어 주세요. 백그라운드에서 돌아가는 프로그램은 보지 않아요.", 12));
        programs.Children.Add(Pastel.Text("지금 알아본 프로그램: " + buddy.AppSummary, 12));
        programs.Children.Add(Pastel.Text("AI 자동 대사(재촉 또는 랜덤 한마디)가 켜져 있으면 캐릭터 말투로 새 대사를 만들고, 꺼져 있으면 준비된 대사를 씁니다.", 12)); p.Children.Add(Pastel.Card(programs));

        var playing = new StackPanel(); playing.Children.Add(Pastel.Text("지금 보는 영상 · 듣는 노래", 16));
        var media = new CheckBox { Content = "유튜브 영상·유튜브 뮤직 노래 제목을 대화에 알려주기", IsChecked = options.Media }; playing.Children.Add(media);
        playing.Children.Add(Pastel.Text("켜 두면 채팅할 때 지금 재생 중인 제목을 캐릭터가 알고 이야기하며, 새 영상이나 노래가 시작되면 가끔 먼저 말을 겁니다. 한마디 보내기 창의 체크박스로 메시지마다 따로 정할 수도 있어요.", 12));
        var song = buddy.Music.Song;
        playing.Children.Add(Pastel.Text(buddy.Pref.Music.Enabled ? "지금 감지: " + (song.Available ? MusicMonitor.Kind(song) + " · " + song.Title : "재생 중인 브라우저 미디어 없음") : "계속 알아보려면 음악 감지를 켜야 해요. 꺼져 있어도 한마디 보내기의 체크박스는 그 순간 한 번 확인합니다.", 12));
        playing.Children.Add(Pastel.Button("음악 감지 · 뮤직플레이어 설정 열기", buddy.OpenMusicSettings, true)); p.Children.Add(Pastel.Card(playing, true));

        var time = new StackPanel(); time.Children.Add(Pastel.Text("시간", 16));
        var clock = new CheckBox { Content = "지금 시각과 요일을 알려주기 (아침 인사, 늦은 밤 걱정 같은 대사)", IsChecked = options.Clock }; time.Children.Add(clock);
        time.Children.Add(Pastel.Text("프로그램을 오늘 얼마나 썼는지도 함께 알려서, 오래 하면 벌써 2시간째라는 식의 말을 할 수 있어요. 사용 시간 기록은 이 PC에만 저장됩니다.", 12)); p.Children.Add(Pastel.Card(time));
        var status = Pastel.Text(""); p.Children.Add(status);
        p.Children.Add(Pastel.Button("저장하고 닫기", async () =>
        {
            try
            {
                if (!int.TryParse(minutes.Text, out int gap) || gap < 5 || gap > 720) throw new InvalidOperationException("다시 말하기까지의 시간은 5~720분으로 입력해 주세요.");
                var next = buddy.Pref.Snapshot();
                next.Awareness = next.Awareness with
                {
                    Apps = apps.IsChecked == true,
                    Comments = comments.IsChecked == true,
                    CommentMinutes = gap,
                    Games = games.Text,
                    Work = work.Text,
                    Media = media.IsChecked == true,
                    Clock = clock.IsChecked == true
                };
                buddy.Apply(next, configureKeys: false, persist: false); await Storage.SaveAsync(buddy.Pref); Close();
            }
            catch (Exception e) { status.Text = e.Message; }
        }));
        Pastel.Window(this, "주변 알아보기", Pastel.Scroll(p), 760, 800);
    }
}
