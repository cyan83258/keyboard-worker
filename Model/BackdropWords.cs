using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyboardWorker;

// Words for places and times of day, so that a background can be found whatever it is called: the model may answer
// "sandy_beach_daytime", "바닷가" or "beach at noon" for the picture titled "sandy beach daytime". Also used to tell the model
// in Korean what an English title shows, and to follow the narration when the model names no background at all.
public static class BackdropWords
{
    // Korean: what the place is called when shown to the model. Words: how titles and talk name it. More specific places come first.
    static readonly (string Korean, string[] Words)[] places =
    {
        ("바닷가 버스 정류장", new[] { "bus stop", "버스 정류장", "정류장" }), ("바닷가", new[] { "beach", "seaside", "seashore", "coast", "ocean", "바닷가", "해변", "해수욕장", "모래사장", "백사장", "바다" }),
        ("강가", new[] { "riverside", "river", "강가", "강변", "강둑", "둔치" }), ("다리 밑", new[] { "under bridge", "bridge", "다리 밑", "다리 아래", "굴다리" }),
        ("학교 복도", new[] { "school hallway", "school corridor", "학교 복도" }), ("건물 복도", new[] { "hallway", "corridor", "복도" }),
        ("교실", new[] { "classroom", "교실" }), ("동아리방", new[] { "club room", "clubroom", "동아리방", "부실" }), ("놀이터", new[] { "playground", "놀이터" }),
        ("공원", new[] { "park", "공원", "벤치" }), ("기차 안", new[] { "inside train", "train", "기차 안", "전철 안", "지하철 안", "열차 안" }), ("역 승강장", new[] { "platform", "station", "승강장", "플랫폼", "기차역", "역 앞" }),
        ("철도 건널목", new[] { "railroad crossing", "crossing", "건널목" }), ("시골길", new[] { "country road", "시골길", "논길", "시골" }), ("상점가", new[] { "shopping arcade", "arcade", "shopping", "market", "상점가", "시장", "아케이드", "번화가" }),
        ("식당", new[] { "restaurant", "diner", "cafe", "식당", "레스토랑", "카페", "밥집" }), ("욕실", new[] { "bathroom", "bath", "욕실", "화장실", "목욕탕" }), ("지하실", new[] { "cellar", "basement", "지하실", "지하 창고" }),
        ("폐허가 된 방", new[] { "ruined", "ruin", "폐허", "폐가" }), ("침실", new[] { "bedroom", "침실", "침대" }), ("남자아이 방", new[] { "boy room", "boys room" }), ("여자아이 방", new[] { "girl room", "girls room" }),
        ("원룸", new[] { "single room", "원룸", "자취방" }), ("방", new[] { "room", "방 안", "내 방", "집 안", "집에" }), ("거리", new[] { "street", "road", "거리", "길거리", "골목" }),
        ("어지러운 효과", new[] { "dizziness" }), ("집중선 효과", new[] { "horizontal lines", "lines effect" }), ("마법 효과", new[] { "magic" })
    };
    static readonly (string Korean, string[] Words)[] times =
    {
        ("불 꺼짐", new[] { "light off", "불 꺼", "불을 끄" }), ("불 켜짐", new[] { "light on", "불 켜", "불을 켜" }), ("노을", new[] { "sunset", "dusk", "evening", "노을", "해 질", "해질", "석양", "저녁" }),
        ("밤", new[] { "night", "midnight", "dark", "밤", "한밤", "새벽" }), ("아침", new[] { "morning", "dawn", "아침" }), ("오후", new[] { "afternoon", "오후" }), ("낮", new[] { "daytime", "noon", "day", "낮", "대낮", "한낮" })
    };
    static string Plain(string text) { text = (text ?? "").ToLowerInvariant().Replace('_', ' ').Replace('-', ' '); int bracket = text.IndexOf('('); if (bracket > 0) text = text[..bracket]; return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)); }
    // An English word must stand alone ("park" is not in "sparkle"); a Korean one may be part of a longer word ("바닷가에서").
    static bool Has(string text, string word)
    {
        for (int at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
        {
            if (word[0] > 0x7f) return true;
            bool before = at == 0 || !char.IsLetter(text[at - 1]), after = at + word.Length >= text.Length || !char.IsLetter(text[at + word.Length]);
            if (before && after) return true;
        }
        return false;
    }
    static int Index((string Korean, string[] Words)[] table, string plain) { for (int i = 0; i < table.Length; i++) if (table[i].Words.Any(w => Has(plain, w)) || Has(plain, table[i].Korean)) return i; return -1; }
    static int Place(string plain) => Index(places, plain);
    static int Time(string plain) => Index(times, plain);
    // What a title shows, in Korean ("sandy beach night" -> "바닷가 밤"); "" for a title that is Korean already or says nothing known.
    public static string Gloss(string title)
    {
        if (title.Any(c => c >= 0xAC00 && c <= 0xD7A3)) return ""; string plain = Plain(title); int place = Place(plain), time = Time(plain);
        return place < 0 ? "" : places[place].Korean + (time < 0 ? "" : " " + times[time].Korean);
    }
    // The titles as the model is shown them: "sandy beach night(바닷가 밤), 내 방".
    public static string Listed(IEnumerable<string> titles) => string.Join(", ", titles.Select(t => Gloss(t) is { Length: > 0 } gloss ? t + "(" + gloss + ")" : t));
    // The title meant by what the model wrote; null when nothing fits.
    public static string? Find(IReadOnlyList<string> titles, string named)
    {
        string wanted = Plain(named); if (wanted.Length == 0 || wanted is "없음" or "none") return null;
        string squeezed = wanted.Replace(" ", "");
        var exact = titles.FirstOrDefault(t => Plain(t) == wanted) ?? titles.FirstOrDefault(t => Plain(t).Replace(" ", "") == squeezed) ?? titles.FirstOrDefault(t => Plain(Gloss(t)) == wanted);
        if (exact != null) return exact;
        if (wanted.Length >= 3 && titles.FirstOrDefault(t => Plain(t).Contains(wanted) || (Plain(t).Length >= 3 && wanted.Contains(Plain(t)))) is { } partial) return partial;
        return Best(titles, Place(wanted), Time(wanted), -1);
    }
    // The best title for a place: the wanted time of day if there is one, else the time kept from before, else the first.
    static string? Best(IReadOnlyList<string> titles, int place, int time, int kept)
    {
        if (place < 0) return null; var there = titles.Where(t => Place(Plain(t)) == place).ToArray(); if (there.Length == 0) return null;
        return there.FirstOrDefault(t => time >= 0 && Time(Plain(t)) == time) ?? there.FirstOrDefault(t => kept >= 0 && Time(Plain(t)) == kept) ?? there[0];
    }
    // Where the narration says the scene is, when the model named no background. Null when it names no place there is a
    // picture of, or the picture on show is of that place already.
    public static string? Guess(IReadOnlyList<string> titles, string narration, string current)
    {
        string told = Plain(narration.Replace('(', ' ')); if (told.Length == 0) return null; int place = Place(told), now = Place(Plain(current));
        if (place < 0) return null; int time = Time(told);
        if (place == now && (time < 0 || time == Time(Plain(current)))) return null;
        string? best = Best(titles, place, time, Time(Plain(current))); return best == null || string.Equals(best, current, StringComparison.OrdinalIgnoreCase) ? null : best;
    }
}
