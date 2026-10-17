using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// The novel's AI settings, laid out like SillyTavern's: the connection and model, how much is read and written,
// the samplers as sliders, how the prompt is shaped, reasoning, the prefill, and (folded) what is added to the request.
// Explanations sit behind "?" marks. Nothing here touches the chat or the character pages.
public sealed class NovelApiWindow : Window
{
    const string Same = "대화에 쓰는 AI 그대로";
    // A sampler: label, slider and number. An empty number is not sent (the server's default applies).
    sealed class Knob : DockPanel
    {
        readonly Slider slider; readonly TextBox box = new() { Width = 66, MaxLength = 9, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 3, 6, 3) };
        readonly string label; readonly double low, high; readonly bool whole; bool moving;
        public Knob(string label, double low, double high, double step, double? value, double start, bool whole = false, string help = "")
        {
            this.label = label; this.low = low; this.high = high; this.whole = whole; Margin = new Thickness(0, 2, 0, 2);
            var name = new StackPanel { Orientation = Orientation.Horizontal, Width = 132, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock { Text = label, Foreground = Pastel.Ink, FontSize = 13 }); if (help.Length > 0) { var mark = Pastel.Help(help); mark.Margin = new Thickness(5, 0, 0, 0); mark.VerticalAlignment = VerticalAlignment.Center; name.Children.Add(mark); }
            DockPanel.SetDock(name, Dock.Left); Children.Add(name); DockPanel.SetDock(box, Dock.Right); Children.Add(box);
            slider = new Slider { Minimum = low, Maximum = high, SmallChange = step, LargeChange = step * 10, TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }; Children.Add(slider);
            box.ToolTip = "비워 두면 보내지 않아요 (서버 기본값)"; box.Text = value is double v ? Show(v) : ""; slider.Value = value ?? start; slider.Opacity = value == null ? .4 : 1;
            slider.ValueChanged += (_, _) => { if (moving) return; moving = true; box.Text = Show(slider.Value); slider.Opacity = 1; moving = false; };
            box.TextChanged += (_, _) => { if (moving) return; moving = true; if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double typed)) slider.Value = Math.Clamp(typed, low, high); slider.Opacity = box.Text.Trim().Length == 0 ? .4 : 1; moving = false; };
        }
        string Show(double v) => whole ? ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);
        public double? Value
        {
            get
            {
                string text = box.Text.Trim(); if (text.Length == 0) return null;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < low || v > high || (whole && v != Math.Floor(v))) throw new InvalidOperationException($"'{label}'은(는) {low:0.##}~{high:0.##} 사이의 {(whole ? "정수" : "숫자")}로 적거나 비워 주세요.");
                return v;
            }
        }
    }
    static TextBlock Section(string text) => new() { Text = text, Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 14, 0, 4) };
    static StackPanel Labelled(string text, UIElement control, string help = "")
    {
        var row = new StackPanel { Margin = new Thickness(0, 3, 0, 3) }; var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
        head.Children.Add(new TextBlock { Text = text, Foreground = Pastel.Ink, FontWeight = FontWeights.SemiBold, FontSize = 13 }); if (help.Length > 0) { var mark = Pastel.Help(help); mark.Margin = new Thickness(5, 0, 0, 0); head.Children.Add(mark); }
        row.Children.Add(head); row.Children.Add(control); return row;
    }
    static CheckBox Check(string text, bool on, string help = "")
    {
        var box = new CheckBox { IsChecked = on, Margin = new Thickness(0, 3, 0, 3) }; var words = new StackPanel { Orientation = Orientation.Horizontal };
        words.Children.Add(new TextBlock { Text = text, Foreground = Pastel.Ink, FontSize = 13 }); if (help.Length > 0) { var mark = Pastel.Help(help); mark.Margin = new Thickness(5, 0, 0, 0); words.Children.Add(mark); }
        box.Content = words; return box;
    }
    public NovelApiWindow(Buddy buddy)
    {
        var book = buddy.Novel.State; var p = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; var status = Pastel.Text("", 12); var root = new DockPanel { Margin = new Thickness(20, 14, 20, 16) };
        var connections = buddy.Chat.Preferences.Connections; var parameters = book.Parameters;

        // ---- connection ----
        p.Children.Add(Section("연결"));
        var connection = new ComboBox { ItemsSource = new[] { Same }.Concat(connections.Select(c => c.Name)).ToArray(), MinWidth = 260 };
        connection.SelectedIndex = connections.FindIndex(c => string.Equals(c.Name, book.Connection, StringComparison.OrdinalIgnoreCase)) + 1;
        var manage = StageParts.Tool("관리", "AI 연결 만들기 · 고치기 (API 키 · 주소)", buddy.OpenConnections); DockPanel.SetDock(manage, Dock.Right);
        var pick = new DockPanel(); pick.Children.Add(manage); pick.Children.Add(connection); p.Children.Add(Labelled("연결 프로필", pick, "AI 연결 창에 저장한 연결 가운데 하나를 고릅니다. API 키와 주소는 그 연결의 것을 씁니다."));
        var about = new TextBlock { Foreground = Pastel.Ink, Opacity = .7, FontSize = 12, Margin = new Thickness(2, 0, 0, 2) }; p.Children.Add(about);
        var model = new TextBox { Padding = new Thickness(6, 4, 6, 4) }; p.Children.Add(Labelled("모델", model, "비워 두면 연결에 적힌 모델을 씁니다. 같은 연결로 소설만 다른 모델을 쓰고 싶을 때 모델 ID를 적어요."));
        AiConnection? Chosen() => connection.SelectedIndex <= 0 ? connections.ElementAtOrDefault(buddy.Chat.Preferences.Selected) : connections[connection.SelectedIndex - 1];
        void About() { var c = Chosen(); about.Text = c == null ? "" : $"{c.Format}  ·  {(Uri.TryCreate(c.Endpoint.Replace("{model}", "m"), UriKind.Absolute, out var u) ? u.Host : c.Endpoint)}  ·  연결의 모델: {(c.Model.Length > 0 ? c.Model : "(없음)")}"; }
        connection.SelectionChanged += (_, _) => About(); About(); model.Text = book.Model;

        // ---- how much ----
        p.Children.Add(Section("분량"));
        var context = new Knob("컨텍스트 크기", 2000, 1000000, 1000, book.MaxContext, book.MaxContext, true, "한 번에 보내는 최대 토큰 수. 넘으면 이야기의 오래된 부분부터 빠지고, 빠진 부분은 방의 기억 요약이 대신합니다.");
        var output = new Knob("최대 응답 길이", 256, 64000, 64, book.MaxOutput, book.MaxOutput, true, "한 부분이 쓸 수 있는 최대 토큰 수. 생각하는 모델은 그 일부를 생각에 씁니다.");
        p.Children.Add(context); p.Children.Add(output);
        var stream = Check("스트리밍", book.Stream, "쓰는 중인 글을 실시간으로 받아 보여 줍니다."); p.Children.Add(stream);

        // ---- samplers ----
        p.Children.Add(Section("샘플링"));
        var knobs = new (Knob Knob, Action<AiParameters, double?> Set)[]
        {
            (new Knob("온도", 0, 2, .01, parameters.Temperature, 1, help: "높을수록 자유롭고 엉뚱하게, 낮을수록 차분하고 일정하게"), (a, v) => a.Temperature = v),
            (new Knob("빈도 페널티", -2, 2, .01, parameters.FrequencyPenalty, 0, help: "여러 번 쓴 낱말을 덜 쓰게 합니다"), (a, v) => a.FrequencyPenalty = v),
            (new Knob("존재 페널티", -2, 2, .01, parameters.PresencePenalty, 0, help: "한 번이라도 나온 낱말을 덜 쓰게 합니다"), (a, v) => a.PresencePenalty = v),
            (new Knob("Top K", 1, 500, 1, parameters.TopK, 40, true, "확률이 높은 낱말 몇 개 가운데서만 고릅니다"), (a, v) => a.TopK = v is double k ? (int)k : null),
            (new Knob("Top P", 0, 1, .01, parameters.TopP, 1, help: "확률이 높은 낱말부터 이 합에 들 때까지만 고릅니다"), (a, v) => a.TopP = v),
            (new Knob("반복 페널티", 0, 2, .01, parameters.RepetitionPenalty, 1, help: "같은 표현의 반복을 줄입니다. 1이 기본"), (a, v) => a.RepetitionPenalty = v),
            (new Knob("Min P", 0, 1, .01, parameters.MinP, 0, help: "가장 유력한 낱말에 견줘 이 비율보다 낮은 낱말을 버립니다"), (a, v) => a.MinP = v),
            (new Knob("Top A", 0, 1, .01, parameters.TopA, 0, help: "유력한 낱말이 뚜렷할수록 후보를 좁힙니다"), (a, v) => a.TopA = v)
        };
        foreach (var (knob, _) in knobs) p.Children.Add(knob);
        p.Children.Add(new TextBlock { Text = "숫자 칸을 비우면 그 값은 보내지 않아요.", Foreground = Pastel.Ink, Opacity = .6, FontSize = 11.5, Margin = new Thickness(2, 2, 0, 0) });

        // ---- the prompt ----
        p.Children.Add(Section("프롬프트 처리"));
        var system = Check("시스템 프롬프트 사용", book.SystemPrompt, "끄면 system 역할의 메시지를 user 역할로 보냅니다. 시스템 프롬프트를 받지 않는 모델에 씁니다.");
        var squash = Check("시스템 메시지 합치기", book.Squash, "연달아 오는 system 메시지를 하나로 합쳐 보냅니다.");
        var merge = Check("같은 역할 연달아 오면 합치기", book.MergeRoles, "어떤 역할이든 같은 역할의 메시지가 연달아 오면 하나로 합칩니다 (SillyTavern의 Merge consecutive roles).");
        p.Children.Add(system); p.Children.Add(squash); p.Children.Add(merge);
        var prefill = new TextBox { Text = book.Prompts.Prefill, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 54, MaxLength = NovelPrompts.MaxContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 12.5, Padding = new Thickness(6, 4, 6, 4) };
        p.Children.Add(Labelled("답변 첫머리 (Assistant Prefill)", prefill, "AI 답변의 첫머리로 넣어 두면 그 뒤를 이어 씁니다. 비워 두면 쓰지 않아요. 프롬프트 프리셋과 함께 저장됩니다."));

        // ---- reasoning ----
        p.Children.Add(Section("추론"));
        var thinking = Check("모델의 추론 받기", book.ShowThinking, "AI가 쓰기 전에 생각한 내용을 받아 두고 '추론' 버튼으로 펼쳐 봅니다. 보여 주는 것만 달라지며, 생각을 보내 주는 모델(Gemini, DeepSeek 등)에서만 됩니다."); p.Children.Add(thinking);
        var effort = new ComboBox { ItemsSource = AiParameters.Efforts.Select(e => e.Name).ToArray(), MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left }; effort.SelectedIndex = Math.Max(0, Array.FindIndex(AiParameters.Efforts, e => e.Key == parameters.ReasoningEffort));
        var verbosity = new ComboBox { ItemsSource = AiParameters.Verbosities.Select(v => v.Name).ToArray(), MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left }; verbosity.SelectedIndex = Math.Max(0, Array.FindIndex(AiParameters.Verbosities, v => v.Key == parameters.Verbosity));
        var pair = new WrapPanel(); var left = Labelled("추론 강도", effort, "답하기 전에 얼마나 깊이 생각할지. 높을수록 느리고 토큰을 더 씁니다."); left.Margin = new Thickness(0, 3, 24, 3); pair.Children.Add(left); pair.Children.Add(Labelled("답변 상세도", verbosity, "같은 내용을 얼마나 길게 풀어 쓸지 (GPT-5 계열 등)")); p.Children.Add(pair);

        // ---- added by hand, folded ----
        TextBox Area(string text, double height) => new() { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = height, MaxLength = 4000, FontSize = 12.5, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6, 4, 6, 4) };
        var include = Area(parameters.IncludeBody, 64); var exclude = Area(parameters.ExcludeBody, 40); var headers = Area(parameters.IncludeHeaders, 44);
        var extra = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        extra.Children.Add(Labelled("본문에 추가할 파라미터 (JSON)", include, "요청 본문에 그대로 합쳐집니다. { } 로 감싼 JSON 객체로 적어요. 위 설정과 겹치면 여기 적은 값이 이깁니다."));
        extra.Children.Add(Labelled("본문에서 뺄 파라미터", exclude, "쉼표나 줄바꿈으로 나눠 이름을 적습니다. 안쪽 항목은 점으로 (예: generationConfig.thinkingConfig)."));
        extra.Children.Add(Labelled("추가 요청 헤더", headers, "한 줄에 하나씩 '이름: 값'. API 키는 연결이 붙이니 적지 않아도 됩니다."));
        var fold = StageParts.Fold("고급 · 요청에 직접 더하기", extra, parameters.IncludeBody.Length + parameters.ExcludeBody.Length + parameters.IncludeHeaders.Length == 0, _ => { }); fold.FontSize = 14; fold.Margin = new Thickness(0, 12, 0, 2); p.Children.Add(fold); p.Children.Add(extra);

        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(status);
        var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("저장", () =>
        {
            try
            {
                var next = new AiParameters { ReasoningEffort = AiParameters.Efforts[Math.Max(0, effort.SelectedIndex)].Key, Verbosity = AiParameters.Verbosities[Math.Max(0, verbosity.SelectedIndex)].Key, IncludeBody = include.Text, ExcludeBody = exclude.Text, IncludeHeaders = headers.Text };
                foreach (var (knob, set) in knobs) set(next, knob.Value);
                next.Normalize(); next.Extra();   // refuses JSON that is not an object, with a message
                int inputs = (int)(context.Value ?? throw new InvalidOperationException("컨텍스트 크기를 적어 주세요.")), outputs = (int)(output.Value ?? throw new InvalidOperationException("최대 응답 길이를 적어 주세요."));
                book.Connection = connection.SelectedIndex <= 0 ? "" : connections[connection.SelectedIndex - 1].Name; book.Model = model.Text.Trim() == Chosen()?.Model ? "" : model.Text.Trim();
                book.MaxContext = inputs; book.MaxOutput = outputs; book.Stream = stream.IsChecked == true; book.Parameters = next; book.ShowThinking = thinking.IsChecked == true;
                book.SystemPrompt = system.IsChecked == true; book.Squash = squash.IsChecked == true; book.MergeRoles = merge.IsChecked == true; book.Prompts.Prefill = prefill.Text;
                buddy.Novel.Save(); Close();
            }
            catch (InvalidOperationException e) { status.Text = e.Message; }
        }));
        buttons.Children.Add(Pastel.Button("닫기", Close, true)); footer.Children.Add(buttons);
        root.Children.Add(Pastel.Scroll(p));
        Pastel.Window(this, "소설 API 설정", root, 560, 860); MinWidth = 480; MinHeight = 520;
    }
}
