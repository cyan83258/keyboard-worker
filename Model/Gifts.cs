using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyboardWorker;

// A gift for a character. Stars: 1 (common) to 5 (very rare). Effect: "" none, "boost" = the character's closeness grows
// twice as fast for the rest of the day, "keep" = what the character says on receiving it is kept in the line collection.
public sealed record GiftItem(string Name, int Stars, string Glyph, string Description, string Effect = "");
public sealed record GiftResult(GiftItem Gift, int Gain, bool Loved, string Notice);

// The gifts there are, how likely each rarity is when drawing, and what a gift is worth.
public static class GiftShop
{
    public const int DrawCost = 50, TenCost = 450, PerDay = 3, Pity = 50;
    public static readonly GiftItem[] All =
    {
        new("꽃다발", 1, "✿", "수수한 들꽃 한 다발"), new("달콤한 간식", 1, "◍", "한입에 쏙 들어가는 간식"), new("작은 책", 1, "▤", "주머니에 들어가는 얇은 책"),
        new("따뜻한 차", 1, "♨", "김이 오르는 차 한 잔"), new("손편지", 1, "✉", "꾹꾹 눌러 쓴 짧은 편지"), new("사탕 한 줌", 1, "❂", "알록달록한 사탕"),
        new("음악 편지", 2, "♪", "좋아하는 노래를 적어 보낸 편지"), new("별 모양 장식", 2, "☆", "책상에 올려 두는 작은 별"), new("향초", 2, "♧", "은은한 향이 나는 초"),
        new("폭신한 담요", 2, "▦", "무릎에 덮기 좋은 담요"), new("수제 쿠키", 2, "✤", "모양이 조금 삐뚤어진 쿠키"),
        new("오르골", 3, "♬", "태엽을 감으면 조용히 노래하는 상자"), new("은빛 책갈피", 3, "❖", "읽던 곳을 지켜 주는 책갈피"), new("손뜨개 목도리", 3, "❄", "한 코 한 코 직접 뜬 목도리"),
        new("별빛 향수", 4, "✧", "뿌리면 하루 종일 기분이 좋아지는 향수", "boost"), new("추억의 사진첩", 4, "❏", "함께한 순간을 담아 둘 사진첩", "boost"),
        new("달의 반지", 5, "☾", "달빛을 가둔 듯한 반지", "keep"), new("소원의 별", 5, "✦", "소원 하나를 들어준다는 별", "keep")
    };
    // Chances per thousand draws, by stars (index 0 = one star).
    public static readonly int[] Chances = { 550, 280, 120, 40, 10 };
    static readonly int[] gains = { 4, 8, 15, 30, 60 };
    public static GiftItem? Find(string? name) => All.FirstOrDefault(g => g.Name == name);
    public static int Gain(int stars) => gains[Math.Clamp(stars, 1, 5) - 1];
    public static string Stars(int stars) => new string('★', Math.Clamp(stars, 1, 5));
    public static string EffectNote(GiftItem gift) => gift.Effect == "boost" ? "받은 날 하루 동안 그 친구의 친밀도가 2배로 올라요" : gift.Effect == "keep" ? "받았을 때의 한마디가 대사 도감에 저절로 담겨요" : "";
    // What a new friend may turn out to like best: something that is easy to come by.
    public static readonly string[] Favorites = All.Where(g => g.Stars <= 2).Select(g => g.Name).ToArray();
    // One draw. atLeast: the lowest rarity allowed (the pity rule and the tenth of ten use it). roll(n) gives 0..n-1.
    public static GiftItem Roll(Func<int, int> roll, int atLeast = 1)
    {
        int stars = 5, point = roll(1000);
        for (int i = 0, sum = 0; i < Chances.Length; i++) { sum += Chances[i]; if (point < sum) { stars = i + 1; break; } }
        stars = Math.Max(stars, Math.Clamp(atLeast, 1, 5)); var pool = All.Where(g => g.Stars == stars).ToArray(); return pool[roll(pool.Length)];
    }
}
