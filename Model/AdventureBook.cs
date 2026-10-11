using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyboardWorker;

public sealed class FriendCard
{
    public string Name { get; set; } = "";
    public int Bond { get; set; }
    public string Favorite { get; set; } = "";
    public bool Discovered { get; set; }
    public long WorkMs { get; set; }
    public string Day { get; set; } = "";
    public int Pats { get; set; }
    public int Gifts { get; set; }
    public long LastPat { get; set; } = long.MinValue / 2;
    public string Met { get; set; } = "";     // the day this friend first appeared (yyyy-MM-dd)
    public int Stage { get; set; }             // highest closeness stage already celebrated
    public string BoostDay { get; set; } = ""; // closeness grows twice as fast on this day (a rare gift)
    public FriendCard Snapshot() => (FriendCard)MemberwiseClone();
}
public sealed class PairCard
{
    public long WorkMs { get; set; }
    public int Bond { get; set; }
    public string Style { get; set; } = "친근한 친구";
    public int Conversations { get; set; }
    // How the two relate, in the user's own words; all of it is handed to the AI when the two talk.
    public string Description { get; set; } = "";
    public string FirstCalls { get; set; } = "";    // what the main pet calls the second one
    public string SecondCalls { get; set; } = "";   // and the other way round
    public string Rules { get; set; } = "";         // things the two must never do or say
    public bool Moods { get; set; } = true;         // sulking when left out, making up
    public PairMoodState Mood { get; set; } = new();
    // Pictures of the two together, by moment (see PairScenes).
    public Dictionary<string, string> Scenes { get; set; } = new();
    public List<PairEvent> Events { get; set; } = new();
    public PairCard Snapshot() { var copy = (PairCard)MemberwiseClone(); copy.Mood = (Mood ?? new()).Snapshot(); copy.Scenes = new(Scenes ?? new()); copy.Events = new(Events ?? new()); return copy; }
    // The relationship in words for a prompt. firstName / secondName are the main and the second pet.
    public string Context(string firstName, string secondName)
    {
        string text = "\n[두 친구의 관계] " + Style + (string.IsNullOrWhiteSpace(Description) ? "" : ". " + SafeFiles.OneLine(Description, 1500));
        if (!string.IsNullOrWhiteSpace(FirstCalls)) text += $" {firstName}은(는) {secondName}을(를) \"{SafeFiles.OneLine(FirstCalls, 60)}\"(이)라고 부릅니다.";
        if (!string.IsNullOrWhiteSpace(SecondCalls)) text += $" {secondName}은(는) {firstName}을(를) \"{SafeFiles.OneLine(SecondCalls, 60)}\"(이)라고 부릅니다.";
        if (!string.IsNullOrWhiteSpace(Rules)) text += " 꼭 지킬 것: " + SafeFiles.OneLine(Rules, 800);
        return text + $" (관계도 {Bond}/1000)";
    }
    public void Record(string kind, string title, string text) { Events ??= new(); Events.Add(new(DateTimeOffset.Now, kind, title[..Math.Min(title.Length, 120)], text[..Math.Min(text.Length, 2000)])); while (Events.Count > 300) Events.RemoveAt(0); }
}
public sealed record StoryEntry(string Id, string Title, string Body, string Characters, DateTimeOffset At, string Loot = "", bool Favorite = false);
public sealed class StoryQuest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Premise { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public int Minutes { get; set; } = 15;
    public long ProgressMs { get; set; }
    public string StoryId { get; set; } = "";
    public bool Ready => ProgressMs >= Minutes * 60000L;
    public StoryQuest Snapshot() => (StoryQuest)MemberwiseClone();
}
public sealed record AdventureOptions
{
    public bool Enabled { get; init; }
    public int KeepStories { get; init; } = 100;
    public int MinAiMinutes { get; init; } = 20;
    public int OutputTokens { get; init; } = 4096;
    public string Prompt { get; init; } = "캐릭터의 성격·말투·세계관에 맞고 그 캐릭터의 분위기가 살아 있는 작은 모험을 만드세요. 실제 사용자 화면이나 삶을 보았다고 하지 마세요. 이야기는 허구이며 캐릭터 중심입니다. 짧은 도입·사건·감정·여운이 있는 읽기 좋은 글에 집중하고 물건 보상은 언급하지 마세요. 제목과 이야기 본문을 한국어로 써 주세요.";
}
public sealed class AdventureState
{
    public Dictionary<string, FriendCard> Friends { get; set; } = new();
    public Dictionary<string, PairCard> Pairs { get; set; } = new();
    public Dictionary<string, int> Materials { get; set; } = new();
    public List<StoryEntry> Stories { get; set; } = new();
    public List<StoryQuest> Quests { get; set; } = new();
    public AdventureOptions Options { get; set; } = new();
    public DateTimeOffset LastAi { get; set; }
    public List<Anniversary> Anniversaries { get; set; } = new();
    public string AnniversaryDay { get; set; } = "";   // the last day anniversaries were celebrated
    public Dictionary<string, int> GiftBag { get; set; } = new();   // gifts drawn and not yet given, by name
    public int GiftDraws { get; set; }                               // draws ever made
    public int SinceRare { get; set; }                               // draws since the last gift of four stars or more
    public HashSet<string> GiftsMet { get; set; } = new();          // every gift drawn at least once (the gift collection)
    public AdventureState Snapshot() => new()
    {
        Friends = Friends.ToDictionary(p => p.Key, p => p.Value.Snapshot()),
        Pairs = Pairs.ToDictionary(p => p.Key, p => p.Value.Snapshot()),
        Materials = new(Materials),
        Stories = new(Stories),
        Quests = Quests.Select(q => q.Snapshot()).ToList(),
        Options = Options,
        LastAi = LastAi,
        Anniversaries = Anniversaries.Select(a => a.Snapshot()).ToList(),
        AnniversaryDay = AnniversaryDay,
        GiftBag = new(GiftBag), GiftDraws = GiftDraws, SinceRare = SinceRare, GiftsMet = new(GiftsMet)
    };
}
public sealed class AdventureBook
{
    readonly string path; bool dirty;
    public AdventureState State { get; }
    public event Action? Changed;
    // Raised once when a quest reaches its work-time goal.
    public event Action<StoryQuest>? QuestReached;
    // Raised when the closeness with a friend reaches a new stage: friend id, friend name, stage.
    public event Action<string, string, int>? StageReached;
    public AdventureBook(string? root = null)
    {
        path = Path.Combine(root ?? Storage.Root, "adventure-book.json");
        State = Storage.ReadJson<AdventureState>(path, 16 * 1024 * 1024) ?? new();
        State.Friends ??= new(); State.Pairs ??= new(); State.Materials ??= new(); State.Stories ??= new(); State.Quests ??= new(); State.Options ??= new(); State.Anniversaries ??= new();
        State.GiftsMet = (State.GiftsMet ?? new()).Where(n => GiftShop.Find(n) != null).ToHashSet();
        State.GiftBag = (State.GiftBag ?? new()).Where(p => GiftShop.Find(p.Key) != null && p.Value > 0).ToDictionary(p => p.Key, p => Math.Min(p.Value, 9999));
        State.Options = State.Options with { Prompt = (State.Options.Prompt ?? new AdventureOptions().Prompt).Replace("캐릭터의 성격·말투·세계관에 맞는 따뜻하고 조금 신비로운 작은 모험을 만드세요.", "캐릭터의 성격·말투·세계관에 맞고 그 캐릭터의 분위기가 살아 있는 작은 모험을 만드세요.") };
        foreach (var pair in State.Pairs.Values) { pair.Mood ??= new(); pair.Events ??= new(); pair.Scenes = (pair.Scenes ?? new()).Where(p => !SafeFiles.IsNetworkPath(p.Value)).ToDictionary(p => p.Key, p => p.Value); }
        // Friends that existed before stages were introduced start at the stage they already have, without a celebration.
        foreach (var friend in State.Friends.Values) { if (friend.Stage == 0) friend.Stage = Affection.Stage(friend.Bond); if (friend.Met.Length == 0) friend.Met = DateTime.Now.ToString("yyyy-MM-dd"); }
        foreach (var friend in State.Friends.Values) friend.LastPat = long.MinValue / 2;
    }
    public FriendCard Friend(string id, string name, int initial = 0)
    {
        if (!State.Friends.TryGetValue(id, out var friend)) { friend = new() { Name = name, Bond = initial, Favorite = GiftShop.Favorites[SHA256.HashData(Encoding.UTF8.GetBytes(id))[0] % GiftShop.Favorites.Length], Met = DateTime.Now.ToString("yyyy-MM-dd"), Stage = Affection.Stage(initial) }; State.Friends[id] = friend; dirty = true; }
        name = name[..Math.Min(name.Length, 100)]; if (friend.Name != name) { friend.Name = name; dirty = true; }
        if (friend.Day != DateTime.Now.ToString("yyyy-MM-dd")) { friend.Day = DateTime.Now.ToString("yyyy-MM-dd"); friend.Pats = friend.Gifts = 0; dirty = true; }
        return friend;
    }
    public PairCard Pair(string first, string second)
    {
        string key = string.CompareOrdinal(first, second) < 0 ? first + "|" + second : second + "|" + first;
        if (!State.Pairs.TryGetValue(key, out var pair)) { pair = new(); State.Pairs[key] = pair; dirty = true; } return pair;
    }
    void Update() { dirty = true; Changed?.Invoke(); Save(); }
    public void Touch() => Update();
    // Every gain in closeness goes through here so a new stage is noticed exactly once.
    void AddBond(string id, FriendCard friend, int amount)
    {
        if (friend.BoostDay == DateTime.Now.ToString("yyyy-MM-dd")) amount *= 2;   // a rare gift was given today
        friend.Bond = Math.Min(1000, friend.Bond + amount); int stage = Affection.Stage(friend.Bond);
        if (stage > friend.Stage) { friend.Stage = stage; dirty = true; StageReached?.Invoke(id, friend.Name, stage); }
    }
    public bool Pat(string id, string name, long now)
    {
        var f = Friend(id, name); if (f.Pats >= 10 || now - f.LastPat < 10000) return false;
        f.LastPat = now; f.Pats++; AddBond(id, f, 1); Update(); return true;
    }
    // ---- gifts: drawn with coins into a bag, given from the bag ----
    public int Owned(string gift) => State.GiftBag.GetValueOrDefault(gift);
    // Draws one or ten gifts. After many draws without a gift of four stars or more the next one is at least four stars,
    // and ten at once always hold one of three stars or more.
    public List<GiftItem> Draw(PetGame game, int count, Func<int, int>? roll = null)
    {
        count = count >= 10 ? 10 : 1; roll ??= Random.Shared.Next; game.SpendCoins(count == 10 ? GiftShop.TenCost : GiftShop.DrawCost); var drawn = new List<GiftItem>();
        for (int i = 0; i < count; i++)
        {
            int atLeast = State.SinceRare >= GiftShop.Pity - 1 ? 4 : count == 10 && i == 9 && drawn.All(g => g.Stars < 3) ? 3 : 1;
            var gift = GiftShop.Roll(roll, atLeast); drawn.Add(gift); State.GiftBag[gift.Name] = Owned(gift.Name) + 1; State.GiftsMet.Add(gift.Name); State.GiftDraws++; State.SinceRare = gift.Stars >= 4 ? 0 : State.SinceRare + 1;
        }
        Update(); return drawn;
    }
    // Gives one gift from the bag. A favourite counts double; a "boost" gift doubles what the friend gains for the rest of the day.
    public GiftResult Give(string id, string name, string gift)
    {
        var item = GiftShop.Find(gift) ?? throw new InvalidOperationException("선물을 선택해 주세요."); if (Owned(gift) <= 0) throw new InvalidOperationException("가방에 그 선물이 없어요. 먼저 뽑아 주세요.");
        var f = Friend(id, name); if (f.Gifts >= GiftShop.PerDay) throw new InvalidOperationException($"이 친구에게 오늘 선물은 {GiftShop.PerDay}개까지 줄 수 있어요.");
        if (Owned(gift) == 1) State.GiftBag.Remove(gift); else State.GiftBag[gift]--;
        f.Gifts++; bool loved = gift == f.Favorite; f.Discovered |= loved; int before = f.Bond; AddBond(id, f, GiftShop.Gain(item.Stars) * (loved ? 2 : 1));
        if (item.Effect == "boost") f.BoostDay = DateTime.Now.ToString("yyyy-MM-dd");
        Update(); int gain = f.Bond - before;
        return new(item, gain, loved, (loved ? $"{name}가 {gift} 선물을 특히 좋아해요 ♡" : $"{name}에게 {gift} 선물을 전했어요 ♡") + $" 친밀도 +{gain}" + (item.Effect == "boost" ? " · 오늘 하루 친밀도 2배" : ""));
    }    public void Work(string id, string name, long delta)
    {
        if (delta <= 0) return; var f = Friend(id, name); long before = f.WorkMs / 60000; f.WorkMs += delta;
        foreach (var q in State.Quests.Where(q => q.CharacterId == id && q.StoryId.Length == 0)) { bool reached = q.Ready; q.ProgressMs = Math.Min(q.Minutes * 60000L, q.ProgressMs + delta); if (!reached && q.Ready) QuestReached?.Invoke(q); }
        dirty = true; if (before != f.WorkMs / 60000) { AddBond(id, f, 1); Changed?.Invoke(); }
    }
    // Milestones of the two are written into their album as they happen.
    static readonly int[] pairMarks = { 100, 250, 500, 750, 1000 };
    static void AddPairBond(PairCard pair, int amount)
    {
        int before = pair.Bond; pair.Bond = Math.Min(1000, pair.Bond + amount);
        foreach (int mark in pairMarks) if (before < mark && pair.Bond >= mark) pair.Record("관계", "관계도 " + mark + " 달성", "");
    }
    public void TalkPair(string first, string second)
    {
        var p = Pair(first, second); p.Conversations++; AddPairBond(p, 2);
        if (p.Conversations == 1) p.Record("처음", "처음으로 둘이 이야기를 나눴어요", ""); else if (p.Conversations % 100 == 0) p.Record("대화", p.Conversations + "번째 대화", "");
        Update();
    }
    public void WorkPair(string first, string second, long delta)
    {
        if (delta <= 0) return;
        var p = Pair(first, second);
        long previous = p.WorkMs / 300000;
        p.WorkMs += delta;
        if (previous != p.WorkMs / 300000) { AddPairBond(p, 1); Changed?.Invoke(); } dirty = true;
    }
    public void AddQuest(StoryQuest quest)
    {
        if (State.Quests.Count(q => q.StoryId.Length == 0) >= 3) throw new InvalidOperationException("진행 중인 이야기 퀘스트는 최대 3개입니다.");
        quest.Minutes = Math.Clamp(quest.Minutes, 5, 120);
        quest.Title = quest.Title[..Math.Min(quest.Title.Length, 100)];
        quest.Premise = quest.Premise[..Math.Min(quest.Premise.Length, 2000)];
        State.Quests.Add(quest);
        while (State.Quests.Count > 50) State.Quests.RemoveAt(0);
        Update();
    }
    public void AddStory(StoryQuest quest, string title, string body, PetGame game, int roll)
    {
        var live = State.Quests.FirstOrDefault(q => q.Id == quest.Id); if (live == null || live.StoryId.Length > 0 || !live.Ready) return;
        if (body.Length > 6000) throw new InvalidOperationException("이야기는 6000자까지 보관합니다. 더 짧게 생성하도록 지시해 주세요.");
        if (State.Stories.Count >= State.Options.KeepStories && State.Stories.All(s => s.Favorite)) throw new InvalidOperationException("이야기책이 즐겨찾기로 가득합니다. 보관 수를 늘리거나 이야기를 정리해 주세요.");
        string loot = "";
        if (roll < 5) { State.Materials["반짝이는 별가루"] = State.Materials.GetValueOrDefault("반짝이는 별가루") + 1; loot = "희귀 재료 · 반짝이는 별가루"; }
        else if (roll < 8) loot = game.AwardRareItem();
        var story = new StoryEntry(Guid.NewGuid().ToString("N"), title[..Math.Min(title.Length, 100)], body[..Math.Min(body.Length, 6000)], State.Friends.GetValueOrDefault(quest.CharacterId)?.Name ?? "친구", DateTimeOffset.Now, loot);
        State.Stories.Add(story); live.StoryId = story.Id; Trim(); Update();
    }
    public void Trim() { while (State.Stories.Count > State.Options.KeepStories) { int i = State.Stories.FindIndex(s => !s.Favorite); if (i < 0) break; State.Stories.RemoveAt(i); } }
    public void DeleteStory(string id) { State.Stories.RemoveAll(s => s.Id == id); Update(); }
    public void Favorite(string id) { int i = State.Stories.FindIndex(s => s.Id == id); if (i >= 0) State.Stories[i] = State.Stories[i] with { Favorite = !State.Stories[i].Favorite }; Update(); }
    public void SetOptions(AdventureOptions options)
    {
        State.Options = options with { KeepStories = Math.Clamp(options.KeepStories, 1, 300), MinAiMinutes = Math.Clamp(options.MinAiMinutes, 5, 1440), OutputTokens = Math.Clamp(options.OutputTokens, 1000, 8000) };
        Trim();
        Update();
    }
    public void Save(bool force = false) { if (!dirty && !force) return; dirty = false; OrderedStore.SaveJson(path, State.Snapshot()); }
    public string Context(string id, string name) { var f = Friend(id, name); return $"\n[캐릭터 게임 상태] {name}의 친밀도 {f.Bond}/1000. 좋아하는 선물: {f.Favorite}. 이 값은 로컬 게임 값이며 사용자를 압박하거나 의무감을 주지 말 것."; }
}
