using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

public static class GameTests
{
    public static int Run(string root)
    {
        int checks = 0; string day = WorkTimeStore.Today;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
        void AddWork(PetGame game, long ms, string? date = null) { while (ms > 0) { long delta = Math.Min(10000, ms); game.Work(delta, date ?? day); ms -= delta; } }
        var game = new PetGame(Path.Combine(root, "game-growth")); Check(game.Level == 1 && game.State.Coins == 20, "Game starts with welcome coins");
        AddWork(game, 59999); Check(game.State.TotalXp == 0 && game.State.Coins == 20, "No reward before confirmed minute"); game.Work(1, day);
        Check(game.State.TotalXp == 10 && game.State.Coins == 30 && game.State.Badges.Contains("first-minute"), "Minute XP, coins and one-time badge");
        AddWork(game, 240000); Check(game.State.DailyWorkMs == 300000 && game.QuestReady(PetGame.Quests[0]), "Daily goal progress from confirmed work");
        var claim = game.Claim("work5", day); Check(claim.LevelUp && game.Level == 2, "Quest can level up character");
        long balance = game.State.Coins, xp = game.State.TotalXp; bool duplicate = false; try { game.Claim("work5", day); } catch (InvalidOperationException) { duplicate = true; }
        Check(duplicate && balance == game.State.Coins && xp == game.State.TotalXp, "Daily reward cannot be claimed twice");
        bool premature = false; try { game.Claim("work15", day); } catch (InvalidOperationException) { premature = true; } Check(premature, "Unfinished daily goal cannot be claimed");
        long life = game.State.LifetimeWorkMs;
        var disabled = game.State.Snapshot();
        disabled.Enabled = false;
        game.Options(disabled);
        AddWork(game, 60000);
        Check(game.State.LifetimeWorkMs == life, "Disabled growth earns no rewards");
        disabled.Enabled = true;
        game.Options(disabled);
        game.RollDay("next-day");
        Check(game.State.DailyWorkMs == 0 && game.State.ClaimedQuests.Count == 0 && game.State.TotalXp == xp, "Daily reset preserves lifetime level");
        TestGear.Use(root); var shop = new PetGame(Path.Combine(root, "game-shop"));
        shop.Buy(TestGear.Bow);
        Check(shop.State.Coins == 5 && shop.State.Owned.Contains(TestGear.Bow), "Purchase deducts price and grants first-shop badge once");
        long afterBuy = shop.State.Coins;
        bool duplicateBuy = false;
        try { shop.Buy(TestGear.Bow); } catch (InvalidOperationException) { duplicateBuy = true; } Check(duplicateBuy && shop.State.Coins == afterBuy, "Duplicate purchase blocked");
        bool locked = false; try { shop.Buy(TestGear.Crown); } catch (InvalidOperationException) { locked = true; } Check(locked && !shop.State.Owned.Contains(TestGear.Crown), "Level requirement enforced");
        bool unowned = false; try { shop.Equip(TestGear.Glasses); } catch (InvalidOperationException) { unowned = true; } Check(unowned, "Cannot equip unowned item");
        shop.Equip(TestGear.Bow); Check(shop.State.Equipped["head"] == TestGear.Bow, "Owned item can be equipped"); AddWork(shop, 60000); shop.Buy(TestGear.Pin); shop.Equip(TestGear.Pin);
        Check(shop.State.Equipped.Count == 1 && shop.State.Equipped["head"] == TestGear.Pin && shop.State.Owned.Count == 2, "One equipped item per slot");
        shop.Remove("head"); Check(shop.State.Equipped.Count == 0 && shop.State.Owned.Count == 2, "Unequip preserves inventory");
        shop.State.TotalXp = 75;
        bool poor = false;
        try { shop.Buy(TestGear.Glasses); } catch (InvalidOperationException) { poor = true; } Check(poor && shop.State.Coins >= 0, "Insufficient funds never create negative wallet");
        var friend = new PetGame(Path.Combine(root, "game-bond")); Check(friend.Pat(0, day), "First pat increases bond"); Check(!friend.Pat(9999, day), "Pat cooldown enforced");
        for (int n = 1; n < 12; n++) friend.Pat(n * 10000, day); Check(friend.State.Bond == 10 && friend.State.DailyPats == 10, "Daily pat reward cap");
        Check(friend.QuestReady(PetGame.Quests.Single(q => q.Id == "pat3")), "Pat quest can be completed");
        friend.Save(true);
        OrderedStore.FlushAsync().GetAwaiter().GetResult();
        var reload = new PetGame(Path.Combine(root, "game-bond"));
        Check(reload.State.Bond == 10 && reload.State.DailyPats == 10, "Game progress survives reload");
        shop.Save(true);
        OrderedStore.FlushAsync().GetAwaiter().GetResult();
        var closet = new PetGame(Path.Combine(root, "game-shop"));
        Check(closet.State.Owned.SetEquals(shop.State.Owned) && closet.State.TotalXp == 75, "Purchased inventory and level persist");
        Check(PetGame.LevelOf(74) == 1 && PetGame.LevelOf(75) == 2 && PetGame.LevelOf(175) == 3 && PetGame.LevelOf(long.MaxValue) == 100, "Level thresholds and cap");
        Check(game.ChatContext().Contains("실제 돈이 아니다") && game.ChatContext().Contains("친밀도"), "Game chat context is factual and local");
        var privateOptions = game.State.Snapshot(); privateOptions.ShareWithAi = false; game.Options(privateOptions); Check(game.ChatContext() == "", "Game chat context can be disabled");
        var outfit = new GameState();
        outfit.Equipped["head"] = TestGear.Crown;
        outfit.Owned.Add("unknown");
        outfit.AccessoryScale = double.NaN;
        outfit.Normalize();
        Check(outfit.Owned.Count == 0 && outfit.Equipped.Count == 0 && outfit.AccessoryScale == 1, "Malformed outfit safely normalized");
        outfit.Owned.Add(TestGear.Bow);
        outfit.Equipped["head"] = TestGear.Bow;
        var dot = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)255, 64).ToArray(), 16); dot.Freeze();
        var layer = new AccessoryLayer { Outfit = outfit, CustomImage = _ => dot };
        layer.Measure(new Size(240, 180));
        layer.Arrange(new Rect(0, 0, 240, 180));
        layer.UpdateLayout();
        var bitmap = new RenderTargetBitmap(240, 180, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(layer);
        byte[] pixels = new byte[240 * 180 * 4];
        bitmap.CopyPixels(pixels, 240 * 4, 0);
        Check(Enumerable.Range(0, 240 * 180).Any(n => pixels[n * 4 + 3] > 0), "Accessory layer renders without altering avatar image");
        // Where an accessory sits can be asked for and moved by hand; the user's place for a shipped one is kept apart from the list it came with
        var spot = layer.Spot("head", outfit.Wearing(TestGear.Bow)!)!.Value; Check(layer.Hit(spot.Centre) == TestGear.Bow && layer.Hit(new Point(2, 170)) == null && Math.Abs(spot.Centre.X - 120) < .01 && Math.Abs(spot.Size.Width - 84) < .01, "The accessory under a point is found where it is drawn");
        outfit.Places[TestGear.Bow] = new GearPlace(70, 10, 5, 400); outfit.Places["shop-gone"] = new GearPlace(1, 1, 1, 1); outfit.Normalize();
        var moved = layer.Spot("head", outfit.Wearing(TestGear.Bow)!)!.Value;
        Check(outfit.Places.Count == 1 && outfit.Places[TestGear.Bow].Rotation == 180 && Math.Abs(moved.Size.Width - 168) < .01 && moved.Centre.X > spot.Centre.X && moved.Centre.Y > spot.Centre.Y && ShopGear.Items.First(g => g.Id == TestGear.Bow).WidthPercent == 35 && outfit.Snapshot().Places.Count == 1, "A place of the user's own moves a shipped accessory without changing the one that came with the program");
        outfit.Places[TestGear.Bow] = outfit.Places[TestGear.Bow] with { Flip = true }; outfit.Normalize(); var mirrored = new RenderTargetBitmap(240, 180, 96, 96, PixelFormats.Pbgra32); layer.InvalidateVisual(); layer.UpdateLayout(); mirrored.Render(layer);
        Check(outfit.Wearing(TestGear.Bow)!.Flip && outfit.Places[TestGear.Bow].Flip && !ShopGear.Items.First(g => g.Id == TestGear.Bow).Flip && ShopGear.Describe(outfit.Wearing(TestGear.Bow)!, "bow_20.png").Contains("\"flip\":true") && layer.Spot("head", outfit.Wearing(TestGear.Bow)!)!.Value.Centre == moved.Centre, "An accessory can be mirrored where it sits, and the choice is kept with its place");
        var placing = new PetGame(Path.Combine(root, "game-place")); var draft = placing.State.Snapshot(); draft.Places[TestGear.Pin] = new GearPlace(50, -3, 2, 15); placing.Place(draft); OrderedStore.FlushAsync().GetAwaiter().GetResult();
        Check(new PetGame(Path.Combine(root, "game-place")).State.Places[TestGear.Pin] == new GearPlace(50, -3, 2, 15), "Places worked out by hand are saved");
        // Two pets, two outfits: what is owned is shared, what each wears and where it sits is its own
        var two = new PetGame(Path.Combine(root, "game-two")); two.State.Coins = 1000; two.Buy(TestGear.Bow); two.Buy(TestGear.Pin); two.Equip(TestGear.Bow); two.Equip(TestGear.Pin, true);
        Check(two.State.For(false).Equipped["head"] == TestGear.Bow && two.State.For(true).Equipped["head"] == TestGear.Pin && two.State.Equipped["head"] == TestGear.Bow, "Each pet wears its own accessory in the same place");
        var onSecond = two.State.For(true); onSecond.Places[TestGear.Pin] = new GearPlace(70, 5, -5, 0, true); two.Place(onSecond, true);
        Check(two.State.For(true).Wearing(TestGear.Pin) is { WidthPercent: 70, Flip: true } && two.State.For(false).Wearing(TestGear.Pin) is { WidthPercent: 35, Flip: false } && two.State.Places.Count == 0 && two.State.Second!.Places.Count == 1, "An accessory placed on the second pet stays where it was on the main pet");
        var tall = two.State.For(true); tall.HeadY = 30; tall.AccessoryScale = 1.5; two.Options(tall, true);
        Check(two.State.For(true).HeadY == 30 && two.State.For(true).AccessoryScale == 1.5 && two.State.HeadY == 15 && two.State.AccessoryScale == 1, "The anchors and the scale are each pet's own");
        two.Remove("head"); Check(!two.State.Equipped.ContainsKey("head") && two.State.For(true).Equipped["head"] == TestGear.Pin, "Taking it off one pet leaves the other dressed");
        two.Save(true); OrderedStore.FlushAsync().GetAwaiter().GetResult(); var twoAgain = new PetGame(Path.Combine(root, "game-two"));
        Check(twoAgain.State.For(true).Equipped["head"] == TestGear.Pin && twoAgain.State.For(true).Wearing(TestGear.Pin)!.WidthPercent == 70 && twoAgain.State.For(true).HeadY == 30 && !twoAgain.State.Equipped.ContainsKey("head"), "Both outfits are saved");
        twoAgain.DeleteOwned(TestGear.Pin); Check(!twoAgain.State.For(true).Equipped.ContainsKey("head") && twoAgain.State.Second!.Places.Count == 0 && twoAgain.State.Owned.Contains(TestGear.Bow), "What is deleted from the wardrobe is taken off both");
        var old = new GameState(); old.Owned.Add(TestGear.Bow); old.Equipped["head"] = TestGear.Bow; old.HeadY = 22; old.Normalize();
        Check(old.Second is { HeadY: 22 } && old.Second.Equipped["head"] == TestGear.Bow && !ReferenceEquals(old.Second.Equipped, old.Equipped) && old.Snapshot().Second is { } copied && !ReferenceEquals(copied, old.Second), "Settings from before start the second pet in what the main pet wears, then the two are apart");
        var mine = new CustomGear { Name = "내 리본", Slot = "head", ImagePath = @"C:\pictures\ribbon.png" }; twoAgain.RegisterCustom(mine, true); var moved2 = twoAgain.State.For(false); moved2.Places[mine.Id] = new GearPlace(90, 1, 2, 3); twoAgain.Place(moved2);
        Check(twoAgain.State.Wearing(mine.Id)!.WidthPercent == 90 && twoAgain.State.For(true).Wearing(mine.Id)!.WidthPercent == 35 && twoAgain.State.CustomItems.Single().WidthPercent == 35, "The user's own gear is placed per pet too, and keeps the place it was registered with");
        twoAgain.RegisterCustom(mine with { WidthPercent = 50 }, false); Check(twoAgain.State.Wearing(mine.Id)!.WidthPercent == 50 && !twoAgain.State.Equipped.ContainsKey("head"), "Registering it again starts from the numbers given");
        return checks;
    }
}
