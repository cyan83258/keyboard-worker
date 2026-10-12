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
    // The chat rooms of the pages; each has a thread of talk, an author's note and a memory of its own.
    StageRooms? stageRooms; readonly HashSet<StageRoom> remembering = new();
    public StageRooms Rooms => stageRooms ??= new StageRooms();
    // Brings a room's memory up to date and says what happened. asked: the user pressed the button; otherwise it is only
    // done when the room wants it and enough new talk has gathered ("" = nothing to do).
    public async Task<string> Remember(string thread, StageRoom room, bool asked, CancellationToken cancellation = default)
    {
        var pending = StageMemory.Pending(StageTalks.Turns(thread), room);
        if (!asked && (!room.AutoSummary || pending.Length < Rooms.State.SummaryEvery)) return "";
        if (pending.Length == 0) return "새로 요약할 대화가 없어요.";
        if (!remembering.Add(room)) return "이미 요약하는 중이에요.";
        try
        {
            room.Summary = await StageMemory.Summarize(Chat.Preferences, Rooms.State.SummaryConnection, room.Summary, pending, cancellation);
            room.SummarizedAt = pending[^1].At; Rooms.Save(); return "대화 " + pending.Length + "개를 기억에 담았어요.";
        }
        finally { remembering.Remove(room); }
    }
    // After an exchange, in the background. A failure is not shown: the memory is simply brought up to date next time.
    async void RememberQuietly(string thread, StageRoom room) { try { await Remember(thread, room, false); } catch (Exception) { } }
    // The character page and text messages are two separate things; each has a mode of its own (more than one only with mods).
    public StageMode StageModeNow => StageMode.Find(Stage.State.Mode, StageMode.Stage);
    public StageMode TextModeNow => StageMode.Find(Stage.State.TextMode, StageMode.Messenger);
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
    public async Task<StageReply> SendOnPage(string id, StageMode mode, string text, CancellationToken cancellation, Action<string> progress, string note = "")
    {
        if (IsChatBusy) throw new InvalidOperationException("이전 답변을 기다리거나 먼저 생성을 중단해 주세요.");
        var all = StageCharacters(); var who = all.FirstOrDefault(c => c.Id == id) ?? throw new InvalidOperationException("캐릭터를 찾을 수 없어요.");
        CancelAutomatic(); sending = true; using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation); activeChatCancellation = request;
        bool streaming = Chat.Preferences.Streaming; using var pump = new StreamPump(partial => { if (!closing) progress(StageTalk.Shown(partial, who.Name)); }, streaming);
        try
        {
            bool onScreen = id == CharacterId || companion?.Preset.Id == id; var options = mode.Layout == StageMode.Messenger ? Stage.State.Texting : Stage.State.Talk;
            string state = Adventure.Context(id, who.Name) + (onScreen ? Feelings(id != CharacterId) : Affection.Context(Adventure.Friend(id, who.Name).Bond)) + await Surroundings(null);
            // The backgrounds are only offered when the user lets the character change them.
            var backdrops = Stage.State.AutoBackdrop ? Stage.Backdrops.Select(b => b.Title).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();
            var scene = new StageScene(StageTags.Names(Stage.Set(id).Sprites), backdrops, Stage.CurrentTitle, all.Select(c => c.Name).Where(n => n != who.Name).Distinct().ToArray(), note, mode.Layout == StageMode.Messenger ? Stage.State.Stickers.Select(s => s.Title).ToArray() : null);
            string key = mode.Thread(id); var room = Rooms.Current(key); string thread = StageRooms.Thread(key, room); StageTalks.Limit = options.KeepTurns;   // the pages and the text messages each keep as much as they were told to
            var reply = await StageTalk.Send(StageTalks, Chat.Preferences, who.Persona, thread, text, state + room.Context(), scene, options, mode, request.Token, streaming ? pump.Report : null);
            if (!closing) { ProgressCount("chats"); RememberQuietly(thread, room); }
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
            string key = PairThread(first.Id, second.Id); var room = Rooms.Current(key); string thread = StageRooms.Thread(key, room); StageTalks.Limit = Stage.State.Talk.KeepTurns;
            var (_, scene) = await PairStageTalk.Send(StageTalks, Chat.Preferences, cast, thread, PairStageTalk.Direction(kind, text, first.Name, second.Name), authored, state + room.Context(), Stage.State.Talk, request.Token);
            if (!closing) { Adventure.TalkPair(first.Id, second.Id); RememberQuietly(thread, room); }
            return scene;
        }
        finally { activeChatCancellation = null; sending = false; }
    }
    // A room read from a file becomes a new room of the page, with its talk, note and memory.
    public StageRoom ImportRoom(string page, StageArchive.RoomFile file)
    {
        string kind = StageMode.Kind(page); if (file.Kind.Length > 0 && file.Kind != kind) throw new InvalidOperationException("다른 종류의 방이에요. " + (file.Kind == StageMode.Pair ? "둘만의 페이지" : file.Kind == StageMode.Messenger ? "문자 메시지" : "캐릭터 페이지(마주 보기)") + "에서 불러와 주세요.");
        var room = Rooms.Add(page, file.Name); room.Note = file.Note; room.Summary = file.Summary; if (file.Summary.Trim().Length > 0 && file.Turns.Length > 0) room.SummarizedAt = file.Turns.Max(t => t.At);
        Rooms.Save(); StageTalks.Import(StageRooms.Thread(page, room), file.Turns); return room;
    }
    Window? pairStage, messenger;
    public void OpenMessenger(string? id = null) { Open(ref messenger, () => new MessengerWindow(this), w => messenger = w); if (id != null) (messenger as MessengerWindow)?.Select(id); }
    // The tabs of the page windows: the other page opens where this one stood, and this one closes.
    // The new one takes the old one's place and size before it is first shown, and the old one only goes once the new one
    // has been drawn - so the screen never shows a window jumping into place or the desktop in between.
    Window? placeLike;
    static void Place(Window window, Window like)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        if (like.WindowState == WindowState.Normal) { window.Left = like.Left; window.Top = like.Top; window.Width = Math.Max(window.MinWidth, like.Width); window.Height = Math.Max(window.MinHeight, like.Height); }
        window.WindowState = like.WindowState;
    }
    public void SwitchPage(Window from, string kind, string? id)
    {
        Window? Target() => kind == StageMode.Pair ? pairStage : kind == StageMode.Messenger ? messenger : characterWindow;
        bool fresh = Target() == null; placeLike = from;
        try { if (kind == StageMode.Pair) OpenPairPage(); else if (kind == StageMode.Messenger) OpenMessenger(id); else OpenCharacter(id); } finally { placeLike = null; }
        var target = Target(); if (target == null || ReferenceEquals(target, from)) return;
        if (!fresh) { Place(target, from); from.Close(); return; }
        void Leave(object? sender, EventArgs e) { target.ContentRendered -= Leave; from.Close(); target.Activate(); }
        target.ContentRendered += Leave;
    }
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
    void CloseStageWindows() { characterWindow?.Close(); giftWindow?.Close(); pairStage?.Close(); messenger?.Close(); }

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
