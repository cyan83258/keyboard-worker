using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KeyboardWorker;

// Gifts drawn with coins and given from the bag; and scenes for two characters.
public static class GiftTests
{
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        bool Throws(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
        string home = Path.Combine(root, "gifts"); Directory.CreateDirectory(home);

        // The gifts there are
        Check(GiftShop.All.Select(g => g.Name).Distinct().Count() == GiftShop.All.Length && Enumerable.Range(1, 5).All(stars => GiftShop.All.Any(g => g.Stars == stars)) && GiftShop.All.All(g => g.Glyph.Length > 0 && g.Description.Length > 0 && g.Stars is >= 1 and <= 5), "Every gift has its own name, a look and a rarity of one to five stars");
        Check(GiftShop.Chances.Sum() == 1000 && GiftShop.Chances.Zip(GiftShop.Chances.Skip(1), (common, rare) => common > rare).All(x => x) && GiftShop.Chances[4] == 10, "Rarer gifts are less likely; five stars come once in a hundred draws");
        Check(Enumerable.Range(1, 4).All(stars => GiftShop.Gain(stars) < GiftShop.Gain(stars + 1)) && GiftShop.Gain(1) == 4 && GiftShop.Gain(5) == 60 && GiftShop.Stars(3) == "★★★" && GiftShop.Stars(9) == "★★★★★", "A rarer gift is worth more closeness");
        Check(new[] { "꽃다발", "달콤한 간식", "작은 책", "음악 편지", "별 모양 장식" }.All(old => GiftShop.Find(old) != null && GiftShop.Favorites.Contains(old)) && GiftShop.Favorites.All(name => GiftShop.Find(name)!.Stars <= 2) && GiftShop.Find("없는 선물") == null && GiftShop.Find(null) == null, "The gifts from before are still there, and what a friend likes best is always something common");
        Check(GiftShop.All.Where(g => g.Stars == 4).All(g => g.Effect == "boost") && GiftShop.All.Where(g => g.Stars == 5).All(g => g.Effect == "keep") && GiftShop.All.Where(g => g.Stars <= 3).All(g => g.Effect == "") && GiftShop.EffectNote(GiftShop.Find("달의 반지")!).Contains("도감") && GiftShop.EffectNote(GiftShop.Find("꽃다발")!) == "", "Only the rare gifts have a power of their own");
        GiftItem At(int point, int atLeast = 1) => GiftShop.Roll(n => n == 1000 ? point : 0, atLeast);
        Check(At(0).Stars == 1 && At(549).Stars == 1 && At(550).Stars == 2 && At(829).Stars == 2 && At(830).Stars == 3 && At(949).Stars == 3 && At(950).Stars == 4 && At(989).Stars == 4 && At(990).Stars == 5 && At(999).Stars == 5, "The rarity of a draw follows the chances exactly");
        Check(At(0, 3).Stars == 3 && At(0, 4).Stars == 4 && At(999, 3).Stars == 5 && At(0, 9).Stars == 5, "A guaranteed draw is at least as rare as promised");

        // Drawing
        var book = new AdventureBook(home); var game = new PetGame(Path.Combine(home, "game")); game.State.Coins = 100;
        var one = book.Draw(game, 1, n => 0); Check(one.Single().Stars == 1 && game.State.Coins == 100 - GiftShop.DrawCost && book.Owned(one[0].Name) == 1 && book.State.GiftDraws == 1 && book.State.SinceRare == 1 && book.State.GiftsMet.Contains(one[0].Name), "One draw costs coins and puts a gift into the bag and the collection");
        Check(Throws(() => book.Draw(game, 10, n => 0)) && game.State.Coins == 50 && book.State.GiftDraws == 1, "Without enough coins nothing is drawn and nothing is spent");
        game.State.Coins = 100000; var ten = book.Draw(game, 10, n => 0);
        Check(ten.Count == 10 && game.State.Coins == 100000 - GiftShop.TenCost && ten.Take(9).All(g => g.Stars == 1) && ten[9].Stars == 3 && book.State.GiftDraws == 11, "Ten at once cost less than ten singles and always hold one of three stars or more");
        var lucky = book.Draw(game, 10, n => n == 1000 ? 990 : 0); Check(lucky.All(g => g.Stars == 5) && book.State.SinceRare == 0, "A lucky ten needs no guarantee");
        for (int i = 0; i < GiftShop.Pity - 1; i++) book.Draw(game, 1, n => 0);
        Check(book.State.SinceRare == GiftShop.Pity - 1 && book.Draw(game, 1, n => 0).Single().Stars == 4 && book.State.SinceRare == 0, "After many draws without a rare gift the next one is rare for certain");
        Check(book.Draw(game, 7, n => 0).Count == 1 && book.Draw(game, 99, n => 0).Count == 10, "Draws come in ones and tens");

        // Giving
        var a = book.Friend("a", "에이"); a.Favorite = "작은 책"; var b = book.Friend("b", "비"); b.Favorite = "향초";
        book.State.GiftBag.Clear(); book.State.GiftBag["작은 책"] = 2; book.State.GiftBag["꽃다발"] = 5; book.State.GiftBag["오르골"] = 1; book.State.GiftBag["별빛 향수"] = 1; book.State.GiftBag["달의 반지"] = 1; long coins = game.State.Coins;
        var loved = book.Give("a", "에이", "작은 책"); var plain = book.Give("b", "비", "꽃다발");
        Check(loved is { Gain: 8, Loved: true } && loved.Notice.Contains("특히 좋아해요") && loved.Notice.Contains("+8") && plain is { Gain: 4, Loved: false } && a.Bond == 8 && b.Bond == 4 && a.Discovered && !b.Discovered && book.Owned("작은 책") == 1 && book.Owned("꽃다발") == 4 && game.State.Coins == coins, "A gift leaves the bag, costs nothing more, and counts double when it is the friend's favourite");
        Check(Throws(() => book.Give("a", "에이", "향초")) && Throws(() => book.Give("a", "에이", "없는 선물")) && a.Gifts == 1, "A gift that is not in the bag cannot be given");
        var music = book.Give("a", "에이", "오르골"); Check(music.Gain == 15 && book.Owned("오르골") == 0 && !book.State.GiftBag.ContainsKey("오르골"), "The last one of a kind leaves no empty place in the bag");
        var scent = book.Give("a", "에이", "별빛 향수"); int after = a.Bond;
        Check(scent.Gain == 30 && scent.Notice.Contains("2배") && a.BoostDay == DateTime.Now.ToString("yyyy-MM-dd") && a.Gifts == 3 && Throws(() => book.Give("a", "에이", "꽃다발")) && book.Owned("꽃다발") == 4, "A friend takes three gifts a day; a four-star gift starts a day of doubled closeness");
        book.Pat("a", "에이", 100000); book.Pat("b", "비", 100000); Check(a.Bond == after + 2 && b.Bond == 5, "During that day everything the friend gains is doubled; other friends gain as usual");
        a.BoostDay = "2020-01-01"; book.Pat("a", "에이", 200000); Check(a.Bond == after + 3, "The next day closeness grows at its usual pace again");
        var ring = book.Give("b", "비", "달의 반지"); Check(ring.Gain == 60 && ring.Gift.Effect == "keep" && b.Bond == 65 && Affection.Stage(b.Bond) == 2, "A five-star gift is worth a great deal");
        book.Save(true); OrderedStore.FlushAsync().GetAwaiter().GetResult(); var reopened = new AdventureBook(home);
        Check(reopened.Owned("꽃다발") == 4 && reopened.Owned("작은 책") == 1 && reopened.State.GiftDraws == book.State.GiftDraws && reopened.State.GiftsMet.SetEquals(book.State.GiftsMet) && reopened.State.Friends["a"].BoostDay == "2020-01-01", "The bag, the collection and the count of draws are saved");
        string odd = Path.Combine(home, "odd"); Directory.CreateDirectory(odd); File.WriteAllText(Path.Combine(odd, "adventure-book.json"), "{\"GiftBag\":{\"꽃다발\":3,\"없는 선물\":9,\"향초\":0,\"오르골\":-2},\"GiftsMet\":[\"꽃다발\",\"가짜\"]}");
        var cleaned = new AdventureBook(odd).State; Check(cleaned.GiftBag.Keys.Single() == "꽃다발" && cleaned.GiftsMet.Single() == "꽃다발", "Gifts the program does not know are dropped from a saved bag");

        // A scene for two: what the user directs
        Check(PairStageTalk.Turns("매우 짧게") == 2 && PairStageTalk.Turns("중간") == 4 && PairStageTalk.Turns("매우 길게") == 8 && PairStageTalk.Turns("?") == 4, "The chosen length decides how many lines the two exchange");
        Check(PairStageTalk.Direction(PairStageTalk.Continue, "무시되는 글", "루카", "미오").Contains("이어 가세요") && !PairStageTalk.Direction(PairStageTalk.Continue, "무시되는 글", "루카", "미오").Contains("무시되는"), "Going on needs no words");
        string staged = PairStageTalk.Direction(PairStageTalk.Situation, " 비 오는 밤, 한 우산 ", "루카", "미오"), spoken = PairStageTalk.Direction(PairStageTalk.AsSecond, "(고개를 든다) 왔어?", "루카", "미오");
        Check(staged.StartsWith("[상황]\n비 오는 밤, 한 우산") && spoken.StartsWith("[미오의 대사·행동") && spoken.Contains("(고개를 든다) 왔어?") && spoken.Contains("루카의 반응부터") && PairStageTalk.Direction(PairStageTalk.AsFirst, "가자.", "루카", "미오").Contains("미오의 반응부터") && PairStageTalk.Direction(PairStageTalk.Situation, "  ", "루카", "미오").Contains("이어 가세요"), "A situation sets the scene; a line written for one of the two is taken as said, and the other answers");
        var options = new StageTalkOptions { Length = "길게" }; var cast = new PairCast(new Persona { Name = "루카" }, new Persona { Name = "미오" }, new[] { "기본", "웃음" }, Array.Empty<string>(), "\n[두 친구의 관계] 오래된 벗", new[] { "교실", "옥상" }, "교실");
        string instruction = PairStageTalk.Instruction(options, cast);
        Check(instruction.Contains("루카와(과) 미오 두 사람만") && instruction.Contains("사용자는 이 자리에 없고") && instruction.Contains("6번 안팎") && instruction.Contains("[두 친구의 관계] 오래된 벗") && instruction.Contains("루카의 그림: 기본, 웃음") && !instruction.Contains("미오의 그림") && instruction.Contains("지금 배경(교실)") && instruction.Contains("교실, 옥상") && instruction.Contains("\"view\"") && !instruction.Contains("{turns}"), "The scene's instruction names the two, keeps the user out, and lists what may be shown");
        Check(PairStageTalk.Instruction(options, cast with { FirstPictures = Array.Empty<string>(), Backdrops = new[] { "교실" } }).Contains("sprite: 빈 문자열") && PairStageTalk.Instruction(options, cast with { Backdrops = Array.Empty<string>() }).Contains("bg: 빈 문자열"), "Without pictures or a choice of backgrounds none are asked for");
        Check(new StageTalkOptions().PairPrompt == StageTalkOptions.StandardPairPrompt && new Func<StageTalkOptions>(() => { var blank = new StageTalkOptions { PairPrompt = " " }; blank.Normalize(); return blank; })().PairPrompt == StageTalkOptions.StandardPairPrompt, "The scene prompt can be edited and falls back to the standard one when emptied");

        // A scene for two: what comes back
        var scene = PairStageTalk.Parse("```json\n{\"lines\":[{\"who\":\"first\",\"text\":\"루카: (창밖을 보며) 늦었군.\",\"sprite\":\"화남\",\"view\":\"both\"},{\"who\":\"미오\",\"text\":\"미안, 길이 막혀서.\",\"sprite\":\"웃음\",\"view\":\"SOLO\"},{\"who\":\"second\",\"text\":\"  \"},\"잡음\",{\"who\":\"FIRST\",\"text\":\"…앉아라.\"}],\"bg\":\"옥상\"}\n```", "루카", "미오");
        Check(scene.Lines.Count == 3 && scene.Lines[0] == new PairLine(false, "(창밖을 보며) 늦었군.", "화남", true) && scene.Lines[1] == new PairLine(true, "미안, 길이 막혀서.", "웃음", false) && scene.Lines[2] == new PairLine(false, "…앉아라.", "", true) && scene.Backdrop == "옥상", "A scene is read line by line: who speaks, what is shown, whether both are on screen, and a new background");
        var chatty = PairStageTalk.Parse("좋아요, 장면입니다:\n{\"lines\":[{\"who\":\"second\",\"text\":\"왔어?\"}]}\n즐겁게 보세요!", "루카", "미오"); Check(chatty.Lines.Single() == new PairLine(true, "왔어?") && chatty.Backdrop == "", "Words around the JSON do not matter");
        var plain2 = PairStageTalk.Parse("루카: 늦었군.\n미오 ： 미안.\n  길이 막혔어.\n\n루카: …됐다.\n지나가던 사람: 안녕하세요", "루카", "미오");
        Check(plain2.Lines.Count == 3 && plain2.Lines[0] == new PairLine(false, "늦었군.") && plain2.Lines[1] == new PairLine(true, "미안. 길이 막혔어.") && plain2.Lines[2].Text == "…됐다. 지나가던 사람: 안녕하세요", "An answer that is not JSON is still read when it is written as a script");
        Check(Throws(() => PairStageTalk.Parse("죄송합니다, 쓸 수 없습니다.", "루카", "미오")) && Throws(() => PairStageTalk.Parse("{\"lines\":[]}", "루카", "미오")) && PairStageTalk.Parse("{\"lines\":[" + string.Join(",", Enumerable.Repeat("{\"who\":\"first\",\"text\":\"말\"}", 40)) + "]}", "루카", "미오").Lines.Count == 16, "An answer without lines is refused, and an endless one is cut");
        string script = PairStageTalk.Script(scene.Lines, "루카", "미오");
        Check(script == "루카: (창밖을 보며) 늦었군.\n미오: 미안, 길이 막혀서.\n루카: …앉아라." && PairStageTalk.FromScript(script, "루카", "미오").Select(l => (l.Second, l.Text)).SequenceEqual(scene.Lines.Select(l => (l.Second, l.Text))) && PairStageTalk.FromScript("", "루카", "미오").Count == 0, "A scene is kept as a script and read back the same");
        Check(Buddy.PairThread("a", "b") == "pair:a+b" && Buddy.PairThread("a", "b") != Buddy.PairThread("b", "a"), "Each pair, in each order, has a thread of scenes of its own");
        return count;
    }
}
