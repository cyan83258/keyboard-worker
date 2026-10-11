using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardWorker;

// Lines the pets said during this run, newest first, for the ones that slipped by.
public sealed class LineHistoryWindow : Window
{
    readonly Buddy buddy; readonly StackPanel list = new();
    public LineHistoryWindow(Buddy buddy)
    {
        this.buddy = buddy; var p = new StackPanel { Margin = new Thickness(22) }; p.Children.Add(Pastel.Text("지난 대사 다시 보기", 23));
        p.Children.Add(Pastel.Text("이번 실행 동안 말풍선에 나온 대사를 최근 50개까지 보여줍니다. 프로그램을 끄면 지워져요. 남겨 두고 싶은 대사는 ♡로 도감에 담아 주세요.", 12)); p.Children.Add(list);
        var actions = new WrapPanel(); actions.Children.Add(Pastel.Button("새로 고침", Refresh, true)); actions.Children.Add(Pastel.Button("모두 복사", () => { try { Clipboard.SetText(string.Join("\n\n", buddy.SpokenLines.Reverse().Select(l => $"[{l.At:HH:mm}] {l.Speaker}: {l.Text}"))); } catch (System.Runtime.InteropServices.ExternalException) { /* another program is holding the clipboard */ } }, true)); actions.Children.Add(Pastel.Button("대사 도감 열기", buddy.OpenCollection, true)); actions.Children.Add(Pastel.Button("닫기", Close)); p.Children.Add(actions);
        Pastel.Window(this, "지난 대사", Pastel.Scroll(p), 560, 720); Activated += (_, _) => Refresh(); Refresh();
    }
    void Refresh()
    {
        list.Children.Clear(); var lines = buddy.SpokenLines.Reverse().ToArray();
        if (lines.Length == 0) { list.Children.Add(Pastel.Text("아직 나온 대사가 없어요.", 13)); return; }
        foreach (var line in lines)
        {
            var entry = new StackPanel(); var head = new DockPanel(); bool kept = buddy.Collection.Has(line.Speaker, line.Text.Trim());
            var keep = Pastel.Button(kept ? "♥ 담음" : "♡ 도감에 담기", () => { if (!buddy.Collection.Has(line.Speaker, line.Text.Trim())) buddy.Collection.Add(buddy.IdOf(line.Speaker), line.Speaker, line.Text); Refresh(); }, true);
            keep.Padding = new Thickness(8, 2, 8, 2); keep.FontSize = 11; keep.IsEnabled = !kept; DockPanel.SetDock(keep, Dock.Right); head.Children.Add(keep);
            head.Children.Add(new TextBlock { Text = $"♡ {line.Speaker} · {line.At:HH:mm}", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }); entry.Children.Add(head);
            entry.Children.Add(new TextBox { Text = line.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Pastel.Ink, FontSize = 14, Padding = new Thickness(0), Margin = new Thickness(0, 3, 0, 0) });
            var card = Pastel.Card(entry); card.Padding = new Thickness(12, 8, 12, 8); card.Margin = new Thickness(0, 0, 4, 6); list.Children.Add(card);
        }
    }
}
