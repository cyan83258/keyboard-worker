using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KeyboardWorker;

public sealed record ShopItem(string Id, string Name, string Slot, int Price, int Level, string Description);
public sealed record DailyQuest(string Id, string Name, long Target, string Kind, int Coins, int Xp);
public sealed class GameState
{
    public long TotalXp { get; set; }
    public long Coins { get; set; } = 20;
    public long LifetimeWorkMs { get; set; }
    public long MinuteRemainderMs { get; set; }
    public string Day { get; set; } = WorkTimeStore.Today;
    public long DailyWorkMs { get; set; }
    public int DailyPats { get; set; }
    public int Bond { get; set; }
    public HashSet<string> Owned { get; set; } = new();
    public Dictionary<string, string> Equipped { get; set; } = new();
    public HashSet<string> ClaimedQuests { get; set; } = new();
    public HashSet<string> Badges { get; set; } = new();
    public List<CustomGear> CustomItems { get; set; } = new();
    // Everything that is drawn from a picture: the user's own gear and the accessories that came with the program.
    // Where the user moved an accessory that came with the program (by its id). Their own gear keeps its place in itself.
    public Dictionary<string, GearPlace> Places { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public IEnumerable<CustomGear> Gear => CustomItems.Concat(ShopGear.Items.Select(g => Places.TryGetValue(g.Id, out var place) ? g with { WidthPercent = place.Width, OffsetX = place.X, OffsetY = place.Y, Rotation = place.Rotation } : g));
    public CustomGear? Wearing(string id) { foreach (var gear in Gear) if (gear.Id == id) return gear; return null; }
    public bool Enabled { get; set; } = true;
    public bool ShowBadge { get; set; } = true;
    public bool ShowAccessories { get; set; } = true;
    public bool Announcements { get; set; } = true;
    public bool ShareWithAi { get; set; } = true;
    public double HeadX { get; set; } = 50;
    public double HeadY { get; set; } = 15;
    public double FaceX { get; set; } = 50;
    public double FaceY { get; set; } = 40;
    public double NeckX { get; set; } = 50;
    public double NeckY { get; set; } = 61;
    public double AccessoryScale { get; set; } = 1;
    public GameState Snapshot()
    {
        var copy = (GameState)MemberwiseClone();
        copy.Owned = new(Owned);
        copy.Equipped = new(Equipped);
        copy.ClaimedQuests = new(ClaimedQuests);
        copy.Badges = new(Badges);
        copy.CustomItems = new(CustomItems); copy.Places = new(Places);
        return copy;
    }
    public void Normalize()
    {
        TotalXp = Math.Clamp(TotalXp, 0, 1000000000); Coins = Math.Clamp(Coins, 0, 1000000000);
        LifetimeWorkMs = Math.Clamp(LifetimeWorkMs, 0, 36500L * 86400000); MinuteRemainderMs = Math.Clamp(MinuteRemainderMs, 0, 59999);
        DailyWorkMs = Math.Clamp(DailyWorkMs, 0, 86400000); DailyPats = Math.Clamp(DailyPats, 0, 10); Bond = Math.Clamp(Bond, 0, 100);
        Owned ??= new(); Equipped ??= new(); ClaimedQuests ??= new(); Badges ??= new();
        CustomItems ??= new();
        CustomItems = CustomItems.Where(c => c != null && c.Id != null && c.Id.StartsWith("custom-", StringComparison.Ordinal) && CustomGear.Slots.Contains(c.Slot) && !string.IsNullOrWhiteSpace(c.ImagePath)).GroupBy(c => c.Id).Select(g => g.First().Normalized()).Take(50).ToList();
        Places ??= new(); foreach (var id in Places.Keys.ToArray()) { if (Places[id] == null || ShopGear.Items.All(g => g.Id != id)) Places.Remove(id); else Places[id] = Places[id].Normalized(); }
        var catalog = ShopGear.Catalog.Concat(CustomItems.Select(c => new ShopItem(c.Id, c.Name, c.Slot, 0, 1, "직접 등록한 장비"))).ToArray();
        Owned.RemoveWhere(id => !catalog.Any(i => i.Id == id));
        foreach (var slot in Equipped.Keys.ToArray()) if (!catalog.Any(i => i.Id == Equipped[slot] && i.Slot == slot) || !Owned.Contains(Equipped[slot])) Equipped.Remove(slot);
        ClaimedQuests.RemoveWhere(id => !PetGame.Quests.Any(q => q.Id == id)); Badges.RemoveWhere(id => !PetGame.BadgeNames.ContainsKey(id));
        static double Safe(double v, double fallback, double min, double max) => double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
        HeadX = Safe(HeadX, 50, 0, 100);
        HeadY = Safe(HeadY, 15, 0, 100);
        FaceX = Safe(FaceX, 50, 0, 100);
        FaceY = Safe(FaceY, 40, 0, 100);
        NeckX = Safe(NeckX, 50, 0, 100);
        NeckY = Safe(NeckY, 61, 0, 100);
        AccessoryScale = Safe(AccessoryScale, 1, .5, 2);
    }
}
// Size (a share of the character's width), offset from the slot's anchor (percent) and turn of one accessory.
public sealed record GearPlace(double Width, double X, double Y, double Rotation)
{
    public static GearPlace Of(CustomGear gear) => new(gear.WidthPercent, gear.OffsetX, gear.OffsetY, gear.Rotation);
    public GearPlace Normalized() => Of(new CustomGear { WidthPercent = Width, OffsetX = X, OffsetY = Y, Rotation = Rotation }.Normalized());
}
public sealed record GameUpdate(bool LevelUp, string Message);
public sealed class PetGame
{
    public static readonly DailyQuest[] Quests =
    {
        new("work5", "키보드 작업 5분", 300000, "work", 15, 30),
        new("work15", "키보드 작업 15분", 900000, "work", 30, 50),
        new("work30", "키보드 작업 30분", 1800000, "work", 50, 80),
        new("pat3", "친구 쓰다듬기 3회", 3, "pat", 10, 10)
    };
    public static readonly Dictionary<string, string> BadgeNames = new() { ["first-minute"] = "첫 동행 · 작업 1분", ["first-shop"] = "첫 쇼핑", ["level3"] = "성장하는 친구 · Lv.3", ["first-quest"] = "오늘의 작은 성취", ["bond20"] = "친한 친구 · 친밀도 20" };
    readonly string path;
    long lastPat = -10000;
    bool dirty;
    public GameState State { get; private set; }
    public event Action? Changed;
    public int Level => LevelOf(State.TotalXp);
    public long XpAtLevel => State.TotalXp - FloorXp(Level);
    public long NextXp => Level == 100 ? 0 : 50 + 25 * Level;
    public bool Dirty => dirty;
    // What can be worn: the accessories that came with the program as pictures (ShopGear), then the user's own.
    public ShopItem[] Catalog => ShopGear.Catalog.Concat(State.CustomItems.Select(c => new ShopItem(c.Id, c.Name, c.Slot, 0, 1, "직접 등록한 장비 · 무료"))).ToArray();
    public PetGame(string? root = null)
    {
        path = Path.Combine(root ?? Storage.Root, "pet-game.json");
        State = Storage.ReadJson<GameState>(path, 512 * 1024) ?? new();
        State.CustomItems?.RemoveAll(c => c != null && SafeFiles.IsNetworkPath(c.ImagePath));
        State.Normalize(); RollDay(WorkTimeStore.Today);
    }
    public static long FloorXp(int level) => 50L * (level - 1) + 25L * (level - 1) * level / 2;
    public static int LevelOf(long xp) { int level = 1; while (level < 100 && xp >= FloorXp(level + 1)) level++; return level; }
    public void RollDay(string day)
    {
        if (State.Day == day) return; State.Day = day; State.DailyWorkMs = 0; State.DailyPats = 0; State.ClaimedQuests.Clear(); dirty = true; Changed?.Invoke();
    }
    void Badge(string id) { if (State.Badges.Add(id)) State.Coins += 5; }
    void Updated() { State.Normalize(); dirty = true; Changed?.Invoke(); }
    public GameUpdate Work(long delta, string day)
    {
        RollDay(day); if (!State.Enabled || delta <= 0) return new(false, "");
        delta = Math.Min(delta, 10000); State.LifetimeWorkMs += delta; State.DailyWorkMs += delta; State.MinuteRemainderMs += delta; dirty = true;
        long minutes = State.MinuteRemainderMs / 60000; State.MinuteRemainderMs %= 60000;
        if (minutes == 0) return new(false, "");
        int oldLevel = Level; State.TotalXp += minutes * 10; State.Coins += minutes * 5;
        if (State.LifetimeWorkMs >= 60000) Badge("first-minute"); if (Level >= 3) Badge("level3");
        bool leveled = Level > oldLevel; if (leveled) State.Coins += (Level - oldLevel) * 10; Updated();
        return new(leveled, leveled ? $"Lv.{Level}이 됐어! 같이 쌓은 시간이 선물이네 ♡" : "");
    }
    public bool Pat(long now, string day)
    {
        RollDay(day); if (!State.Enabled || State.DailyPats >= 10 || now - lastPat < 10000) return false;
        lastPat = now; State.DailyPats++; State.Bond = Math.Min(100, State.Bond + 1); if (State.Bond >= 20) Badge("bond20"); Updated(); return true;
    }
    public bool QuestReady(DailyQuest quest) => (quest.Kind == "work" ? State.DailyWorkMs : State.DailyPats) >= quest.Target;
    public GameUpdate Claim(string id, string day)
    {
        RollDay(day); var quest = Quests.SingleOrDefault(q => q.Id == id) ?? throw new InvalidOperationException("알 수 없는 목표입니다.");
        if (!State.Enabled) throw new InvalidOperationException("성장 보상을 먼저 켜 주세요.");
        if (State.ClaimedQuests.Contains(id)) throw new InvalidOperationException("오늘 이미 받은 보상입니다.");
        if (!QuestReady(quest)) throw new InvalidOperationException("목표를 아직 달성하지 않았어요.");
        int oldLevel = Level; State.ClaimedQuests.Add(id); State.Coins += quest.Coins; State.TotalXp += quest.Xp; Badge("first-quest"); if (Level >= 3) Badge("level3");
        bool leveled = Level > oldLevel; if (leveled) State.Coins += (Level - oldLevel) * 10; Updated(); Save(); return new(leveled, $"{quest.Name} 보상을 받았어 ♡");
    }
    public void Buy(string id)
    {
        var item = Catalog.SingleOrDefault(i => i.Id == id) ?? throw new InvalidOperationException("알 수 없는 아이템입니다.");
        if (State.Owned.Contains(id)) throw new InvalidOperationException("이미 가지고 있는 아이템입니다.");
        if (Level < item.Level) throw new InvalidOperationException($"Lv.{item.Level}부터 살 수 있어요.");
        if (State.Coins < item.Price) throw new InvalidOperationException("코인이 부족해요.");
        State.Coins -= item.Price; State.Owned.Add(id); Badge("first-shop"); Updated(); Save();
    }
    public void Equip(string id)
    {
        var item = Catalog.SingleOrDefault(i => i.Id == id) ?? throw new InvalidOperationException("알 수 없는 아이템입니다.");
        if (!State.Owned.Contains(id)) throw new InvalidOperationException("먼저 아이템을 구매해 주세요.");
        State.Equipped[item.Slot] = id; Updated(); Save();
    }
    public void Remove(string slot) { State.Equipped.Remove(slot); Updated(); Save(); }
    public void SpendCoins(int coins) { if (coins < 0 || State.Coins < coins) throw new InvalidOperationException($"코인이 부족합니다. ({coins}코인 필요 · 보유 {State.Coins}코인)"); State.Coins -= coins; Updated(); Save(); }
    // Coins from attendance, goals, events and achievements.
    public void Earn(int coins) { if (coins <= 0) return; State.Coins += coins; Updated(); }
    public string AwardRareItem()
    {
        var available = ShopGear.Catalog.Where(i => !State.Owned.Contains(i.Id)).ToArray(); if (available.Length == 0) return "";
        var item = available[Random.Shared.Next(available.Length)]; State.Owned.Add(item.Id); Updated(); Save(); return "희귀 장식 · " + item.Name;
    }
    public void RegisterCustom(CustomGear item, bool equip)
    {
        if (string.IsNullOrWhiteSpace(item.Name) || !CustomGear.Slots.Contains(item.Slot) || !item.Id.StartsWith("custom-", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(item.ImagePath)) throw new InvalidOperationException("장비 이름·종류·이미지를 확인해 주세요.");
        int index = State.CustomItems.FindIndex(c => c.Id == item.Id);
        if (index < 0 && State.CustomItems.Count >= 50) throw new InvalidOperationException("커스텀 장비는 최대 50개까지 등록할 수 있어요.");
        if (index >= 0) State.CustomItems[index] = item.Normalized(); else State.CustomItems.Add(item.Normalized());
        foreach (var slot in State.Equipped.Keys.Where(k => State.Equipped[k] == item.Id && (k != item.Slot || !equip)).ToArray()) State.Equipped.Remove(slot);
        State.Owned.Add(item.Id); if (equip) State.Equipped[item.Slot] = item.Id; Updated(); Save();
    }
    // The places worked out in the placement window: taken over as they are.
    public void Place(GameState placed) { State.CustomItems = placed.CustomItems.Select(c => c.Normalized()).ToList(); State.Places = new(placed.Places); State.Normalize(); Updated(); Save(); }
    public void RemapImages(IReadOnlyDictionary<string, string> merge)
    {
        for (int i = 0; i < State.CustomItems.Count; i++) State.CustomItems[i] = State.CustomItems[i] with { ImagePath = ImageDiet.Map(merge, State.CustomItems[i].ImagePath) };
        Updated(); Save();
    }
    public void DeleteCustom(string id)
    {
        if (!State.CustomItems.Any(c => c.Id == id)) throw new InvalidOperationException("커스텀 장비를 선택해 주세요.");
        State.CustomItems.RemoveAll(c => c.Id == id); State.Owned.Remove(id);
        foreach (var slot in State.Equipped.Keys.Where(k => State.Equipped[k] == id).ToArray()) State.Equipped.Remove(slot); Updated(); Save();
    }
    public void DeleteOwned(string id)
    {
        if (!State.Owned.Contains(id)) throw new InvalidOperationException("보유한 장비를 선택해 주세요.");
        if (State.CustomItems.Any(c => c.Id == id)) { DeleteCustom(id); return; }
        if (ShopGear.Catalog.All(i => i.Id != id)) throw new InvalidOperationException("알 수 없는 장비입니다.");
        State.Owned.Remove(id);
        foreach (var slot in State.Equipped.Keys.Where(k => State.Equipped[k] == id).ToArray()) State.Equipped.Remove(slot);
        Updated(); Save();
    }
    public void Options(GameState options)
    {
        State.Enabled = options.Enabled;
        State.ShowBadge = options.ShowBadge;
        State.ShowAccessories = options.ShowAccessories;
        State.Announcements = options.Announcements;
        State.ShareWithAi = options.ShareWithAi;
        State.HeadX = options.HeadX;
        State.HeadY = options.HeadY;
        State.FaceX = options.FaceX;
        State.FaceY = options.FaceY;
        State.NeckX = options.NeckX;
        State.NeckY = options.NeckY;
        State.AccessoryScale = options.AccessoryScale;
        Updated(); Save();
    }
    public void Save(bool force = false) { if (!dirty && !force) return; OrderedStore.SaveJson(path, State.Snapshot()); dirty = false; }
    public string ChatContext()
    {
        if (!State.ShareWithAi) return "";
        var catalog = Catalog; string outfit = string.Join(", ", State.Equipped.Values.Select(id => catalog.First(i => i.Id == id).Name));
        return $"\n[앱이 제공한 현재 게임 상태]\n캐릭터 레벨 {Level}, 보유 {State.Coins}코인, 친밀도 {State.Bond}/100, 장착: {(outfit.Length == 0 ? "없음" : outfit)}. 이는 앱의 로컬 게임 값이며 실제 돈이 아니다. 대화로 구매·보상 수령·장착을 실행할 수 없고 실행했다고 말하지 말 것. 필요하면 성장·상점·옷장 창을 안내하라. 친밀도나 레벨을 이유로 사용자에게 접속이나 작업을 강요하지 말 것.";
    }
}
