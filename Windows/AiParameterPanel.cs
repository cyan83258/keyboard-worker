using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// The finer parameters of an AI connection, shown inside a fold in the connections window.
// Show puts a connection's values into the boxes; Read takes them out again (and says which box is wrong).
public sealed class AiParameterPanel : StackPanel
{
    sealed record Number(string Label, string Hint, double Low, double High, bool Whole, Func<AiParameters, double?> Get, Action<AiParameters, double?> Set);
    static readonly Number[] numbers =
    {
        new("온도", "높을수록 자유롭고 엉뚱하게, 낮을수록 차분하고 일정하게 (0~2)", 0, 2, false, p => p.Temperature, (p, v) => p.Temperature = v),
        new("상위 확률 합 (Top P)", "확률이 높은 낱말부터 이 합에 들 때까지만 고릅니다 (0~1)", 0, 1, false, p => p.TopP, (p, v) => p.TopP = v),
        new("상위 후보 수 (Top K)", "확률이 높은 낱말 몇 개 중에서만 고릅니다 (1~1000)", 1, 1000, true, p => p.TopK, (p, v) => p.TopK = v is double k ? (int)k : null),
        new("빈도 페널티", "이미 여러 번 쓴 낱말을 덜 쓰게 합니다 (-2~2)", -2, 2, false, p => p.FrequencyPenalty, (p, v) => p.FrequencyPenalty = v),
        new("존재 페널티", "한 번이라도 나온 낱말을 덜 쓰게 해 새 화제로 가게 합니다 (-2~2)", -2, 2, false, p => p.PresencePenalty, (p, v) => p.PresencePenalty = v),
        new("반복 페널티", "같은 표현의 반복을 줄입니다. 1이 기본 (0~2)", 0, 2, false, p => p.RepetitionPenalty, (p, v) => p.RepetitionPenalty = v),
        new("최소 확률 (Min P)", "가장 유력한 낱말에 견주어 이 비율보다 낮은 낱말은 버립니다 (0~1)", 0, 1, false, p => p.MinP, (p, v) => p.MinP = v),
        new("상위 A (Top A)", "유력한 낱말이 뚜렷할수록 후보를 더 좁힙니다 (0~1)", 0, 1, false, p => p.TopA, (p, v) => p.TopA = v)
    };
    readonly TextBox[] boxes = new TextBox[numbers.Length];
    readonly ComboBox effort = new() { ItemsSource = AiParameters.Efforts.Select(e => e.Name).ToArray() }, verbosity = new() { ItemsSource = AiParameters.Verbosities.Select(v => v.Name).ToArray() };
    readonly TextBox includeBody = Area(90, "{ \"seed\": 7, \"provider\": { \"order\": [\"...\"] } }"), excludeBody = Area(46, "temperature, generationConfig.thinkingConfig"), headers = Area(60, "HTTP-Referer: https://example.com");
    static TextBox Area(double height, string example) => new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = height, MaxLength = 4000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 12.5, ToolTip = "예: " + example };
    public AiParameterPanel()
    {
        Margin = new Thickness(10);
        Children.Add(Pastel.Text("비워 둔 칸은 보내지 않아서 서버의 기본값이 쓰입니다. 연결(모델)마다 따로 저장돼요. 서버가 모르는 파라미터를 보내면 오류가 날 수 있으니, 바꾼 뒤에는 '연결 테스트'로 확인해 보세요.", 12));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        for (int i = 0; i < numbers.Length; i++)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 4, 12, 4) }; cell.Children.Add(new TextBlock { Text = numbers[i].Label, Foreground = Pastel.Ink, FontSize = 13 });
            boxes[i] = new TextBox { MaxLength = 8, ToolTip = numbers[i].Hint }; cell.Children.Add(boxes[i]);
            cell.Children.Add(new TextBlock { Text = numbers[i].Hint, Foreground = Pastel.Ink, FontSize = 11, Opacity = .8, TextWrapping = TextWrapping.Wrap }); grid.Children.Add(cell);
        }
        Children.Add(grid);
        void Choice(string title, ComboBox box, string hint) { Children.Add(Pastel.Text(title, 13)); Children.Add(box); Children.Add(Pastel.Text(hint, 11)); }
        Choice("추론 강도", effort, "답하기 전에 얼마나 깊이 생각할지. 생각하는 모델에서만 쓰이고, 높을수록 느리고 토큰을 더 씁니다. 모델에 따라 받지 않는 값이 있어요.");
        Choice("답변 상세도", verbosity, "같은 내용을 얼마나 길게 풀어 쓸지. 이 설정을 아는 모델(GPT-5 계열 등)에서만 쓰입니다.");
        void Text(string title, TextBox box, string hint) { Children.Add(Pastel.Text(title, 13)); Children.Add(box); Children.Add(Pastel.Text(hint, 11)); }
        Text("본문에 추가할 파라미터 (JSON)", includeBody, "요청 본문에 그대로 합쳐집니다. { } 로 감싼 JSON 객체로 적어 주세요. 위 설정과 같은 이름이면 여기 적은 값이 이깁니다.");
        Text("본문에서 뺄 파라미터", excludeBody, "쉼표나 줄바꿈으로 나눠 이름을 적습니다. 안쪽 항목은 점으로 적어요 (예: generationConfig.thinkingConfig).");
        Text("추가 요청 헤더", headers, "한 줄에 하나씩 '이름: 값'. API 키 헤더는 위의 인증 설정이 붙이므로 여기 적지 않아도 됩니다.");
    }
    static string Shown(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
    public void Show(AiParameters p)
    {
        for (int i = 0; i < numbers.Length; i++) boxes[i].Text = Shown(numbers[i].Get(p));
        effort.SelectedIndex = Math.Max(0, Array.FindIndex(AiParameters.Efforts, e => e.Key == p.ReasoningEffort)); verbosity.SelectedIndex = Math.Max(0, Array.FindIndex(AiParameters.Verbosities, v => v.Key == p.Verbosity));
        includeBody.Text = p.IncludeBody; excludeBody.Text = p.ExcludeBody; headers.Text = p.IncludeHeaders;
    }
    // True when anything is set, so the fold can open by itself.
    public static bool Any(AiParameters p) => numbers.Any(n => n.Get(p) != null) || p.ReasoningEffort.Length > 0 || p.Verbosity.Length > 0 || p.IncludeBody.Length > 0 || p.ExcludeBody.Length > 0 || p.IncludeHeaders.Length > 0;
    public AiParameters Read()
    {
        var p = new AiParameters();
        for (int i = 0; i < numbers.Length; i++)
        {
            string text = boxes[i].Text.Trim(); var number = numbers[i]; if (text.Length == 0) continue;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value < number.Low || value > number.High || (number.Whole && value != Math.Floor(value)))
                throw new InvalidOperationException($"'{number.Label}'은(는) {number.Low:0.##}~{number.High:0.##} 사이의 {(number.Whole ? "정수" : "숫자")}로 적거나 비워 주세요.");
            number.Set(p, value);
        }
        p.ReasoningEffort = AiParameters.Efforts[Math.Max(0, effort.SelectedIndex)].Key; p.Verbosity = AiParameters.Verbosities[Math.Max(0, verbosity.SelectedIndex)].Key;
        p.IncludeBody = includeBody.Text; p.ExcludeBody = excludeBody.Text; p.IncludeHeaders = headers.Text; p.Normalize(); p.Extra();   // Extra() refuses JSON that is not an object
        return p;
    }
}
