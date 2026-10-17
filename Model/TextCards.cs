using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KeyboardWorker;

// The special messages of the text messages, written as one line in brackets so that the user and the model use the same
// form: "[음성: …]" a voice message, "[사진: …]" a photo or video described, "[선물: …]" a gift, "[송금: 30000 | 메모]" money
// sent, "[있었던 일: …]" what happened since (told by the user, shown as a note), "[상태: …]" what the character is doing
// now (not a message: shown under the name).
public sealed record TextCard(string Kind, string Text, string Memo = "");

public static class TextCards
{
    public const string Voice = "음성", Photo = "사진", Gift = "선물", Money = "송금", Event = "있었던 일", Status = "상태";
    // What the user can send from the "+" menu, with what its box asks for.
    public static readonly (string Kind, string Name, string Ask)[] Kinds =
    {
        (Voice, "음성 메시지", "음성으로 보낼 말"), (Photo, "사진 · 영상", "사진이나 영상에 담긴 장면"), (Gift, "선물", "보낼 선물"), (Money, "송금", "금액 (숫자)"), (Event, "있었던 일", "그사이 있었던 일 (예: 다음 날 저녁, 둘이 영화를 보고 헤어졌다)")
    };
    static readonly Regex card = new(@"^\s*\[(음성|사진|선물|송금|있었던 일|상태)\s*[:：]\s*(.+?)\]\s*$", RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
    // The card a message is, or null for plain words.
    public static TextCard? Read(string message)
    {
        if (card.Match(message) is not { Success: true } m) return null;
        string kind = m.Groups[1].Value, text = m.Groups[2].Value.Trim();
        if (kind != Money) return text.Length > 0 ? new(kind, text) : null;
        var parts = text.Split(new[] { '|', '；', ';' }, 2); string amount = new(parts[0].Where(char.IsDigit).ToArray());
        return amount.Length is > 0 and <= 12 ? new(kind, long.Parse(amount).ToString("N0") + "원", parts.Length > 1 ? parts[1].Trim() : "") : null;
    }
    public static string Write(string kind, string text, string memo = "") => kind == Money ? $"[{Money}: {text.Trim()}" + (memo.Trim().Length > 0 ? " | " + memo.Trim() : "") + "]" : $"[{kind}: {text.Replace("\r", "").Replace("\n", " ").Replace("]", ")").Trim()}]";
    // A voice message lasts about as long as it takes to say.
    public static string Length(string said) { int seconds = Math.Clamp((int)Math.Ceiling(said.Length / 4.5), 1, 600); return $"{seconds / 60}:{seconds % 60:00}"; }
    // What the character said it is doing now: the last status in the thread ("" = none said).
    public static string Now(IReadOnlyList<ChatTurn> turns)
    {
        for (int i = turns.Count - 1; i >= 0; i--)
            foreach (string line in turns[i].Assistant.Split('\n').Reverse()) if (Read(line) is { Kind: Status } status) return status.Text;
        return "";
    }
    // For the model: what the cards mean, and which it may send.
    public const string Rules =
        "\n- 한 줄에 아래 형식 하나만 적으면 특별한 메시지가 됩니다. 사용자도 같은 형식으로 보냅니다."
        + "\n  [음성: 말한 내용] 음성 메시지 · [사진: 사진이나 영상에 담긴 장면] 사진/영상 · [선물: 선물 설명] 선물 · [송금: 금액 | 메모] 송금"
        + "\n- 사용자가 보낸 음성·사진·선물·송금에는 그 내용에 맞게, 이 캐릭터답게 반응합니다. 사진은 설명된 장면을 본 것으로 여깁니다."
        + "\n- 당신도 꼭 맞는 순간에만 가끔 음성·사진·선물·송금을 보낼 수 있습니다. 대부분의 답장은 글자 메시지만으로 씁니다."
        + "\n- [있었던 일: …]은 사용자가 알려 준, 그사이 흐른 시간과 있었던 일입니다. 그 일이 있었던 것으로 알고 이어 가되, 그 문장을 그대로 따라 읽지 않습니다."
        + "\n- 지금 하는 일이나 기분이 달라졌다면 답장 맨 끝 줄에 [상태: 짧은 상태]를 적을 수 있습니다 (예: [상태: 영화 보는 중]). 메시지로 보이지 않고 이름 아래에만 보입니다. 바뀌었을 때만 가끔 씁니다.";
}
