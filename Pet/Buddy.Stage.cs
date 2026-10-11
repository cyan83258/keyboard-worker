using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace KeyboardWorker;

// The character pages: who has one, talking to a character there, gifts handed over there, and opening the page.
public sealed partial class Buddy
{
    // Loaded the first time a page is opened, not at start-up.
    StageBook? stage; StageLog? stageLog; StageClips? stageClips;
    public StageBook Stage => stage ??= new StageBook();
    public StageLog StageTalks => stageLog ??= new StageLog { Limit = Stage.State.Talk.KeepTurns };
    // Exchanges and passages the user set aside from the pages.
    public StageClips StageClips => stageClips ??= new StageClips();
    public StageMode PageMode => StageMode.Find(Stage.State.Mode);
    // A character that has a page: the main pet, the second pet and every other saved preset.
    // Title tells two presets of the same character apart ("별명 (루카)").
    public sealed record StageCharacter(string Id, string Name, Persona Persona, IReadOnlyDictionary<string, string>? Book, string Role, string Title);
    public IReadOnlyList<StageCharacter> StageCharacters()
    {
        var list = new List<StageCharacter> { new(CharacterId, Chat.Preferences.Profile.Name, Chat.Preferences.Profile, Pref.LineBook, "대표", Chat.Preferences.Profile.Name) };
        foreach (var preset in Presets.Items.Where(p => p.Id != Pref.ActivePresetId).OrderByDescending(p => p.Id == Pref.Duo.PresetId))
            list.Add(new(preset.Id, preset.Profile.Name, preset.Profile, preset.LineBook, preset.Id == Pref.Duo.PresetId ? "둘째" : "프리셋", string.IsNullOrWhiteSpace(preset.Name) || preset.Name == preset.Profile.Name ? preset.Profile.Name : preset.Name + " (" + preset.Profile.Name + ")"));
        return list;
    }
    // One exchange on a character's page. note: something that just happened there (a gift), told to the character with the message.
    public async Task<StageReply> SendOnStage(string id, string text, CancellationToken cancellation, Action<string> progress, string note = "")
    {
        if (IsChatBusy) throw new InvalidOperationException("이전 답변을 기다리거나 먼저 생성을 중단해 주세요.");
        var all = StageCharacters(); var who = all.FirstOrDefault(c => c.Id == id) ?? throw new InvalidOperationException("캐릭터를 찾을 수 없어요.");
        CancelAutomatic(); sending = true; using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation); activeChatCancellation = request;
        bool streaming = Chat.Preferences.Streaming; using var pump = new StreamPump(partial => { if (!closing) progress(StageTalk.Shown(partial, who.Name)); }, streaming);
        try
        {
            bool onScreen = id == CharacterId || companion?.Preset.Id == id; var mode = PageMode;
            string state = Adventure.Context(id, who.Name) + (onScreen ? Feelings(id != CharacterId) : Affection.Context(Adventure.Friend(id, who.Name).Bond)) + await Surroundings(null);
            // The backgrounds are only offered when the user lets the character change them.
            var backdrops = Stage.State.AutoBackdrop ? Stage.Backdrops.Select(b => b.Title).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();
            var scene = new StageScene(StageTags.Names(Stage.Set(id).Sprites), backdrops, Stage.CurrentTitle, all.Select(c => c.Name).Where(n => n != who.Name).Distinct().ToArray(), note);
            var reply = await StageTalk.Send(StageTalks, Chat.Preferences, who.Persona, mode.Thread(id), text, state, scene, Stage.State.Talk, mode, request.Token, streaming ? pump.Report : null);
            if (!closing) ProgressCount("chats");
            return reply;
        }
        finally { activeChatCancellation = null; sending = false; }
    }
    // A gift handed over on a character's page (the only place gifts are given). Returns what happened and what to tell the character.
    public (GiftResult Result, string Note) GiveOnStage(StageCharacter who, string gift)
    {
        var given = Adventure.Give(who.Id, who.Name, gift); bool loved = given.Loved; NoteGift();
        var pair = CurrentPair; if (pair != null && pair.Moods && PairMood.Care(pair.Mood, who.Id, Today) == "makeup") { pair.Record("화해", who.Name + "의 서운함이 풀렸어요", "선물을 받고 금방 풀렸대요."); Adventure.Touch(); }
        string worth = given.Gift.Stars >= 5 ? " 세상에 몇 없는 아주 귀한 선물입니다." : given.Gift.Stars == 4 ? " 쉽게 구할 수 없는 귀한 선물입니다." : given.Gift.Stars == 3 ? " 제법 공을 들인 선물입니다." : " 소박한 선물입니다.";
        return (given, "\n[방금 일어난 일]\n사용자가 방금 당신에게 '" + gift + "'(" + given.Gift.Description + ") 선물을 건넸습니다." + worth + " " + (loved ? "당신이 특히 좋아하는 선물입니다." : "특별히 좋아하는 선물은 아닙니다.") + " 이 캐릭터가 선물을 받았을 때 보일 반응을 성격과 말투 그대로 보여 주세요 (기뻐함·쑥스러움·당연하다는 태도·퉁명스러운 감사·떨떠름함 등 성격에 맞는 것).");
    }
    // ---- the page for two ----
    public static string PairThread(string first, string second) => "pair:" + first + "+" + second;
    // One scene between two characters, with the user absent. kind / text: what the user directed (see PairStageTalk).
    public async Task<PairScene> SendPairOnStage(StageCharacter first, StageCharacter second, string kind, string text, CancellationToken cancellation)
    {
        if (IsChatBusy) throw new InvalidOperationException("이전 답변을 기다리거나 먼저 생성을 중단해 주세요.");
        if (first.Id == second.Id) throw new InvalidOperationException("서로 다른 두 캐릭터를 골라 주세요.");
        CancelAutomatic(); sending = true; using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation); activeChatCancellation = request;
        try
        {
            // The relationship card is written from the main pet's side; with the main pet as the second of the scene it is read the other way round.
            var card = Adventure.Pair(first.Id, second.Id); string relation = second.Id == CharacterId ? card.Context(second.Name, first.Name) : card.Context(first.Name, second.Name);
            var backdrops = Stage.State.AutoBackdrop ? Stage.Backdrops.Select(b => b.Title).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();
            var cast = new PairCast(first.Persona, second.Persona, StageTags.Names(Stage.Set(first.Id).Sprites), StageTags.Names(Stage.Set(second.Id).Sprites), relation, backdrops, Stage.CurrentTitle);
            PairLine? authored = kind is PairStageTalk.AsFirst or PairStageTalk.AsSecond && text.Trim().Length > 0 ? new PairLine(kind == PairStageTalk.AsSecond, text.Trim()) : null;
            string state = Pref.Awareness.Clock ? DayClock.Rough(DateTime.Now) : "";
            var (_, scene) = await PairStageTalk.Send(StageTalks, Chat.Preferences, cast, PairThread(first.Id, second.Id), PairStageTalk.Direction(kind, text, first.Name, second.Name), authored, state, Stage.State.Talk, request.Token);
            if (!closing) Adventure.TalkPair(first.Id, second.Id);
            return scene;
        }
        finally { activeChatCancellation = null; sending = false; }
    }
    Window? pairStage;
    public void OpenPairPage() => Open(ref pairStage, () => new PairStageWindow(this), w => pairStage = w);
    public void OpenFonts(Window? owner = null) { var window = new FontWindow(this) { ShowInTaskbar = false }; if (owner != null) window.Owner = owner; window.ShowDialog(); }
    Window? characterWindow, giftWindow;
    // Drawing gifts, the bag and the gift collection.
    public void OpenGifts(int page = 0) { Open(ref giftWindow, () => new GiftWindow(this), w => giftWindow = w); (giftWindow as GiftWindow)?.ShowPage(page); }
    public void OpenCharacter(string? id = null)
    {
        Open(ref characterWindow, () => new CharacterWindow(this), w => characterWindow = w);
        if (id != null) (characterWindow as CharacterWindow)?.Select(id);
    }
    void CloseStageWindows() { characterWindow?.Close(); giftWindow?.Close(); pairStage?.Close(); }

    // ---- what mod files add ----
    public bool OwnsPack(LinePack pack) => pack.Price == 0 || Progress.IsUnlocked(LinePacks.Key(pack.Id));
    // The user's own situation profiles, then those of enabled mods.
    public IEnumerable<ModeProfile> AllModes => Pref.Modes.Concat(ModStore.Current.Profiles);
    internal void ModsChanged() => RefreshMenus();

    // ---- updating by hand ----
    // Downloads the release, checks it, replaces the program files and starts the new version. Only ever called from the update button.
    public async Task ApplyUpdate(ReleaseInfo release, Action<string> say, CancellationToken cancellation)
    {
        string root = Storage.Root, program = AppContext.BaseDirectory;
        say("내려받는 중…"); long last = 0;
        string package = await Updater.Download(release, root, (got, all) => { long now = Environment.TickCount64; if (now - last < 200) return; last = now; Dispatcher.BeginInvoke(() => say(all is > 0 ? $"내려받는 중… {got * 100 / all.Value}% ({got / 1048576.0:0.0} / {all.Value / 1048576.0:0.0}MB)" : $"내려받는 중… {got / 1048576.0:0.0}MB")); }, cancellation);
        say("파일을 확인하는 중…"); string staged = await Task.Run(() => Updater.Stage(package), cancellation);
        // Everything still in memory is written before the program files change.
        Storage.Save(Pref); Game.Save(true); Adventure.Save(true); Progress.Save(true); await OrderedStore.FlushAsync();
        say("프로그램 파일을 바꾸는 중…"); int files = await Task.Run(() => Updater.Apply(staged, program));
        say($"업데이트했어요 ({files}개 파일). 다시 시작합니다…"); Restart();
    }
}
