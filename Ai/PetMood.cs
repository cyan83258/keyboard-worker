using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace KeyboardWorker;

public sealed record PetReply(string Text, string Mood);
public static class PetMood
{
    public static string DisplayText(string reply)
    {
        int bracket = reply.LastIndexOf('[');
        if (bracket >= 0)
        {
            string tail = reply[bracket..]; const string marker = "[pet-mood:";
            if (marker.StartsWith(tail, StringComparison.OrdinalIgnoreCase) || tail.StartsWith(marker, StringComparison.OrdinalIgnoreCase)) return reply[..bracket].TrimEnd();
        }
        return reply;
    }
    static readonly Regex Tag = new(@"\s*\[pet-mood:([a-z-]{1,24})\]\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static readonly string[] Moods = { "neutral", "happy", "sad", "angry", "surprised", "shy", "sleepy", "caring" };
    public static string Normalize(string? value) { value = (value ?? "").Trim().ToLowerInvariant(); return Array.IndexOf(Moods, value) >= 0 ? value : "neutral"; }
    // What each expression means, so the model picks by the feeling of this line instead of repeating one favourite.
    const string Guide = "\n- happy: 기쁨·신남·뿌듯함·웃음·칭찬·자랑\n- sad: 슬픔·아쉬움·시무룩·실망\n- angry: 화남·짜증·투덜거림·불만·핀잔·호통\n- surprised: 놀람·당황·뜻밖의 일\n- shy: 부끄러움·쑥스러움·민망함\n- sleepy: 졸림·피곤·나른함·지루함\n- caring: 걱정·위로·다정하게 챙김\n- neutral: 위 어느 것도 아닌 담담한 말\n캐릭터의 평소 성격이 아니라 '이 대사'에 담긴 감정을 기준으로 고르고, 같은 표정만 반복하지 마세요. neutral은 감정이 정말 드러나지 않을 때만 고릅니다. 사용자의 슬픔을 그대로 따라 하지 말고 위로할 때는 caring을 고르세요.";
    public const string Instruction = "\n[표정 표시 지시]\n이번 답변의 내용과 말투에 가장 잘 맞는 캐릭터의 표정 하나를 고르세요." + Guide + "\n자연스러운 답변을 쓴 뒤 맨 마지막 줄에 [pet-mood:선택값]을 붙이세요. 이 표식은 앱이 제거하므로 대사 안에서 설명하지 마세요.";
    public const string LineInstruction = "\n[표정 표시 지시]\n각 대사 끝에 그 대사의 감정에 맞는 표정 표식 [pet-mood:값]을 하나 붙이세요. 값은 다음 중 하나입니다." + Guide + "\n표식은 앱이 제거하므로 대사 안에서 설명하지 마세요.";
    public const string PairInstruction = "\n[표정 표시 지시]\n두 캐릭터 각각의 대사에 맞는 표정을 따로 고르세요. 값은 다음 중 하나입니다." + Guide + "\n최종 출력은 JSON 하나입니다: {\"first\":\"대표 친구 대사\",\"first_mood\":\"표정\",\"second\":\"둘째 친구 대사\",\"second_mood\":\"표정\"}";
    // Used only when the model did not say which expression fits.
    public static string Guess(string reply)
    {
        if (new[] { "위로", "괜찮아", "곁에", "힘들었", "안아", "걱정", "무리하지", "쉬어", "챙겨" }.Any(reply.Contains)) return "caring";
        if (new[] { "졸려", "졸리", "하품", "잠이 와", "피곤", "나른", "지루" }.Any(reply.Contains)) return "sleepy";
        if (new[] { "부끄", "쑥스", "수줍", "민망" }.Any(reply.Contains)) return "shy";
        if (new[] { "놀랐", "깜짝", "세상에", "뭐라고", "설마", "?!", "!?" }.Any(reply.Contains)) return "surprised";
        if (new[] { "화났", "화가 나", "흥!", "투덜", "짜증", "무례", "감히", "괘씸", "시끄러" }.Any(reply.Contains)) return "angry";
        if (new[] { "슬퍼", "슬프", "시무룩", "속상", "아쉽", "서운", "ㅠ" }.Any(reply.Contains)) return "sad";
        if (new[] { "신나", "기뻐", "행복", "헤헤", "고마워", "후후", "하하", "훌륭", "좋아", "최고", "ㅎㅎ", "♡" }.Any(reply.Contains)) return "happy";
        return "neutral";
    }
    public static PetReply Parse(string reply)
    {
        var match = Tag.Match(reply);
        if (match.Success)
        {
            return new(reply[..match.Index].Trim(), Normalize(match.Groups[1].Value));
        }
        return new(reply, Guess(reply));
    }
}
