using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KeyboardWorker;

// Names of standing pictures and backgrounds: read from file names, offered to the AI to choose from, and read back from its answer.
public static class StageTags
{
    public const string Default = "기본";
    // Usual file names in English or Korean and the name shown for them. Any other name is kept as the user wrote it.
    static readonly (string Name, string[] Words)[] known =
    {
        (Default, new[] { "default", "normal", "neutral", "idle", "base", "기본", "평소", "무표정" }),
        ("웃음", new[] { "smile", "happy", "joy", "laugh", "웃음", "기쁨", "미소" }), ("슬픔", new[] { "sad", "cry", "tears", "슬픔", "눈물", "울음" }),
        ("화남", new[] { "angry", "mad", "anger", "화남", "분노" }), ("놀람", new[] { "surprise", "surprised", "shock", "놀람" }),
        ("부끄러움", new[] { "shy", "blush", "embarrassed", "부끄러움", "수줍음" }), ("졸림", new[] { "sleepy", "tired", "졸림", "피곤" }),
        ("다정", new[] { "caring", "kind", "gentle", "love", "다정", "걱정" }), ("진지", new[] { "serious", "진지" }), ("당황", new[] { "panic", "flustered", "당황" }),
        ("삐짐", new[] { "sulk", "pout", "삐짐", "토라짐" }), ("생각", new[] { "think", "thinking", "생각" }), ("자신만만", new[] { "smug", "proud", "confident", "자신만만", "득의" })
    };
    static readonly Dictionary<string, string> moods = new() { ["happy"] = "웃음", ["sad"] = "슬픔", ["angry"] = "화남", ["surprised"] = "놀람", ["shy"] = "부끄러움", ["sleepy"] = "졸림", ["caring"] = "다정", ["neutral"] = Default };
    public static string Clean(string? tag, string fallback = Default) { tag = SafeFiles.OneLine(tag, 20).Replace("[", "").Replace("]", "").Replace(",", " ").Trim(); return tag.Length == 0 ? fallback : tag; }
    // The file name without its extension and without a number at the end.
    static string Bare(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file).Trim(); string bare = Regex.Replace(name, @"[\s_\-\(\)\d]+$", "", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return bare.Length == 0 ? name : bare;
    }
    // "Smile_02.png" -> "웃음"; "교복 차림.png" -> "교복 차림".
    public static string FromFile(string file)
    {
        string bare = Bare(file), word = bare.ToLowerInvariant();
        // A name like "gil_smile" counts by its last part.
        string last = word.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? word;
        foreach (var (shown, words) in known) if (words.Contains(word) || words.Contains(last)) return shown;
        return Clean(bare);
    }
    // A background is called what its file is called: "교실_저녁.jpg" -> "교실 저녁".
    public static string BackdropTitle(string file) => Clean(Bare(file).Replace('_', ' ').Replace('-', ' '), "배경");

    // The picture for a name the AI chose; failing that, for the feeling guessed from the line; failing that, the everyday one.
    public static StageSprite? Choose(IReadOnlyList<StageSprite> sprites, string tag, string mood)
    {
        if (sprites.Count == 0) return null;
        StageSprite? Named(string name) => name.Length == 0 ? null : sprites.FirstOrDefault(s => string.Equals(s.Tag, name, StringComparison.OrdinalIgnoreCase));
        return Named(tag.Trim()) ?? Named(moods.GetValueOrDefault(mood, "")) ?? Named(Default) ?? sprites[0];
    }
    public static string[] Names(IEnumerable<StageSprite> sprites) => sprites.Select(s => s.Tag).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static string Instruction(IReadOnlyList<string> names) => names.Count < 2 ? "" :
        "\n[스탠딩 그림 고르기]\n화면에는 당신의 스탠딩 그림이 함께 보입니다. 이번 대사의 감정이나 상황에 가장 어울리는 그림 이름 하나를 다음에서 고르세요: " + string.Join(", ", names)
        + "\n대사를 다 쓴 뒤 맨 마지막 줄에 [sprite:이름] 형식으로 붙이세요. 목록에 있는 이름만 쓰고, 캐릭터의 평소 성격이 아니라 이 대사의 감정으로 고르며, 같은 그림만 반복하지 마세요. 이 표식은 앱이 지우니 대사에서 설명하지 마세요.";
    // The background is only changed when the scene really moves somewhere else, so most answers carry no mark for it.
    public static string BackdropInstruction(IReadOnlyList<string> titles, string current) => titles.Count < 2 ? "" :
        "\n[배경 바꾸기]\n화면 뒤에는 배경 그림이 있습니다. 지금 배경: " + (current.Length == 0 ? "없음" : current) + ". 고를 수 있는 배경: " + string.Join(", ", titles)
        + "\n대화 속 장소·시간·분위기가 지금 배경과 달라졌을 때만 [bg:이름]을 맨 마지막 줄에 붙여 바꾸세요. 그대로 두는 것이 보통이며 그럴 때는 이 표식을 쓰지 않습니다. 목록에 있는 이름만 쓰세요.";

    static readonly Regex marks = new(@"\s*\[(sprite|bg)\s*:\s*([^\]\r\n]{1,40})\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    // The line without its marks, and the names in them ("" when there is none).
    public static (string Text, string Sprite, string Backdrop) Read(string reply)
    {
        string sprite = "", backdrop = "";
        string text = marks.Replace(reply, match => { if (match.Groups[1].Value.Equals("bg", StringComparison.OrdinalIgnoreCase)) backdrop = match.Groups[2].Value.Trim(); else sprite = match.Groups[2].Value.Trim(); return ""; });
        return (text.Trim(), sprite, backdrop);
    }
    // While an answer is still arriving: without finished marks, and without one that has only partly come in.
    public static string Display(string partial)
    {
        partial = marks.Replace(partial, ""); int bracket = partial.LastIndexOf('['); if (bracket < 0) return partial;
        string tail = partial[bracket..].ToLowerInvariant();
        foreach (string start in new[] { "[sprite:", "[bg:" }) if (start.StartsWith(tail) || tail.StartsWith(start)) return partial[..bracket].TrimEnd();
        return partial;
    }
}
