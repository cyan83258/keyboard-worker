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
    // Where an accessory was moved to by hand on this pet (by its id); without an entry it sits where it says itself.
    public Dictionary<string, GearPlace> Places { get; set; } = new();
    // What the second pet has on. The main pet's is in this object itself (Equipped, Places, the anchors and the scale).
    public Wear? Second { get; set; }
    // Everything that is drawn from a picture - the user's own gear and the accessories that came with the program - placed as on this pet.
    [System.Text.Json.Serialization.JsonIgnore] public IEnumerable<CustomGear> Gear => CustomItems.Concat(ShopGear.Items).Select(g => Places.TryGetValue(g.Id, out var place) ? g with { WidthPercent = place.Width, OffsetX = place.X, OffsetY = place.Y, Rotation = place.Rotation, Flip = place.Flip } : g);
    // The state as one of the two pets wears it: for the second pet, with what it has on in place of the main pet's.
    // Everything that draws or changes an outfit works on such a view; Take stores it back.
    public GameState For(bool second)
    {
        var copy = Snapshot(); if (!second || Second is not { } wear) return copy;
        copy.Equipped = new(wear.Equipped ?? new()); copy.Places = new(wear.Places ?? new());
        copy.HeadX = wear.HeadX; copy.HeadY = wear.HeadY; copy.FaceX = wear.FaceX; copy.FaceY = wear.FaceY; copy.NeckX = wear.NeckX; copy.NeckY = wear.NeckY; copy.AccessoryScale = wear.AccessoryScale; return copy;
    }
    public void Take(bool second, GameState from)
    {
        var wear = new Wear { Equipped = new(from.Equipped), Places = new(from.Places), HeadX = from.HeadX, HeadY = from.HeadY, FaceX = from.FaceX, FaceY = from.FaceY, NeckX = from.NeckX, NeckY = from.NeckY, AccessoryScale = from.AccessoryScale };
        if (second) { Second = wear; return; }
        Equipped = wear.Equipped; Places = wear.Places; HeadX = wear.HeadX; HeadY = wear.HeadY; FaceX = wear.FaceX; FaceY = wear.FaceY; NeckX = wear.NeckX; NeckY = wear.NeckY; AccessoryScale = wear.AccessoryScale;
    }
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
        if (Second is { } wear) copy.Second = new Wear { Equipped = new(wear.Equipped ?? new()), Places = new(wear.Places ?? new()), HeadX = wear.HeadX, HeadY = wear.HeadY, FaceX = wear.FaceX, FaceY = wear.FaceY, NeckX = wear.NeckX, NeckY = wear.NeckY, AccessoryScale = wear.AccessoryScale };
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
        var catalog = ShopGear.Catalog.Concat(CustomItems.Select(c => new ShopItem(c.Id, c.Name, c.Slot, 0, 1, "직접 등록한 장비"))).ToArray();
        Owned.RemoveWhere(id => !catalog.Any(i => i.Id == id));
        ClaimedQuests.RemoveWhere(id => !PetGame.Quests.Any(q => q.Id == id)); Badges.RemoveWhere(id => !PetGame.BadgeNames.ContainsKey(id));
        Clean(catalog);
        // The second pet starts out wearing what the main pet wears (as it did before each had its own); from then on the two are apart.
        if (Second == null) Take(true, this); else { var view = For(true); view.Clean(catalog); Take(true, view); }
    }
    // What this pet has on, kept to what exists and is owned, and within bounds.
    void Clean(ShopItem[] catalog)
    {
        Equipped ??= new(); Places ??= new();
        foreach (var slot in Equipped.Keys.ToArray()) if (!catalog.Any(i => i.Id == Equipped[slot] && i.Slot == slot) || !Owned.Contains(Equipped[slot])) Equipped.Remove(slot);
        foreach (var id in Places.Keys.ToArray()) { if (Places[id] == null || catalog.All(i => i.Id != id)) Places.Remove(id); else Places[id] = Places[id].Normalized(); }
        static double Safe(double v, double fallback, double min, double max) => double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
        HeadX = Safe(HeadX, 50, 0, 100); HeadY = Safe(HeadY, 15, 0, 100); FaceX = Safe(FaceX, 50, 0, 100); FaceY = Safe(FaceY, 40, 0, 100); NeckX = Safe(NeckX, 50, 0, 100); NeckY = Safe(NeckY, 61, 0, 100);
        AccessoryScale = Safe(AccessoryScale, 1, .5, 2);
    }
}
// What one pet has on and where: which accessory in each place, where each was moved to by hand, the anchors on its picture and the scale.
public sealed class Wear
{
    public Dictionary<string, string> Equipped { get; set; } = new();
    public Dictionary<string, GearPlace> Places { get; set; } = new();
    public double HeadX { get; set; } = 50;
    public double HeadY { get; set; } = 15;
    public double FaceX { get; set; } = 50;
    public double FaceY { get; set; } = 40;
    public double NeckX { get; set; } = 50;
    public double NeckY { get; set; } = 61;
    public double AccessoryScale { get; set; } = 1;
}
// Size (a share of the character's width), offset from the slot's anchor (percent), turn and mirroring of one accessory.
public sealed record GearPlace(double Width, double X, double Y, double Rotation, bool Flip = false)
{
    public static GearPlace Of(CustomGear gear) => new(gear.WidthPercent, gear.OffsetX, gear.OffsetY, gear.Rotation, gear.Flip);
    public GearPlace Normalized() => Of(new CustomGear { WidthPercent = Width, OffsetX = X, OffsetY = Y, Rotation = Rotation, Flip = Flip }.Normalized());
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
    // second: the second pet puts it on (what is owned is shared; what each wears is its own).
    public void Equip(string id, bool second = false)
    {
        var item = Catalog.SingleOrDefault(i => i.Id == id) ?? throw new InvalidOperationException("알 수 없는 아이템입니다.");
        if (!State.Owned.Contains(id)) throw new InvalidOperationException("먼저 아이템을 구매해 주세요.");
        Change(second, wear => wear.Equipped[item.Slot] = id);
    }
    public void Remove(string slot, bool second = false) => Change(second, wear => wear.Equipped.Remove(slot));
    void Change(bool second, Action<GameState> change) { var wear = State.For(second); change(wear); State.Take(second, wear); Updated(); Save(); }
    // Nobody wears it any more.
    void TakeOff(string id) { foreach (bool second in new[] { false, true }) { var wear = State.For(second); foreach (var slot in wear.Equipped.Keys.Where(k => wear.Equipped[k] == id).ToArray()) wear.Equipped.Remove(slot); wear.Places.Remove(id); State.Take(second, wear); } }
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
        // Its place and where it is worn were just written anew, so what the pets had of it by hand goes.
        if (index >= 0) State.CustomItems[index] = item.Normalized(); else State.CustomItems.Add(item.Normalized());
        TakeOff(item.Id); State.Owned.Add(item.Id); if (equip) State.Equipped[item.Slot] = item.Id; Updated(); Save();
    }
    // The places worked out in the placement window for one pet: taken over as they are.
    public void Place(GameState placed, bool second = false) { State.Take(second, placed); State.Normalize(); Updated(); Save(); }
    public void RemapImages(IReadOnlyDictionary<string, string> merge)
    {
        for (int i = 0; i < State.CustomItems.Count; i++) State.CustomItems[i] = State.CustomItems[i] with { ImagePath = ImageDiet.Map(merge, State.CustomItems[i].ImagePath) };
        Updated(); Save();
    }
    public void DeleteCustom(string id)
    {
        if (!State.CustomItems.Any(c => c.Id == id)) throw new InvalidOperationException("커스텀 장비를 선택해 주세요.");
        State.CustomItems.RemoveAll(c => c.Id == id); State.Owned.Remove(id); TakeOff(id); Updated(); Save();
    }
    public void DeleteOwned(string id)
    {
        if (!State.Owned.Contains(id)) throw new InvalidOperationException("보유한 장비를 선택해 주세요.");
        if (State.CustomItems.Any(c => c.Id == id)) { DeleteCustom(id); return; }
        if (ShopGear.Catalog.All(i => i.Id != id)) throw new InvalidOperationException("알 수 없는 장비입니다.");
        State.Owned.Remove(id); TakeOff(id); Updated(); Save();
    }
    // The switches are the program's; the anchors and the scale are those of the pet the view (For) was of.
    public void Options(GameState options, bool second = false)
    {
        var wear = State.For(second); wear.HeadX = options.HeadX; wear.HeadY = options.HeadY; wear.FaceX = options.FaceX; wear.FaceY = options.FaceY; wear.NeckX = options.NeckX; wear.NeckY = options.NeckY; wear.AccessoryScale = options.AccessoryScale; State.Take(second, wear);
        State.Enabled = options.Enabled;
        State.ShowBadge = options.ShowBadge;
        State.ShowAccessories = options.ShowAccessories;
        State.Announcements = options.Announcements;
        State.ShareWithAi = options.ShareWithAi;
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
