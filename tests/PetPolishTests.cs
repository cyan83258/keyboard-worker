using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

public static class PetPolishTests
{
    public static int Run(string root)
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        BitmapSource Bitmap(int w, int h, double dpi = 96) => BitmapSource.Create(w, h, dpi, dpi, PixelFormats.Bgra32, null, new byte[w * h * 4], w * 4);
        var idle = Bitmap(100, 100); var left = Bitmap(125, 100); var right = Bitmap(140, 112, 144);
        var image = new Image { Stretch = Stretch.Uniform };
        var panel = new Grid { Width = 260, Height = 200 }; panel.Children.Add(image);
        PetImageLayout.Apply(image, 260, 200, idle);
        Rect Bounds(BitmapSource source)
        {
            image.Source = source; panel.Measure(new Size(260, 200)); panel.Arrange(new Rect(0, 0, 260, 200)); panel.UpdateLayout(); return VisualTreeHelper.GetDescendantBounds(image);
        }
        var idleBounds = Bounds(idle); var leftBounds = Bounds(left); var rightBounds = Bounds(right);
        Check(idleBounds.Width == 200 && leftBounds.Width <= idleBounds.Width + .01 && rightBounds.Width <= idleBounds.Width + .01, "Wide typing poses never enlarge beyond idle image viewport");
        Check(leftBounds.Height <= 200 && Math.Abs(leftBounds.Width / leftBounds.Height - 1.25) < .001, "Shared viewport preserves image proportions");
        Check(Math.Abs(leftBounds.Width - rightBounds.Width) < .01, "Image DPI does not change typing viewport width");
        Check(image.Width == 200 && image.Height == 200 && panel.DesiredSize == new Size(260, 200), "Pose replacement does not resize pet layout");
        var wideViewport = PetImageLayout.Viewport(260, 200, Bitmap(150, 100));
        Check(wideViewport.Width == 260 && Math.Abs(wideViewport.Height - 260 / 1.5) < .001, "Wide idle image sets stable reference canvas");
        var lifetime = new BubbleLifetime(); lifetime.Start(1000, 3);
        Check(!lifetime.Expired(3999, true) && lifetime.Expired(4000, true), "Bubble expires exactly at configured duration");
        Check(!lifetime.Expired(100000, false), "Automatic hiding can be disabled");
        lifetime.Streaming = true; Check(!lifetime.Expired(100000, true), "Streaming reply does not expire while being generated");
        lifetime.Start(100000, 5); Check(!lifetime.Expired(104999, true) && lifetime.Expired(105000, true), "Completed reply receives full reading interval");
        var options = new DesktopOptions { BubbleHideSeconds = 5000 };
        options.Normalize();
        Check(options.BubbleHideSeconds == 3600 && options.BubbleAutoHide, "Bubble duration limit and default automatic hiding");
        var oldOptions = new DesktopOptions(); oldOptions.Normalize(); Check(oldOptions.BubbleHideSeconds == null, "Old settings retain existing individual display times until configured");
        TestGear.Use(root); string folder = Path.Combine(root, "delete-owned"); var game = new PetGame(folder); game.State.Coins = 100;
        game.Buy(TestGear.Bow); game.Buy(TestGear.Pin); game.Equip(TestGear.Bow); long coins = game.State.Coins, xp = game.State.TotalXp;
        game.DeleteOwned(TestGear.Bow);
        Check(!game.State.Owned.Contains(TestGear.Bow) && !game.State.Equipped.Values.Contains(TestGear.Bow), "Delete purchased item removes ownership and wearing");
        Check(game.State.Owned.Contains(TestGear.Pin) && game.State.Coins == coins && game.State.TotalXp == xp, "Deletion preserves other items and does not refund or alter progress");
        OrderedStore.FlushAsync().GetAwaiter().GetResult(); var reloaded = new PetGame(folder);
        Check(!reloaded.State.Owned.Contains(TestGear.Bow) && !reloaded.State.Equipped.ContainsKey("head"), "Purchased item deletion persists after reload");
        reloaded.Buy(TestGear.Bow); Check(reloaded.State.Owned.Contains(TestGear.Bow) && reloaded.State.Coins == coins - 20, "Deleted shop item can be purchased again at normal price");
        bool rejected = false; try { reloaded.DeleteOwned("not-owned"); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Unknown inventory deletion rejected");
        var custom = new CustomGear { ImagePath = "test.png", Slot = "body" }; reloaded.RegisterCustom(custom, true); reloaded.DeleteOwned(custom.Id);
        Check(!reloaded.State.Owned.Contains(custom.Id) && !reloaded.State.CustomItems.Any(c => c.Id == custom.Id) && !reloaded.State.Equipped.ContainsKey("body"), "Custom and shop items share complete inventory removal");
        return count;
    }
}
