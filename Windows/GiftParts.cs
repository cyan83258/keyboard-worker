using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace KeyboardWorker;

// How a gift looks wherever it is shown (drawing, the bag, the collection, choosing one to give): one card, coloured by rarity.
static class GiftParts
{
    // Rarity colours are the same in every theme, so a five-star gift is always gold.
    static readonly Brush[] tones = { Pastel.Original("#8E9AA8"), Pastel.Original("#3FA66B"), Pastel.Original("#3F86D9"), Pastel.Original("#9A5BD6"), Pastel.Original("#E79A00") };
    static readonly Brush[] grounds = { Pastel.Original("#F6F8FA"), Pastel.Original("#EEF9F2"), Pastel.Original("#EDF4FD"), Pastel.Original("#F5EEFC"), Pastel.Original("#FFF6DC") };
    public static Brush Tone(int stars) => tones[Math.Clamp(stars, 1, 5) - 1];
    public static Brush Ground(int stars) => grounds[Math.Clamp(stars, 1, 5) - 1];
    // count: how many are owned (shown as ×n; -1 = not shown). hidden: a gift not met yet, shown as a silhouette.
    public static Border Card(GiftItem gift, int count = -1, bool hidden = false, bool loved = false)
    {
        var tone = hidden ? Pastel.Line : Tone(gift.Stars); var body = new StackPanel { Width = 132 };
        var top = new Grid { Height = 62 };
        top.Children.Add(new TextBlock { Text = hidden ? "?" : gift.Glyph, FontSize = 38, Foreground = tone, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = hidden ? .45 : 1 });
        if (count >= 0) top.Children.Add(new Border { Child = new TextBlock { Text = "×" + count, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold }, Background = tone, CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
        if (loved) top.Children.Add(new TextBlock { Text = "♥", Foreground = Pastel.Original("#E4479A"), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ToolTip = "이 친구가 특히 좋아하는 선물" });
        body.Children.Add(top);
        body.Children.Add(new TextBlock { Text = GiftShop.Stars(gift.Stars), Foreground = tone, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
        body.Children.Add(new TextBlock { Text = hidden ? "아직 못 만난 선물" : gift.Name, Foreground = Pastel.Original("#4A4050"), FontWeight = FontWeights.Bold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
        if (!hidden)
        {
            body.Children.Add(new TextBlock { Text = gift.Description, Foreground = Pastel.Original("#6B6272"), FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0), MinHeight = 30 });
            body.Children.Add(new TextBlock { Text = "친밀도 +" + GiftShop.Gain(gift.Stars) + (gift.Effect.Length > 0 ? " ✧" : ""), Foreground = tone, FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, ToolTip = GiftShop.EffectNote(gift).Length > 0 ? GiftShop.EffectNote(gift) : null });
        }
        return new Border { Child = body, Background = hidden ? Pastel.Original("#F3F1F4") : Ground(gift.Stars), BorderBrush = tone, BorderThickness = new Thickness(1.5, 1.5, 3, 3.5), CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 6, 8, 9), Margin = new Thickness(0, 0, 9, 9), SnapsToDevicePixels = true };
    }
    // A card pops in; a rare one glows.
    public static void Pop(Border card, GiftItem gift, bool animate)
    {
        if (gift.Stars >= 4) card.Effect = new DropShadowEffect { Color = ((SolidColorBrush)Tone(gift.Stars)).Color, BlurRadius = gift.Stars == 5 ? 26 : 16, ShadowDepth = 0, Opacity = .85 };
        if (!animate) return;
        var scale = new ScaleTransform(.3, .3); card.RenderTransformOrigin = new Point(.5, .5); card.RenderTransform = scale;
        var grow = new DoubleAnimation(.3, 1, TimeSpan.FromMilliseconds(gift.Stars >= 4 ? 520 : 300)) { EasingFunction = new BackEase { Amplitude = gift.Stars >= 4 ? .9 : .5, EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow); scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }
}
