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
        var shop = new PetGame(Path.Combine(root, "game-shop"));
        shop.Buy("pink-bow");
        Check(shop.State.Coins == 5 && shop.State.Owned.Contains("pink-bow"), "Purchase deducts price and grants first-shop badge once");
        long afterBuy = shop.State.Coins;
        bool duplicateBuy = false;
        try { shop.Buy("pink-bow"); } catch (InvalidOperationException) { duplicateBuy = true; } Check(duplicateBuy && shop.State.Coins == afterBuy, "Duplicate purchase blocked");
        bool locked = false; try { shop.Buy("crown"); } catch (InvalidOperationException) { locked = true; } Check(locked && !shop.State.Owned.Contains("crown"), "Level requirement enforced");
        bool unowned = false; try { shop.Equip("glasses"); } catch (InvalidOperationException) { unowned = true; } Check(unowned, "Cannot equip unowned item");
        shop.Equip("pink-bow"); Check(shop.State.Equipped["head"] == "pink-bow", "Owned item can be equipped"); AddWork(shop, 60000); shop.Buy("star-pin"); shop.Equip("star-pin");
        Check(shop.State.Equipped.Count == 1 && shop.State.Equipped["head"] == "star-pin" && shop.State.Owned.Count == 2, "One equipped item per slot");
        shop.Remove("head"); Check(shop.State.Equipped.Count == 0 && shop.State.Owned.Count == 2, "Unequip preserves inventory");
        shop.State.TotalXp = 75;
        bool poor = false;
        try { shop.Buy("glasses"); } catch (InvalidOperationException) { poor = true; } Check(poor && shop.State.Coins >= 0, "Insufficient funds never create negative wallet");
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
        outfit.Equipped["head"] = "crown";
        outfit.Owned.Add("unknown");
        outfit.AccessoryScale = double.NaN;
        outfit.Normalize();
        Check(outfit.Owned.Count == 0 && outfit.Equipped.Count == 0 && outfit.AccessoryScale == 1, "Malformed outfit safely normalized");
        outfit.Owned.Add("pink-bow");
        outfit.Equipped["head"] = "pink-bow";
        var layer = new AccessoryLayer { Outfit = outfit };
        layer.Measure(new Size(240, 180));
        layer.Arrange(new Rect(0, 0, 240, 180));
        layer.UpdateLayout();
        var bitmap = new RenderTargetBitmap(240, 180, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(layer);
        byte[] pixels = new byte[240 * 180 * 4];
        bitmap.CopyPixels(pixels, 240 * 4, 0);
        Check(Enumerable.Range(0, 240 * 180).Any(n => pixels[n * 4 + 3] > 0), "Accessory layer renders without altering avatar image");
        return checks;
    }
}
