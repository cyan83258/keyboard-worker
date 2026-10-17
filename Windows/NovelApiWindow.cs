using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// The AI settings of the novel alone: which saved connection writes it, how much it may read and write, and its finer
// parameters (the same panel as the connections window, kept apart from the connection's own values).
public sealed class NovelApiWindow : Window
{
    const string Same = "대화에 쓰는 AI 그대로";
    public NovelApiWindow(Buddy buddy)
    {
        var book = buddy.Novel.State; var p = new StackPanel(); var status = Pastel.Text("", 12); var root = new DockPanel { Margin = new Thickness(20) };
        p.Children.Add(Pastel.Text("소설 API 설정", 20));
        p.Children.Add(new TextBlock { Text = "소설에만 쓰이는 설정이에요. 채팅과 캐릭터 페이지에는 영향을 주지 않아요.", Foreground = Pastel.Ink, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });
        p.Children.Add(Pastel.Text("AI 연결 (모델)", 14));
        var names = new[] { Same }.Concat(buddy.Chat.Preferences.Connections.Select(c => c.Name + (c.Model.Length > 0 ? "  ·  " + c.Model : ""))).ToArray(); var connections = buddy.Chat.Preferences.Connections;
        var connection = new ComboBox { ItemsSource = names, MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
        int at = connections.FindIndex(c => string.Equals(c.Name, book.Connection, StringComparison.OrdinalIgnoreCase)); connection.SelectedIndex = at + 1;
        var row = new WrapPanel(); row.Children.Add(connection); var manage = Pastel.Button("AI 연결 관리…", buddy.OpenConnections, true); manage.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(manage); p.Children.Add(row);
        p.Children.Add(new TextBlock { Text = "다른 모델이나 주소를 쓰려면 'AI 연결 관리'에서 연결을 하나 더 만든 뒤 여기서 고르세요.", Foreground = Pastel.Ink, Opacity = .75, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap });
        var limits = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        TextBox Number(string label, int value, string unit) { limits.Children.Add(new TextBlock { Text = label, Foreground = Pastel.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }); var box = new TextBox { Text = value.ToString(), Width = 90, MaxLength = 7 }; limits.Children.Add(box); limits.Children.Add(new TextBlock { Text = unit, Foreground = Pastel.Ink, FontSize = 11.5, Opacity = .75, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 22, 0) }); return box; }
        var context = Number("최대 입력 토큰", book.MaxContext, "2000~2000000"); context.ToolTip = "넘으면 이야기의 오래된 부분부터 빠져요"; var output = Number("최대 출력 토큰", book.MaxOutput, "256~64000"); p.Children.Add(limits);
        p.Children.Add(Pastel.Text("세부 파라미터", 14));
        var parameters = new AiParameterPanel { Margin = new Thickness(0) }; parameters.Show(book.Parameters); p.Children.Add(parameters);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(status);
        var buttons = new WrapPanel();
        buttons.Children.Add(Pastel.Button("저장", () =>
        {
            try
            {
                if (!int.TryParse(context.Text, out int inputs) || inputs < 2000 || inputs > 2000000) throw new InvalidOperationException("최대 입력 토큰은 2000~2000000 사이의 숫자로 적어 주세요.");
                if (!int.TryParse(output.Text, out int outputs) || outputs < 256 || outputs > 64000) throw new InvalidOperationException("최대 출력 토큰은 256~64000 사이의 숫자로 적어 주세요.");
                var read = parameters.Read(); book.Connection = connection.SelectedIndex <= 0 ? "" : connections[connection.SelectedIndex - 1].Name;
                book.MaxContext = inputs; book.MaxOutput = outputs; book.Parameters = read; buddy.Novel.Save(); Close();
            }
            catch (InvalidOperationException e) { status.Text = e.Message; }
        }));
        buttons.Children.Add(Pastel.Button("닫기", Close, true)); footer.Children.Add(buttons);
        root.Children.Add(Pastel.Scroll(p));
        Pastel.Window(this, "소설 API 설정", root, 700, 820); MinWidth = 560; MinHeight = 520;
    }
}
