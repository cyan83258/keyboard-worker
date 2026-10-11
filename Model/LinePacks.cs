using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyboardWorker;

// Ready-made sets of lines in one tone, bought with coins and then added to any pet's line book (where they can be rewritten).
public sealed record LinePack(string Id, string Name, string Description, int Price, IReadOnlyDictionary<string, string> Lines);
public static class LinePacks
{
    // The packs that come with the program; enabled mod files add more.
    public static LinePack[] All => BuiltIn.Concat(ModStore.Current.LinePacks).ToArray();
    public static readonly LinePack[] BuiltIn =
    {
        new("warm", "다정한 응원 팩", "포근하게 챙겨 주는 말투", 900, new Dictionary<string, string>
        {
            ["nudge"] = "천천히 해도 괜찮아. 나 여기 있어.\n잠깐 멈춘 거지? 숨 고르고 다시 가자.",
            ["pat"] = "따뜻하다… 조금만 더 해 줘.\n{user} 손길이면 힘이 나.",
            ["random"] = "오늘 정말 잘하고 있어.\n힘들면 말해. 같이 쉬자.\n[3+] {user}가 애쓰는 거, 내가 다 보고 있어.",
            ["greet-morning"] = "잘 잤어? 오늘도 무리하지 말고 해 보자.", ["greet-night"] = "이제 그만 자자. 내일의 {user}가 고마워할 거야.",
            ["rest-start"] = "잘 생각했어. 푹 쉬고 와.", ["rest-end"] = "쉬고 오니까 얼굴이 훨씬 좋아 보여.",
            ["long-work"] = "{time}이나 했어? 대단해. 물 한 잔 마시자.", ["stage-up"] = "'{stage}'… 그 말이 참 좋다. 고마워, {user}."
        }),
        new("tsun", "츤데레 팩", "퉁명스럽지만 은근히 챙기는 말투", 1200, new Dictionary<string, string>
        {
            ["nudge"] = "뭐 해, 손 멈췄잖아.\n딱히 걱정돼서 그러는 건 아니거든.",
            ["pat"] = "…그, 그만해. 머리 헝클어져.\n흥, 이번만 봐주는 거야.",
            ["random"] = "따, 딱히 네가 보고 싶어서 있는 건 아니야.\n…잘하고 있네. 그냥 그렇다고.\n[4+] 너 없으면 좀… 심심하긴 해.",
            ["greet-morning"] = "늦었잖아. …기다린 건 아니야.", ["greet-night"] = "아직도 안 자? 쓰러져도 난 몰라. …얼른 자.",
            ["game"] = "{app}? 하, 팔자 좋네.", ["long-game"] = "{time}째야. 적당히 해, 진짜.",
            ["rest-start"] = "쉬어. 쓰러지면 귀찮으니까.", ["sulk"] = "…{partner}랑 실컷 놀아. 난 상관없으니까.", ["makeup"] = "…다음부터는 나도 불러."
        }),
        new("playful", "장난꾸러기 팩", "까불까불 놀리는 말투", 900, new Dictionary<string, string>
        {
            ["nudge"] = "어라? 딴짓하다 걸렸지~?\n손가락 파업했어? 협상할까?",
            ["pat"] = "히히, 간지러워!\n한 번 더! 한 번 더!",
            ["random"] = "심심한데 나랑 눈싸움할래?\n방금 하품한 거 다 봤다~\n[3+] {user} 놀리는 게 제일 재밌어.",
            ["greet-day"] = "짜잔~ 나 왔어! 아니지, {user}가 왔구나!", ["work"] = "오~ {app}! 일하는 척 멋있다?",
            ["game"] = "{app}다! 나도 시켜 줘!", ["music"] = "「{title}」! 같이 흔들자~",
            ["rest-end"] = "쉬는 시간 끝! 자, 일하러 가자~ (나는 구경)", ["anniversary"] = "{event}이다! 케이크는 어디 있어?"
        }),
        new("night", "심야 감성 팩", "조용하고 차분한 밤의 말투", 1100, new Dictionary<string, string>
        {
            ["random"] = "조용하네. 이런 시간도 나쁘지 않아.\n키보드 소리가 빗소리 같아.\n[4+] 이 고요함을 {user}랑 나눌 수 있어서 좋아.",
            ["greet-evening"] = "해가 졌네. 오늘 하루도 여기까지 왔구나.", ["greet-night"] = "밤이 깊었어. 별이 잘 보이는 시간이야.\n늦은 밤의 {user}는 조금 지쳐 보여.",
            ["nudge"] = "생각이 길어지는 밤이구나.", ["music"] = "「{title}」… 밤에 어울리는 곡이네.",
            ["long-work"] = "{time}째. 밤공기 한 번 쐬고 오는 건 어때.", ["rest-start"] = "눈을 감고 잠깐 쉬어. 내가 지키고 있을게."
        }),
        new("couple", "커플 대화 팩", "두 친구가 주고받는 대화와 질투·화해", 1500, new Dictionary<string, string>
        {
            ["pair"] = "오늘도 같이 있네. / 당연하지, 어디 가겠어. / …그 말 좋다.\n{user} 좀 봐, 또 집중했어. / 방해하지 말자. / 조용히 응원하는 거야.\n내 자리 좁아. / 그럼 더 가까이 와. / …바보.\n이따 뭐 할래? / 너 하고 싶은 거. / 그럼 계속 이렇게 있자.",
            ["sulk"] = "{partner}한테만 다정하네…\n나도 말 걸어 주면 좋겠는데.", ["makeup"] = "…알았어. 화 풀게.\n다시 봐 주니까 좋다.",
            ["anniversary"] = "{event}이래. 우리 셋이 같이 축하하자.", ["stage-up"] = "우리도, {user}도 조금씩 가까워지고 있어."
        })
    };
    public static LinePack? Find(string id) => All.FirstOrDefault(p => p.Id == id);
    public static string Key(string id) => "pack:" + id;
    // Adds the pack's lines to a book (situation -> text), skipping lines that are already there. Returns how many lines were added.
    public static int AddTo(IDictionary<string, string> book, LinePack pack)
    {
        int added = 0;
        foreach (var (situation, text) in pack.Lines)
        {
            book.TryGetValue(situation, out var current); current ??= "";
            var have = current.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet();
            var fresh = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && have.Add(l)).ToArray(); if (fresh.Length == 0) continue;
            book[situation] = (current.TrimEnd().Length == 0 ? "" : current.TrimEnd() + "\n") + string.Join("\n", fresh); added += fresh.Length;
        }
        return added;
    }
}
