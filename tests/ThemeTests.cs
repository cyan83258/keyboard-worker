using System;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace KeyboardWorker;

public static class ThemeTests
{
    static (int R, int G, int B) Rgb(string hex) { var c = (Color)ColorConverter.ConvertFromString(hex); return (c.R, c.G, c.B); }
    static Color Tone(string id, string hex) => ((SolidColorBrush)Pastel.Sample(id, hex)).Color;
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        try
        {
            Pastel.Use("strawberry");
            Check(Pastel.Themed("#FFB9DC #B3EFF4") == "#FFB9DC #B3EFF4" && ((SolidColorBrush)Pastel.Pink).Color == (Color)ColorConverter.ConvertFromString("#FFB9DC"), "Strawberry keeps the original colours");
            Pastel.Use("apple"); Check(Pastel.Theme == "apple", "Apple theme selected");
            var yellow = Rgb(Pastel.Themed("#FFB9DC")); Check(yellow.R >= 250 && yellow.G > 225 && yellow.B < 200, "Light pink becomes pastel yellow");
            var green = Rgb(Pastel.Themed("#B3EFF4")); Check(green.G > green.R && green.R > green.B && green.G > 225, "Sky blue becomes pastel yellow-green");
            var accent = Rgb(Pastel.Themed("#E4479A")); Check(accent.G > accent.R && accent.R > accent.B && accent.G < 190, "The accent becomes a green dark enough for white text");
            var ink = Rgb(Pastel.Themed("#7E3A6C")); Check(ink.G >= ink.R && ink.R > ink.B && ink.G < 120, "Body text becomes dark olive");
            Check(Pastel.Themed("#FFFFFF #808080 #FFE1A8 #FF3B30") == "#FFFFFF #808080 #FFE1A8 #FF3B30", "White, grey and unrelated hues are left alone");
            Check(Pastel.Themed("#CCFFB9DC").StartsWith("#CC") && Pastel.Themed("#CCFFB9DC").Length == 9 && Pastel.Themed("&#9825; Width=\"#\"") == "&#9825; Width=\"#\"", "Transparency and non-colour text survive");
            Check(((SolidColorBrush)Pastel.Pink).Color.B < 200 && ((SolidColorBrush)Pastel.Original("#FFB9DC")).Color.B == 0xDC, "Palette follows the theme while artwork colours do not");
            Check(((SolidColorBrush)Pastel.Custom("#875477", "#875477")).Color != (Color)ColorConverter.ConvertFromString("#875477") && ((SolidColorBrush)Pastel.Custom("#123456", "#875477")).Color == (Color)ColorConverter.ConvertFromString("#123456"), "A standard setting follows the theme; a colour the user chose does not");
            Pastel.Use("unknown"); Check(Pastel.Theme == "strawberry", "Unknown theme falls back to strawberry");
            var settings = new Preferences { Theme = "apple" }; settings.Normalize(); Check(settings.Theme == "apple", "Apple theme is a valid setting");
            settings.Theme = "cherry"; settings.Normalize(); Check(settings.Theme == "strawberry" && new Preferences().Theme == "strawberry", "Strawberry is the default theme");
            Check(Pastel.Themes.Select(t => t.Name).SequenceEqual(new[] { "딸기팝", "사과팝", "포도팝", "민트팝", "모노팝" }), "All five themes are offered");

            var purple = Tone("grape", "#FFB9DC"); Check(purple.B > purple.R && purple.R > purple.G, "Grape turns the pinks purple");
            var mint = Tone("mint", "#FFB9DC"); Check(mint.G > mint.B && mint.B > mint.R, "Mint turns the pinks mint green");
            Check(new[] { "#FFB9DC", "#B3EFF4", "#E4479A", "#7E3A6C", "#A85A8E", "#2F8791", "#FFF1F8" }.All(hex => { var c = Tone("mono", hex); return c.R == c.G && c.G == c.B; }), "Mono has no colour at all");

            // Nothing may become unreadable in any theme: text on every kind of surface, and each pet's speech bubble.
            foreach (var (id, name) in Pastel.Themes)
            {
                Brush C(string hex) => Pastel.Sample(id, hex); double Ratio(string text, string surface) => Pastel.Contrast(C(text), C(surface));
                // Window and card backgrounds carry small text (4.5); buttons and title bars carry larger or bold text (4).
                foreach (string surface in new[] { "#FFF1F8", "#FFFAFD", "#EAFBFC" }) Check(Ratio("#7E3A6C", surface) >= 4.5, $"{name}: body text is readable on {surface} ({Ratio("#7E3A6C", surface):0.0})");
                foreach (string surface in new[] { "#FFB9DC", "#B3EFF4", "#F9AED4", "#A6E9EF" }) Check(Ratio("#7E3A6C", surface) >= 4, $"{name}: button and title text is readable on {surface} ({Ratio("#7E3A6C", surface):0.0})");
                Check(Ratio("#2F8791", "#B3EFF4") >= 3 && Ratio("#E4479A", "#FFF1F8") >= 3, $"{name}: secondary text and headings are readable ({Ratio("#2F8791", "#B3EFF4"):0.0}, {Ratio("#E4479A", "#FFF1F8"):0.0})");
                Check(Pastel.Contrast(Brushes.White, C("#E4479A")) >= 3, $"{name}: white text is readable on the selected colour ({Pastel.Contrast(Brushes.White, C("#E4479A")):0.0})");
                Check(Ratio(DesktopOptions.StandardBubbleText, DesktopOptions.StandardBubbleBackground) >= 4.5 && Ratio(DesktopOptions.StandardBubbleText, DesktopOptions.StandardBubbleBackground2) >= 4.5, $"{name}: both speech bubbles are readable ({Ratio(DesktopOptions.StandardBubbleText, DesktopOptions.StandardBubbleBackground):0.0}, {Ratio(DesktopOptions.StandardBubbleText, DesktopOptions.StandardBubbleBackground2):0.0})");
                var first = ((SolidColorBrush)C(DesktopOptions.StandardBubbleBackground)).Color; var second = ((SolidColorBrush)C(DesktopOptions.StandardBubbleBackground2)).Color;
                Check(Math.Abs(first.R - second.R) + Math.Abs(first.G - second.G) + Math.Abs(first.B - second.B) >= 18, name + ": the two pets' bubbles look different");
            }
            Check(Pastel.Contrast(Pastel.Readable(Pastel.Original("#101010")), Pastel.Original("#101010")) > 10 && Pastel.Contrast(Pastel.Readable(Pastel.Original("#FFFFE0")), Pastel.Original("#FFFFE0")) > 10, "Fallback text colour is readable on dark and light bubbles");

            var bubbles = new DesktopOptions { BubbleBackground = "#FFF8FC", BubbleBackground2 = "nope", BubbleText2 = "#123456" }; bubbles.Normalize();
            Check(bubbles.BubbleBackground == DesktopOptions.StandardBubbleBackground && bubbles.BubbleBackground2 == DesktopOptions.StandardBubbleBackground2 && bubbles.BubbleText2 == "#123456", "Old standard bubble colour moves to the new one; custom colours are kept");

            string before = Storage.Root; Storage.UseTestRoot(Path.Combine(root, "error-log"));
            try
            {
                ErrorLog.Write("테스트", new InvalidOperationException("boom")); string log = File.ReadAllText(ErrorLog.FilePath);
                Check(log.Contains("테스트") && log.Contains("InvalidOperationException") && log.Contains("boom") && log.Contains("버전"), "Errors are written to the log file with context");
                File.WriteAllText(ErrorLog.FilePath, new string('x', 600 * 1024)); ErrorLog.Write("테스트", "after trim");
                Check(new FileInfo(ErrorLog.FilePath).Length < 400 * 1024 && File.ReadAllText(ErrorLog.FilePath).EndsWith("after trim\r\n\r\n"), "The log file is trimmed instead of growing without limit");
            }
            finally { Storage.UseTestRoot(before); }
        }
        finally { Pastel.Use("strawberry"); }
        return count;
    }
}
