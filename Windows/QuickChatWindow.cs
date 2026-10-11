using System;
using System.Threading;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace KeyboardWorker;

public sealed class QuickChatWindow : Window
{
    readonly ComboBox recipient = new() { ItemsSource = new[] { "대표 친구", "둘째 친구", "둘 모두" }, Width = 150 };
    readonly ComboBox mode = new() { ItemsSource = DialogueModes.Rules.Keys.ToArray(), Width = 140 };
    readonly Buddy buddy;
    readonly TextBox input = new() { MaxLength = 6000, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 54, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock status = Pastel.Text("Enter 전송 · Shift+Enter 줄바꿈 · Esc 닫기", 11);
    readonly Button send;
    readonly CheckBox watching = new() { Content = "지금 보는 영상·듣는 노래도 알려주기", Margin = new Thickness(0, 4, 0, 2) };
    CancellationTokenSource? pending;
    bool closed;
    public QuickChatWindow(Buddy buddy)
    {
        this.buddy = buddy; var panel = new StackPanel { Margin = new Thickness(12) };
        recipient.SelectedIndex = buddy.Chat.Preferences.Target == "secondary" ? 1 : buddy.Chat.Preferences.Target == "both" ? 2 : 0; mode.SelectedItem = buddy.Chat.Preferences.Mode;
        var routes = new WrapPanel();
        routes.Children.Add(recipient);
        routes.Children.Add(mode);
        panel.Children.Add(routes);
        panel.Children.Add(input);
        watching.IsChecked = buddy.Pref.Awareness.Media;
        watching.ToolTip = "체크하면 지금 재생 중인 유튜브 영상·유튜브 뮤직 노래의 제목을 이 메시지와 함께 보내요.";
        panel.Children.Add(watching);
        var row = new WrapPanel();
        send = Pastel.Button("♡ 보내기", async () => await Send());
        row.Children.Add(send);
        row.Children.Add(Pastel.Button("중단", buddy.CancelMessage, true));
        panel.Children.Add(row);
        panel.Children.Add(status);
        Pastel.Window(this, "한마디 보내기", panel, 390, 318); MinWidth = 350; MinHeight = 292; ShowInTaskbar = false; Topmost = buddy.Pref.Topmost;
        Loaded += (_, _) => { FloatingPlacement.Place(this, buddy, "왼쪽"); input.Focus(); };
        PreviewKeyDown += async (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; await Send(); } };
        Closed += (_, _) => { closed = true; pending?.Cancel(); };
    }
    async System.Threading.Tasks.Task Send()
    {
        if (pending != null || string.IsNullOrWhiteSpace(input.Text)) return;
        pending = new(); send.IsEnabled = false; input.IsReadOnly = true; status.Text = "답변을 생각하는 중… 작업을 계속하셔도 돼요.";
        try
        {
            var p = buddy.Chat.Preferences.Snapshot();
            p.Target = recipient.SelectedIndex == 1 ? "secondary" : recipient.SelectedIndex == 2 ? "both" : "primary";
            p.Mode = mode.SelectedItem?.ToString() ?? "잡담";
            await buddy.Chat.ApplyAsync(p);
            await buddy.SendMessage(input.Text.Trim(), true, pending.Token, watching.IsChecked == true);
            if (!closed) Close();
        }
        catch (OperationCanceledException) { if (!closed) status.Text = "중단되었거나 응답 시간이 초과됐어요."; }
        catch (System.Net.Http.HttpRequestException) { if (!closed) status.Text = "네트워크와 AI 연결 설정을 확인해 주세요."; }
        catch (Exception e) { if (!closed) status.Text = e.Message; }
        finally { pending?.Dispose(); pending = null; if (!closed) { send.IsEnabled = true; input.IsReadOnly = false; input.Focus(); } }
    }
}
