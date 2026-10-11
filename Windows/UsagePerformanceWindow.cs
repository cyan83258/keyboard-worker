using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class UsagePerformanceWindow : Window
{
    public UsagePerformanceWindow(Buddy buddy)
    {
        var p = new StackPanel { Margin = new Thickness(22) }; p.Children.Add(Pastel.Text("가볍게 함께 쓰는 AI ♡", 23)); var day = AiUsage.Current.Snapshot();
        p.Children.Add(Pastel.Card(Pastel.Text($"오늘 요청 {day.Calls}회 · 자동 요청 {day.AutomaticCalls}회\n토큰 {day.Tokens:N0} · 예약 {day.Reserved:N0} · 진행 {day.Pending}회\n실패/중단 {day.Failed}회 · 서버 사용량 미제공 추정 {day.Estimated}회\n" + string.Join(" · ", day.Kinds.Select(k => AiUsage.Label(k.Key) + " " + k.Value)), 14), true));
        var budget = buddy.Chat.Preferences.Budget; var limit = new CheckBox { Content = "하루 AI 사용 상한 적용", IsChecked = budget.Enabled }; p.Children.Add(limit);
        TextBox Number(string name, long value) { p.Children.Add(Pastel.Text(name)); var b = new TextBox { Text = value.ToString() }; p.Children.Add(b); return b; }
        var calls = Number("전체 요청 최대 수 (1~10000회)", budget.Calls); var automatic = Number("자동 요청 최대 수 (1~10000회)", budget.AutomaticCalls); var tokens = Number("토큰 상한 (1000~10000000)", budget.Tokens);
        p.Children.Add(Pastel.Text("날짜가 바뀌면 하루 예산이 새로 시작합니다. 실제 서버 사용량을 반영하며 미제공 응답은 출력 예약 예산으로 추정합니다. 요청 전 출력 예산을 예약해 동시 요청이 한도를 넘지 않게 합니다. 입력 토큰·서버 집계 차이는 응답 후 반영하므로 토큰 값은 API 제공사 청구 화면과 다를 수 있습니다.", 12));
        var low = new CheckBox { Content = "저전력 모드 · 하트 15fps / 적은 효과", IsChecked = buddy.Pref.LowPower }; p.Children.Add(low);
        var gameLight = new CheckBox { Content = "게임·전체 화면 프로그램 사용 중에는 가볍게 동작", IsChecked = buddy.Pref.Awareness.GameLight }; p.Children.Add(gameLight);
        p.Children.Add(Pastel.Text("게임이나 전체 화면 프로그램이 맨 앞에 있는 동안 타이핑 소리·흔들림·하트·플레이어 장식을 쉬고, 손 반응을 줄이고, 이 프로그램의 우선순위를 낮춰 다른 프로그램에 자원을 양보합니다. 업무 프로그램을 전체 화면으로 쓸 때는 평소대로 동작해요.", 12));
        var gameHide = new CheckBox { Content = "게임을 하는 동안에는 캐릭터를 아예 숨기기", IsChecked = buddy.Pref.Awareness.GameHide }; p.Children.Add(gameHide);
        p.Children.Add(Pastel.Text("가볍게 동작해도 게임이 버벅이면 켜 보세요. 화면 위에 떠 있는 창 자체가 게임의 전체 화면 최적화를 방해할 수 있습니다. 게임에서 나오면 다시 나타나요.", 12));
        // Measured use of this program itself, to judge whether it is the cause of a slowdown.
        var measured = Pastel.Text(buddy.Meter.Summary + "\nGPU 측정 중…", 12);
        string gpu = "GPU 측정 중…";
        var meterCard = new StackPanel();
        meterCard.Children.Add(Pastel.Text("이 프로그램이 쓰는 자원 (실측)", 15));
        meterCard.Children.Add(measured);
        meterCard.Children.Add(Pastel.Text("CPU는 작업 관리자와 같은 기준(전체 코어 대비)입니다. 게임을 켠 채로 몇 분 쓴 뒤 이 창을 다시 열면 평소와 게임 중 평균을 비교할 수 있어요.", 12)); p.Children.Add(Pastel.Card(meterCard, true));
        var meterWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        meterWatch.Tick += (_, _) => measured.Text = buddy.Meter.Summary + "\n" + gpu;
        meterWatch.Start();
        Closed += (_, _) => meterWatch.Stop();
        _ = System.Threading.Tasks.Task.Run(PerfMeter.Gpu).ContinueWith(t => Dispatcher.BeginInvoke(() => gpu = t.IsCompletedSuccessfully && t.Result is double share ? $"GPU {share:0.0}% (창을 연 순간 1초 측정)" : "GPU는 이 PC에서 측정할 수 없어요"));
        var pace = new CheckBox { Content = "자동 AI 요청을 하루에 고르게 나눠 쓰기", IsChecked = budget.Pace }; p.Children.Add(pace);
        p.Children.Add(Pastel.Text($"켜면 자동 요청(재촉·랜덤·상호작용·상황 한마디·이야기)이 아침에 몰려 일찍 바닥나지 않게, 시간이 지난 만큼만 씁니다. 직접 보내는 채팅은 제한하지 않아요. 지금: 오늘 자동 {day.AutomaticCalls}회 사용 · 지금까지 허용 {AiUsage.Allowance(budget, DateTime.Now)}회 · 하루 {budget.AutomaticCalls}회.", 12));
        var batch = new CheckBox { Content = "자동 대사 묶음 생성·재사용", IsChecked = buddy.Chat.Preferences.Automatic.Batch };
        p.Children.Add(batch);
        var batchCount = Number("묶음 대사 수 (2~8개)", buddy.Chat.Preferences.Automatic.BatchSize);
        p.Children.Add(Pastel.Text("묶음은 같은 프로필·프롬프트의 재촉과 랜덤 한마디에만 사용하며, 설정이 바뀌면 버립니다. 남은 대사는 메모리에서만 보관합니다. 두 캐릭터의 동일 이미지는 공유 캐시로 재사용합니다.", 12));
        var starts = StartupTrace.Recent(3);
        p.Children.Add(Pastel.Card(Pastel.Text("최근 시작 속도 (단계별 ms)\n" + (starts.Length == 0 ? "아직 측정한 기록이 없어요. 다음 시작부터 기록됩니다." : string.Join("\n", starts)) + "\n전체 기록: 설정 폴더의 startup-timing.txt (최근 30회)", 12)));
        var status = Pastel.Text(""); p.Children.Add(status);
        p.Children.Add(Pastel.Button("저장하고 닫기", async () =>
        {
            try
            {
                int Value(TextBox box, int min, int max) => int.TryParse(box.Text, out int n) && n >= min && n <= max ? n : throw new InvalidOperationException("숫자를 범위 안에 입력해 주세요.");
                var next = buddy.Chat.Preferences.Snapshot();
                next.Budget = new() { Enabled = limit.IsChecked == true, Calls = Value(calls, 1, 10000), AutomaticCalls = Value(automatic, 1, 10000), Tokens = Value(tokens, 1000, 10000000), Pace = pace.IsChecked == true };
                next.Automatic = next.Automatic with { Batch = batch.IsChecked == true, BatchSize = Value(batchCount, 2, 8) }; buddy.CancelAutomatic(); await buddy.Chat.ApplyAsync(next);
                var pref = buddy.Pref.Snapshot();
                pref.LowPower = low.IsChecked == true;
                pref.Awareness = pref.Awareness with { GameLight = gameLight.IsChecked == true, GameHide = gameHide.IsChecked == true };
                buddy.Apply(pref, configureKeys: false, persist: false);
                await Storage.SaveAsync(pref);
                Close();
            }
            catch (Exception e) { status.Text = e.Message; }
        }));
        Pastel.Window(this, "AI 사용량 · 성능", Pastel.Scroll(p), 730, 790);
    }
}
