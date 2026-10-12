using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// Accessories as small tiles, four to a row, under a heading for each place they are worn, with a box to find one by
// name. The shop and the wardrobe both use it: a click on a tile does the main thing (buy, put on, take off), a right
// click opens a menu with the rest. Pictures come from the shared, cached thumbnails (StageParts.Picture).
public sealed class GearShelf : StackPanel
{
    // Note: the small line under the name (a price, "장착 중"…). Marked: drawn as chosen. Pale: drawn faint (cannot be bought yet).
    public sealed record Tile(string Id, string Name, string Slot, string ImagePath, string Note, bool Marked, bool Pale, string Tip);
    readonly TextBox search = new() { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 4), ToolTip = "이름으로 찾기" }; readonly StackPanel groups = new();
    readonly Action<Tile> click; readonly Func<Tile, ContextMenu?> menu; Tile[] tiles = Array.Empty<Tile>(); string empty = "";
    public GearShelf(Action<Tile> click, Func<Tile, ContextMenu?> menu)
    {
        this.click = click; this.menu = menu; Children.Add(search); Children.Add(groups); search.TextChanged += (_, _) => Build();
    }
    // What to show now; emptyNote is said when there is nothing at all.
    public void Show(IEnumerable<Tile> all, string emptyNote) { tiles = all.ToArray(); empty = emptyNote; Build(); }
    void Build()
    {
        groups.Children.Clear(); string words = search.Text.Trim(); search.Visibility = tiles.Length > 8 || words.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var found = words.Length == 0 ? tiles : tiles.Where(t => t.Name.Contains(words, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (found.Length == 0) { groups.Children.Add(Pastel.Text(tiles.Length == 0 ? empty : "그런 이름의 악세서리가 없어요.", 13)); return; }
        foreach (string slot in CustomGear.Slots)
        {
            var members = found.Where(t => t.Slot == slot).ToArray(); if (members.Length == 0) continue;
            groups.Children.Add(new TextBlock { Text = $"{ShopGear.SlotName(slot)}  ({members.Length})", Foreground = Pastel.Hot, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(2, 10, 0, 6) });
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 }; foreach (var tile in members) grid.Children.Add(Card(tile)); groups.Children.Add(grid);
        }
    }
    Border Card(Tile tile)
    {
        var inside = new StackPanel(); var picture = StageParts.Picture(tile.ImagePath, 76); picture.HorizontalAlignment = HorizontalAlignment.Center; inside.Children.Add(picture);
        inside.Children.Add(new TextBlock { Text = tile.Name, Foreground = Pastel.Ink, FontWeight = FontWeights.Bold, FontSize = 12, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
        inside.Children.Add(new TextBlock { Text = tile.Note, Foreground = tile.Marked ? Pastel.Hot : Pastel.Ink, Opacity = tile.Marked ? 1 : .7, FontWeight = tile.Marked ? FontWeights.Bold : FontWeights.Normal, FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 1, 0, 0) });
        var card = new Border
        {
            Child = inside, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tile.Tip, Opacity = tile.Pale ? .5 : 1, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(6, 8, 6, 8), CornerRadius = new CornerRadius(12),
            Background = tile.Marked ? Pastel.CardPink : Pastel.Paper, BorderBrush = tile.Marked ? Pastel.Hot : Pastel.Line, BorderThickness = tile.Marked ? new Thickness(2.4) : new Thickness(1.2, 1.2, 2.4, 2.8)
        };
        card.MouseLeftButtonUp += (_, _) => click(tile);
        card.MouseRightButtonUp += (_, e) => { if (menu(tile) is { } options) { options.PlacementTarget = card; options.IsOpen = true; e.Handled = true; } };
        return card;
    }
}
