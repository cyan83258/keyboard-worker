using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Interop;

namespace KeyboardWorker;

// Retro "love OS" pastel theme: pink + sky-blue windows with plum outlines,
// chunky 3D-edged buttons, star-sprinkled title bars and heart check marks.
public static class Pastel
{
    // Themes share one design and differ only in colour. "딸기팝" is the pink + sky-blue original; every other theme is
    // derived from it colour by colour (see Map), so each screen follows without keeping its own colour list.
    static readonly (string Id, string Name)[] builtIn = { ("strawberry", "딸기팝"), ("apple", "사과팝"), ("grape", "포도팝"), ("mint", "민트팝"), ("mono", "모노팝") };
    // Themes added by mod files: the pinks take one hue and the sky blues another, derived like the built-in ones.
    static (string Id, string Name, double Accent, double Second, double Saturation)[] extra = Array.Empty<(string, string, double, double, double)>();
    public static (string Id, string Name)[] Themes { get; private set; } = builtIn;
    public static void SetExtraThemes(IEnumerable<(string Id, string Name, double Accent, double Second, double Saturation)> themes)
    {
        extra = themes.Where(t => builtIn.All(b => b.Id != t.Id)).ToArray(); Themes = builtIn.Concat(extra.Select(t => (t.Id, t.Name))).ToArray();
    }
    static string theme = "strawberry";
    public static string Theme => theme;
    // Call before the first window is built: windows keep the brushes they were created with.
    public static void Use(string? id) { theme = Array.Exists(Themes, t => t.Id == id) ? id! : "strawberry"; shared = null; palette = Load(); }
    static Brush[] Load() => new[] { Brush("#7E3A6C"), Brush("#A85A8E"), Brush("#E4479A"), Brush("#FFB9DC"), Brush("#B3EFF4"), Brush("#2F8791"), Brush("#FFF1F8"), Brush("#FFFAFD"), Brush("#EAFBFC"), Brush("#F9AED4"), Brush("#A6E9EF") };
    static Brush[] palette = Load();

    // Core palette (colour names describe the strawberry theme)
    public static Brush Ink => palette[0];        // body text
    public static Brush Line => palette[1];       // outlines
    public static Brush Hot => palette[2];        // accent (selected / headings)
    public static Brush Pink => palette[3];       // primary button
    public static Brush Blue => palette[4];       // secondary button
    public static Brush Teal => palette[5];       // text on the secondary colour
    public static Brush Paper => palette[6];      // window body
    public static Brush CardPink => palette[7];
    public static Brush CardBlue => palette[8];
    public static Brush PinkBar => palette[9];
    public static Brush BlueBar => palette[10];

    // A strawberry colour in another theme. Only the two colour families of the design are touched: the pinks (light fills,
    // the vivid accent, muted text and outlines) and the sky blues (light fills, darker text). Each result is (hue, saturation,
    // lightness); text and accent tones are darkened so they stay readable on the light fills. Whites, greys and every other
    // hue (item artwork, fur, gold) are left alone.
    static string Map(string id, string hex)
    {
        if (id == "strawberry" || hex.Length is not (7 or 9) || hex[0] != '#' || !int.TryParse(hex.AsSpan(hex.Length - 6), System.Globalization.NumberStyles.HexNumber, null, out int rgb)) return hex;
        double r = (rgb >> 16 & 255) / 255.0, g = (rgb >> 8 & 255) / 255.0, b = (rgb & 255) / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min, l = (max + min) / 2;
        if (d < .015) return hex;
        double s = d / (1 - Math.Abs(2 * l - 1)), h = 60 * (max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4); if (h < 0) h += 360;
        bool pink = h >= 285 && h <= 352, sky = h >= 160 && h <= 215; if (!pink && !sky) return hex;
        bool light = pink ? l >= .72 : l >= .6, vivid = s >= .55;
        var own = Array.Find(extra, t => t.Id == id);
        if (own.Id != null) (h, s, l) = pink ? (light ? (own.Accent, s * own.Saturation, l) : vivid ? (own.Accent, s * own.Saturation * .9, l * .6) : (own.Accent, Math.Max(s, .4) * own.Saturation, l * .72)) : light ? (own.Second, s * own.Saturation * .9, l) : (own.Second, s * own.Saturation, l * .85);
        else (h, s, l) = (id, pink) switch
        {
            ("apple", true) => light ? (50, s, l) : vivid ? (92, s * .95, l * .6) : (75, Math.Max(s, .42), l * .75),
            ("apple", false) => light ? (88, s * .9, l) : (100, s, l * .9),
            ("grape", true) => light ? (268, s * .9, l) : vivid ? (270, s * .85, l * .85) : (272, Math.Max(s, .35), l * .8),
            ("grape", false) => light ? (232, s * .8, l) : (240, s * .7, l * .95),
            ("mint", true) => light ? (158, s * .75, l) : vivid ? (168, s * .9, l * .55) : (172, Math.Max(s, .4), l * .7),
            ("mint", false) => light ? (198, s * .85, l) : (200, s, l * .9),
            (_, true) => light ? (0, 0, l) : vivid ? (0, 0, l * .42) : (0, 0, l * .5),
            _ => light ? (0, 0, l * .93) : (0, 0, l * .8)
        };
        double c = (1 - Math.Abs(2 * l - 1)) * Math.Min(1, s), sector = h / 60, x = c * (1 - Math.Abs(sector % 2 - 1)), m = l - c / 2;
        (r, g, b) = sector < 1 ? (c, x, 0d) : sector < 2 ? (x, c, 0d) : sector < 3 ? (0d, c, x) : sector < 4 ? (0d, x, c) : sector < 5 ? (x, 0d, c) : (c, 0d, x);
        static int Byte(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
        return hex[..(hex.Length - 6)] + $"{Byte(r + m):X2}{Byte(g + m):X2}{Byte(b + m):X2}";
    }
    static readonly System.Text.RegularExpressions.Regex colours = new(@"(?<!&)#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\b");
    // Colours written inside a XAML string follow the theme too.
    public static string Themed(string xaml) => theme == "strawberry" ? xaml : colours.Replace(xaml, match => Map(theme, match.Value));

    public static Brush Brush(string hex) => Original(Map(theme, hex));
    // A colour that must not follow the theme: item artwork, the app icon, colours the user typed in.
    public static Brush Original(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }
    // A user setting follows the theme only while it still has its standard value.
    public static Brush Custom(string hex, string standard) => string.Equals(hex, standard, StringComparison.OrdinalIgnoreCase) ? Brush(hex) : Original(hex);
    // How a strawberry colour looks in any theme, for previews.
    public static Brush Sample(string id, string hex) => Original(Map(id, hex));
    // WCAG contrast ratio (1 = identical, 21 = black on white).
    public static double Contrast(Brush first, Brush second)
    {
        static double Light(Brush brush)
        {
            var c = ((SolidColorBrush)brush).Color; static double Linear(byte v) { double n = v / 255.0; return n <= .03928 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
            return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        }
        double a = Light(first), b = Light(second); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    // Text colour that is certainly readable on a background.
    public static Brush Readable(Brush background) => Contrast(background, Brushes.White) >= Contrast(background, Brushes.Black) ? Brushes.White : Original("#2B2B2B");
    static ResourceDictionary? shared;
    static ResourceDictionary Shared => shared ??= Parse();

    // The theme XAML is parsed once; each window or menu gets a thin dictionary that merges the shared one.
    public static ResourceDictionary Styles() { var own = new ResourceDictionary(); own.MergedDictionaries.Add(Shared); return own; }

    static ResourceDictionary Parse() => (ResourceDictionary)XamlReader.Parse(Themed("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

          <!-- Chunky pastel button with a thick plum bottom edge -->
          <Style TargetType="Button">
            <Setter Property="Foreground" Value="#7E3A6C"/><Setter Property="Background" Value="#FFB9DC"/>
            <Setter Property="BorderBrush" Value="#A85A8E"/><Setter Property="BorderThickness" Value="1.5,1.5,1.5,4"/>
            <Setter Property="Padding" Value="16,8"/><Setter Property="Margin" Value="4"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="edge" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="13" SnapsToDevicePixels="True">
                <Grid>
                  <Border x:Name="shine" Background="White" Opacity="0" CornerRadius="11"/>
                  <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True"/>
                </Grid>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="shine" Property="Opacity" Value="0.35"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="edge" Property="BorderBrush" Value="#E4479A"/></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter TargetName="edge" Property="BorderThickness" Value="1.5,3,1.5,2.5"/><Setter TargetName="shine" Property="Opacity" Value="0.15"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter TargetName="edge" Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>

          <!-- Round title-bar dot buttons (minimize / maximize / close) -->
          <Style x:Key="kwDot" TargetType="Button">
            <Setter Property="Width" Value="22"/><Setter Property="Height" Value="22"/><Setter Property="Margin" Value="3,0"/>
            <Setter Property="Foreground" Value="White"/><Setter Property="FontSize" Value="11"/><Setter Property="FontWeight" Value="Bold"/>
            <Setter Property="Cursor" Value="Hand"/><Setter Property="Focusable" Value="False"/><Setter Property="VerticalAlignment" Value="Center"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Grid>
                <Ellipse x:Name="dot" Fill="{TemplateBinding Background}" Stroke="#A85A8E" StrokeThickness="1.5"/>
                <Ellipse x:Name="shine" Fill="White" Opacity="0"/>
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="0,-1,0,0"/>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="shine" Property="Opacity" Value="0.35"/></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter TargetName="shine" Property="Opacity" Value="0.1"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>

          <!-- Flat icon button (bubble close etc.) -->
          <Style x:Key="kwGhost" TargetType="Button">
            <Setter Property="Foreground" Value="#A85A8E"/><Setter Property="Cursor" Value="Hand"/><Setter Property="Focusable" Value="False"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="b" Background="Transparent" CornerRadius="8" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#FFDDEF"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>

          <!-- Rounded text inputs -->
          <Style TargetType="TextBox">
            <Setter Property="Background" Value="White"/><Setter Property="Foreground" Value="#6E3360"/>
            <Setter Property="BorderBrush" Value="#D69BBF"/><Setter Property="BorderThickness" Value="1.5"/>
            <Setter Property="Padding" Value="10,7"/><Setter Property="Margin" Value="0,4,0,10"/>
            <Setter Property="CaretBrush" Value="#E4479A"/><Setter Property="SelectionBrush" Value="#F9AED4"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox">
              <Border x:Name="edge" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="10" SnapsToDevicePixels="True">
                <ScrollViewer x:Name="PART_ContentHost" Focusable="False" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="edge" Property="BorderBrush" Value="#EE8CC0"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="edge" Property="BorderBrush" Value="#E4479A"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter TargetName="edge" Property="Opacity" Value="0.55"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="PasswordBox">
            <Setter Property="Background" Value="White"/><Setter Property="Foreground" Value="#6E3360"/>
            <Setter Property="BorderBrush" Value="#D69BBF"/><Setter Property="BorderThickness" Value="1.5"/>
            <Setter Property="Padding" Value="10,7"/><Setter Property="Margin" Value="0,4,0,10"/>
            <Setter Property="CaretBrush" Value="#E4479A"/><Setter Property="SelectionBrush" Value="#F9AED4"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="PasswordBox">
              <Border x:Name="edge" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="10" SnapsToDevicePixels="True">
                <ScrollViewer x:Name="PART_ContentHost" Focusable="False" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="edge" Property="BorderBrush" Value="#EE8CC0"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="edge" Property="BorderBrush" Value="#E4479A"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>

          <!-- Check box with a heart mark -->
          <Style TargetType="CheckBox">
            <Setter Property="Foreground" Value="#7E3A6C"/><Setter Property="Margin" Value="0,8,0,8"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox">
              <Grid Background="Transparent">
                <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <Border x:Name="box" Width="21" Height="21" CornerRadius="7" Background="White" BorderBrush="#A85A8E" BorderThickness="1.5,1.5,1.5,3" VerticalAlignment="Center">
                  <TextBlock x:Name="mark" Text="♥" FontSize="12" Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Hidden"/>
                </Border>
                <ContentPresenter Grid.Column="1" Margin="9,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="box" Property="BorderBrush" Value="#E4479A"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="box" Property="BorderBrush" Value="#E4479A"/></Trigger>
                <Trigger Property="IsChecked" Value="True"><Setter TargetName="box" Property="Background" Value="#F06AAE"/><Setter TargetName="mark" Property="Visibility" Value="Visible"/></Trigger>
                <Trigger Property="IsChecked" Value="{x:Null}"><Setter TargetName="box" Property="Background" Value="#F9AED4"/><Setter TargetName="mark" Property="Text" Value="−"/><Setter TargetName="mark" Property="Visibility" Value="Visible"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>

          <!-- Combo box -->
          <Style TargetType="ComboBoxItem">
            <Setter Property="Foreground" Value="#7E3A6C"/><Setter Property="Padding" Value="10,6"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
              <Border x:Name="b" Background="Transparent" CornerRadius="8" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="b" Property="Background" Value="#FFDDEF"/></Trigger>
                <Trigger Property="IsSelected" Value="True"><Setter TargetName="b" Property="Background" Value="#C9F4F8"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBox">
            <Setter Property="Foreground" Value="#6E3360"/><Setter Property="Padding" Value="12,7,32,7"/><Setter Property="Margin" Value="0,4,0,10"/>
            <Setter Property="MinHeight" Value="36"/><Setter Property="Cursor" Value="Hand"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
              <Grid x:Name="root">
                <ToggleButton Focusable="False" ClickMode="Press" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                  <ToggleButton.Template><ControlTemplate TargetType="ToggleButton">
                    <Border x:Name="box" Background="White" BorderBrush="#A85A8E" BorderThickness="1.5,1.5,1.5,3" CornerRadius="10" SnapsToDevicePixels="True">
                      <TextBlock Text="▾" FontSize="15" Foreground="#E4479A" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,12,1"/>
                    </Border>
                    <ControlTemplate.Triggers>
                      <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="box" Property="Background" Value="#FFF4FA"/></Trigger>
                      <Trigger Property="IsChecked" Value="True"><Setter TargetName="box" Property="BorderBrush" Value="#E4479A"/></Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate></ToggleButton.Template>
                </ToggleButton>
                <ContentPresenter IsHitTestVisible="False" Margin="{TemplateBinding Padding}" HorizontalAlignment="Left" VerticalAlignment="Center"
                                  Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"/>
                <Popup x:Name="PART_Popup" Placement="Bottom" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade"
                       IsOpen="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                  <Border Background="#FFF8FC" BorderBrush="#A85A8E" BorderThickness="1.5,1.5,1.5,3" CornerRadius="12" Padding="4" Margin="0,4,0,0"
                          MinWidth="{Binding ActualWidth, ElementName=root}" MaxHeight="{TemplateBinding MaxDropDownHeight}">
                    <ScrollViewer SnapsToDevicePixels="True"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
                  </Border>
                </Popup>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsEnabled" Value="False"><Setter TargetName="root" Property="Opacity" Value="0.5"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>

          <!-- Tabs as chunky pills -->
          <Style TargetType="TabControl">
            <Setter Property="Padding" Value="2"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabControl">
              <Grid KeyboardNavigation.TabNavigation="Local">
                <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
                <TabPanel IsItemsHost="True" Margin="2,0,2,6" KeyboardNavigation.TabIndex="1" Panel.ZIndex="1"/>
                <Border Grid.Row="1" Background="#FFFAFD" BorderBrush="#A85A8E" BorderThickness="1.5,1.5,3.5,3.5" CornerRadius="14" SnapsToDevicePixels="True">
                  <ContentPresenter x:Name="PART_SelectedContentHost" ContentSource="SelectedContent" Margin="{TemplateBinding Padding}"/>
                </Border>
              </Grid>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="TabItem">
            <Setter Property="Foreground" Value="#7E3A6C"/><Setter Property="Padding" Value="14,6"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem">
              <Border x:Name="tab" Background="#FFE3F1" BorderBrush="#A85A8E" BorderThickness="1.5,1.5,1.5,3.5" CornerRadius="12" Padding="{TemplateBinding Padding}" Margin="0,4,6,2" SnapsToDevicePixels="True">
                <ContentPresenter x:Name="head" ContentSource="Header" HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="tab" Property="Background" Value="#C9F4F8"/></Trigger>
                <Trigger Property="IsSelected" Value="True">
                  <Setter TargetName="tab" Property="Background" Value="#E4479A"/>
                  <Setter TargetName="head" Property="TextElement.Foreground" Value="White"/>
                  <Setter TargetName="head" Property="TextElement.FontWeight" Value="Bold"/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>

          <!-- Lists -->
          <Style TargetType="ListBox">
            <Setter Property="Background" Value="White"/><Setter Property="BorderBrush" Value="#D69BBF"/><Setter Property="BorderThickness" Value="1.5"/>
            <Setter Property="Foreground" Value="#7E3A6C"/><Setter Property="Padding" Value="3"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBox">
              <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="12" SnapsToDevicePixels="True">
                <ScrollViewer Focusable="False" Padding="{TemplateBinding Padding}"
                              CanContentScroll="{TemplateBinding ScrollViewer.CanContentScroll}"
                              HorizontalScrollBarVisibility="{TemplateBinding ScrollViewer.HorizontalScrollBarVisibility}"
                              VerticalScrollBarVisibility="{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}">
                  <ItemsPresenter/>
                </ScrollViewer>
              </Border>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ListBoxItem">
            <Setter Property="Foreground" Value="#7E3A6C"/><Setter Property="Padding" Value="10,6"/><Setter Property="Margin" Value="1"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem">
              <Border x:Name="b" Background="Transparent" CornerRadius="8" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#EAFBFC"/></Trigger>
                <Trigger Property="IsSelected" Value="True"><Setter TargetName="b" Property="Background" Value="#FFD3EA"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>

          <!-- Slider: pink fill + round knob -->
          <Style x:Key="kwSliderFill" TargetType="RepeatButton">
            <Setter Property="Focusable" Value="False"/><Setter Property="IsTabStop" Value="False"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RepeatButton">
              <Grid Background="Transparent"><Border Height="6" CornerRadius="3" Margin="3,0,0,0" Background="#F06AAE" VerticalAlignment="Center"/></Grid>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="kwSliderRest" TargetType="RepeatButton">
            <Setter Property="Focusable" Value="False"/><Setter Property="IsTabStop" Value="False"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RepeatButton"><Grid Background="Transparent"/></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="Slider">
            <Setter Property="Cursor" Value="Hand"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider">
              <Grid MinHeight="26" Background="Transparent">
                <Border Height="12" CornerRadius="6" Background="White" BorderBrush="#A85A8E" BorderThickness="1.5" VerticalAlignment="Center"/>
                <Track x:Name="PART_Track" Orientation="{TemplateBinding Orientation}">
                  <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource kwSliderFill}" Command="{x:Static Slider.DecreaseLarge}"/></Track.DecreaseRepeatButton>
                  <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource kwSliderRest}" Command="{x:Static Slider.IncreaseLarge}"/></Track.IncreaseRepeatButton>
                  <Track.Thumb><Thumb Width="22" Height="22"><Thumb.Template><ControlTemplate TargetType="Thumb">
                    <Grid>
                      <Ellipse x:Name="knob" Fill="#FFB9DC" Stroke="#A85A8E" StrokeThickness="1.5"/>
                      <TextBlock Text="♥" FontSize="10" Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Grid>
                    <ControlTemplate.Triggers>
                      <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="knob" Property="Fill" Value="#F06AAE"/></Trigger>
                      <Trigger Property="IsDragging" Value="True"><Setter TargetName="knob" Property="Fill" Value="#E4479A"/></Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                </Track>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>

          <!-- Dotted progress bar -->
          <DrawingBrush x:Key="kwDots" TileMode="Tile" Viewport="0,0,10,8" ViewportUnits="Absolute" Stretch="None">
            <DrawingBrush.Drawing><DrawingGroup>
              <GeometryDrawing Brush="#00FFFFFF"><GeometryDrawing.Geometry><RectangleGeometry Rect="0,0,10,8"/></GeometryDrawing.Geometry></GeometryDrawing>
              <GeometryDrawing Brush="#F06AAE"><GeometryDrawing.Geometry><EllipseGeometry Center="5,4" RadiusX="3.2" RadiusY="3.2"/></GeometryDrawing.Geometry></GeometryDrawing>
            </DrawingGroup></DrawingBrush.Drawing>
          </DrawingBrush>
          <Style TargetType="ProgressBar">
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ProgressBar">
              <Grid MinHeight="12">
                <Border x:Name="PART_Track" Background="White" BorderBrush="#A85A8E" BorderThickness="1.5" CornerRadius="6"/>
                <Border x:Name="PART_Indicator" HorizontalAlignment="Left" Margin="3,2" Background="{StaticResource kwDots}"/>
              </Grid>
            </ControlTemplate></Setter.Value></Setter>
          </Style>

          <!-- Slim pastel scroll bars -->
          <Style x:Key="kwBarPage" TargetType="RepeatButton">
            <Setter Property="Focusable" Value="False"/><Setter Property="IsTabStop" Value="False"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="kwBarThumb" TargetType="Thumb">
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Thumb">
              <Border x:Name="t" Background="#F9AED4" BorderBrush="#A85A8E" BorderThickness="1" CornerRadius="5"/>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="t" Property="Background" Value="#A6E9EF"/></Trigger>
                <Trigger Property="IsDragging" Value="True"><Setter TargetName="t" Property="Background" Value="#F06AAE"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ScrollBar">
            <Setter Property="Width" Value="14"/><Setter Property="MinWidth" Value="14"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
              <Border Background="#FFE6F2" CornerRadius="6" Margin="2">
                <Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="Vertical">
                  <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource kwBarPage}" Command="{x:Static ScrollBar.PageUpCommand}"/></Track.DecreaseRepeatButton>
                  <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource kwBarPage}" Command="{x:Static ScrollBar.PageDownCommand}"/></Track.IncreaseRepeatButton>
                  <Track.Thumb><Thumb Style="{StaticResource kwBarThumb}"/></Track.Thumb>
                </Track>
              </Border>
            </ControlTemplate></Setter.Value></Setter>
            <Style.Triggers>
              <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Width" Value="Auto"/><Setter Property="MinWidth" Value="0"/>
                <Setter Property="Height" Value="14"/><Setter Property="MinHeight" Value="14"/>
                <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
                  <Border Background="#FFE6F2" CornerRadius="6" Margin="2">
                    <Track x:Name="PART_Track" IsDirectionReversed="False" Orientation="Horizontal">
                      <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource kwBarPage}" Command="{x:Static ScrollBar.PageLeftCommand}"/></Track.DecreaseRepeatButton>
                      <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource kwBarPage}" Command="{x:Static ScrollBar.PageRightCommand}"/></Track.IncreaseRepeatButton>
                      <Track.Thumb><Thumb Style="{StaticResource kwBarThumb}"/></Track.Thumb>
                    </Track>
                  </Border>
                </ControlTemplate></Setter.Value></Setter>
              </Trigger>
            </Style.Triggers>
          </Style>

          <!-- Right-click menus -->
          <Style x:Key="kwMenu" TargetType="ContextMenu">
            <Setter Property="Foreground" Value="#7E3A6C"/><Setter Property="FontFamily" Value="Malgun Gothic"/><Setter Property="FontSize" Value="13"/>
            <Setter Property="HasDropShadow" Value="False"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ContextMenu">
              <Border Background="#FFF6FB" BorderBrush="#A85A8E" BorderThickness="1.5,1.5,3,3" CornerRadius="12" Padding="5">
                <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
              </Border>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="MenuItem">
            <Setter Property="Foreground" Value="#7E3A6C"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem">
              <Border x:Name="b" Background="Transparent" CornerRadius="8" Padding="12,7,10,7" MinWidth="170">
                <Grid>
                  <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <TextBlock x:Name="heart" Text="♡" Foreground="#E4479A" Margin="0,0,8,0" VerticalAlignment="Center"/>
                  <ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center"/>
                  <TextBlock x:Name="arrow" Grid.Column="2" Text="▸" Foreground="#E4479A" Margin="14,0,0,0" VerticalAlignment="Center" Visibility="Collapsed"/>
                  <Popup x:Name="PART_Popup" Placement="Right" PlacementTarget="{Binding ElementName=b}" HorizontalOffset="4" VerticalOffset="-7"
                         AllowsTransparency="True" Focusable="False" PopupAnimation="Fade"
                         IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}">
                    <Border Background="#FFF6FB" BorderBrush="#A85A8E" BorderThickness="1.5,1.5,3,3" CornerRadius="12" Padding="5">
                      <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
                    </Border>
                  </Popup>
                </Grid>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="b" Property="Background" Value="#FFDDEF"/><Setter TargetName="heart" Property="Text" Value="♥"/></Trigger>
                <Trigger Property="Role" Value="SubmenuHeader"><Setter TargetName="arrow" Property="Visibility" Value="Visible"/></Trigger>
                <Trigger Property="IsSubmenuOpen" Value="True"><Setter TargetName="b" Property="Background" Value="#C9F4F8"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator">
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Separator">
              <Border Height="1.5" Margin="10,5" Background="#F2BCD9"/>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
        </ResourceDictionary>
        """));

    // Little white stars + dots sprinkled over the title bar, like the retro window samples.
    static DrawingBrush Sparkles()
    {
        var star = Geometry.Parse("M 4,0 L 5,3 8,4 5,5 4,8 3,5 0,4 3,3 Z"); var white = Brush("#CCFFFFFF");
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 90, 40));
            foreach (var (x, y) in new[] { (8.0, 7.0), (52.0, 24.0) }) { dc.PushTransform(new TranslateTransform(x, y)); dc.DrawGeometry(white, null, star); dc.Pop(); }
            foreach (var (x, y, r) in new[] { (34.0, 12.0, 1.8), (74.0, 9.0, 1.4), (22.0, 30.0, 1.3), (82.0, 30.0, 1.8) }) dc.DrawEllipse(white, null, new Point(x, y), r, r);
        }
        group.Freeze(); var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 90, 40), Stretch = Stretch.None }; brush.Freeze(); return brush;
    }

    public static void Window(Window w, string title, UIElement body, double width = 620, double height = 640, bool? blue = null)
    {
        w.Title = title; w.Width = width; w.Height = height; w.MinWidth = 420; w.MinHeight = 340;
        w.WindowStyle = WindowStyle.None; w.ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(w, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        w.SourceInitialized += (_, _) => Native.RemoveSystemBorder(new WindowInteropHelper(w).Handle);
        w.Loaded += (_, _) => Native.RemoveSystemBorder(new WindowInteropHelper(w).Handle);
        w.Activated += (_, _) => Native.RemoveSystemBorder(new WindowInteropHelper(w).Handle);
        w.Deactivated += (_, _) => Native.RemoveSystemBorder(new WindowInteropHelper(w).Handle);
        w.ContentRendered += (_, _) => { if (w.IsVisible) Front(w); };
        // The pets are always-on-top. While one of our windows is being used it joins that layer so the pets cannot cover it, and leaves it again when the user moves on.
        bool lifted = false;
        w.Activated += (_, _) => { if (!Unattended && !w.Topmost) { w.Topmost = true; lifted = true; } };
        w.Deactivated += (_, _) => { if (lifted) { lifted = false; w.Topmost = false; } };
        w.Icon = PetIcon.WindowIcon;
        w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.Background = Paper; w.Foreground = Ink; w.FontFamily = new FontFamily("Malgun Gothic"); w.FontSize = 14;
        w.Resources = Styles();

        // Pink or sky-blue title bar (stable per window so the app mixes both, like the sample sheet).
        int hash = 0; foreach (char c in title) hash += c;
        bool sky = blue ?? hash % 3 == 1;
        var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) }); layout.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { Background = sky ? BlueBar : PinkBar };
        header.Children.Add(new Border { Background = Sparkles(), IsHitTestVisible = false });
        header.Children.Add(new Border { BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 2), IsHitTestVisible = false });
        header.Children.Add(new TextBlock { Text = "♡  " + title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 110, 1), FontWeight = FontWeights.Bold, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 12, 1) };
        Button Dot(string glyph, string fill, Brush glyphBrush, string tip, Action action)
        {
            var b = new Button { Content = glyph, Background = Brush(fill), Foreground = glyphBrush, ToolTip = tip, Style = (Style)w.Resources["kwDot"] };
            b.Click += (_, _) => action(); return b;
        }
        var light = sky ? "#EAFBFC" : "#FFF1F8"; var mid = sky ? "#5FD0DB" : "#F590C6"; var strong = sky ? "#2FB3C1" : "#E4479A";
        controls.Children.Add(Dot("–", light, Line, "최소화", () => w.WindowState = WindowState.Minimized));
        controls.Children.Add(Dot("□", mid, Brushes.White, "최대화", () => w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        controls.Children.Add(Dot("×", strong, Brushes.White, "닫기", w.Close));
        header.Children.Add(controls);
        header.MouseLeftButtonDown += (_, e) => { if (e.Source is Button) return; if (e.ClickCount == 2) w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else if (w.WindowState == WindowState.Normal) w.DragMove(); };
        layout.Children.Add(header); Grid.SetRow(body, 1); layout.Children.Add(body);
        w.Content = new Border { Background = Paper, BorderBrush = Line, BorderThickness = new Thickness(2), Child = layout };
    }

    // Show a window above whatever is on screen, restoring it first if it was minimized.
    // Set for automated runs (smoke test): their windows must not jump in front of what the user is doing.
    public static bool Unattended { get; set; }
    public static void Front(Window w)
    {
        if (Unattended) return;
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate(); Native.Front(new WindowInteropHelper(w).Handle);
    }

    public static Button Button(string text, Action action, bool blue = false)
    {
        var b = new Button { Content = text, Background = blue ? Blue : Pink };
        b.Click += (_, _) => action(); return b;
    }

    // Highlight the active item of a button group (settings sidebar etc.).
    public static void Select(Button b, bool selected)
    {
        b.Background = selected ? Hot : Blue; b.Foreground = selected ? Brushes.White : Ink; b.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
    }

    // Big text (titles) turns hot pink + bold, like "ATTENTION!" / "404 ERROR" in the sample.
    public static TextBlock Text(string text, double size = 14)
    {
        var block = new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size, Margin = new Thickness(0, 4, 0, 8),
            Foreground = size >= 20 ? Hot : Ink, FontWeight = size >= 20 ? FontWeights.Bold : FontWeights.Normal
        };
        // A long text in small print is an explanation: it is folded into a "?" that shows it on hover, so windows stay calm.
        // Labels, headings and texts that are filled in later (status lines, counts) are short or empty here and stay as they are.
        if (!ShowNotes && size <= 14 && text.Length >= NoteLength)
        {
            block.Text = ""; if (HideHelp) { block.Visibility = Visibility.Collapsed; return block; }
            block.Inlines.Add(new System.Windows.Documents.InlineUIContainer(Help(text))); block.Margin = new Thickness(0, 1, 0, 5);
        }
        return block;
    }
    // Explanations are written out in full instead of folded (a setting; applies to windows opened afterwards).
    public static bool ShowNotes { get; set; }
    // The "?" marks are not shown at all and take no room (a setting; applies to windows opened afterwards).
    public static bool HideHelp { get; set; }
    public const int NoteLength = 40;
    // The small translucent "?" that shows an explanation while the mouse is on it.
    public static FrameworkElement Help(string text)
    {
        var tip = new ToolTip
        {
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 400, Foreground = Ink, FontSize = 13, LineHeight = 20 },
            Background = Paper, BorderBrush = Line, BorderThickness = new Thickness(1.5), Padding = new Thickness(12, 8, 12, 9)
        };
        var badge = new Border
        {
            Width = 17, Height = 17, CornerRadius = new CornerRadius(9), Background = Line, Opacity = .38, Cursor = System.Windows.Input.Cursors.Help, ToolTip = tip, SnapsToDevicePixels = true,
            Child = new TextBlock { Text = "?", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) }
        };
        ToolTipService.SetInitialShowDelay(badge, 120); ToolTipService.SetShowDuration(badge, 60000); ToolTipService.SetBetweenShowDelay(badge, 0);
        badge.MouseEnter += (_, _) => badge.Opacity = .9; badge.MouseLeave += (_, _) => badge.Opacity = .38;
        if (HideHelp) badge.Visibility = Visibility.Collapsed;
        return badge;
    }

    // Sticker-like card: plum outline with a thicker bottom-right edge for a 3D pop.
    public static Border Card(UIElement child, bool blue = false) => new()
    {
        Child = child, Padding = new Thickness(18), Margin = new Thickness(0, 8, 4, 10),
        Background = blue ? CardBlue : CardPink, BorderBrush = Line, BorderThickness = new Thickness(1.5, 1.5, 3.5, 3.5), CornerRadius = new CornerRadius(14), SnapsToDevicePixels = true
    };

    public static DrawingBrush Checker()
    {
        var group = new DrawingGroup(); using (var dc = group.Open()) { dc.DrawRectangle(Brush("#FFF5FA"), null, new Rect(0, 0, 32, 32)); dc.DrawRectangle(Brush("#FFDDEE"), null, new Rect(0, 0, 16, 16)); dc.DrawRectangle(Brush("#FFDDEE"), null, new Rect(16, 16, 16, 16)); }
        group.Freeze(); var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 32, 32), Stretch = Stretch.None }; brush.Freeze(); return brush;
    }

    // Apply the pastel look to a right-click menu (menus live in their own popup, outside the window's resources).
    public static ContextMenu Menu(ContextMenu menu)
    {
        menu.Resources = Styles(); menu.Style = (Style)menu.Resources["kwMenu"]; return menu;
    }

    // Open a pet's right-click menu beside the pet instead of over it. WPF uses the first candidate that fits on screen,
    // so the menu flips to the other side (or above/below) when the pet sits at a screen edge.
    public static void PlaceBeside(ContextMenu menu)
    {
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
        menu.CustomPopupPlacementCallback = (popup, target, _) =>
        {
            double left = -popup.Width, right = target.Width, low = target.Height - popup.Height;
            var across = System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal; var along = System.Windows.Controls.Primitives.PopupPrimaryAxis.Vertical;
            return new System.Windows.Controls.Primitives.CustomPopupPlacement[]
            {
                new(new Point(right, 0), across), new(new Point(right, low), across), new(new Point(left, 0), across), new(new Point(left, low), across),
                new(new Point(0, -popup.Height), along), new(new Point(0, target.Height), along)
            };
        };
    }

    // Item container without selection highlight (for chat bubbles).
    public static Style PlainItem() => (Style)XamlReader.Parse("""
        <Style TargetType="ListBoxItem" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Padding" Value="0"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"/>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);

    // Small flat button (bubble close).
    public static void Ghost(Button b) => b.Style = (Style)Shared["kwGhost"];

    public static void Capture(Window window, string path)
    {
        window.UpdateLayout(); var visual = (FrameworkElement)window.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); using var stream = System.IO.File.Create(path); encoder.Save(stream);
    }
    public static ScrollViewer Scroll(UIElement child) => new() { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
}
