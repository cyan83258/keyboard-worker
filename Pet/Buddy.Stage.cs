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
    StageBook? stage; StageLog? stageLog;
    public StageBook Stage => stage ??= new StageBook();
    public StageLog StageTalks => stageLog ??= new StageLog();
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
            var backdrops = Stage.State.AutoBackdrop ? Stage.State.Backdrops.Select(b => b.Title).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();
            var scene = new StageScene(StageTags.Names(Stage.Set(id).Sprites), backdrops, Stage.CurrentTitle, all.Select(c => c.Name).Where(n => n != who.Name).Distinct().ToArray(), note);
            var reply = await StageTalk.Send(StageTalks, Chat.Preferences, who.Persona, mode.Thread(id), text, state, scene, Stage.State.Talk, mode, request.Token, streaming ? pump.Report : null);
            if (!closing) ProgressCount("chats");
            return reply;
        }
        finally { activeChatCancellation = null; sending = false; }
    }
    // A gift handed over on a character's page. Returns the notice (closeness gained) and what to tell the character.
    public (string Notice, string Note) GiveOnStage(StageCharacter who, string gift)
    {
        bool loved = gift == Adventure.Friend(who.Id, who.Name).Favorite; string notice = Adventure.Gift(who.Id, who.Name, gift, Game); NoteGift();
        var pair = CurrentPair; if (pair != null && pair.Moods && PairMood.Care(pair.Mood, who.Id, Today) == "makeup") { pair.Record("화해", who.Name + "의 서운함이 풀렸어요", "선물을 받고 금방 풀렸대요."); Adventure.Touch(); }
        return (notice, "\n[방금 일어난 일]\n사용자가 방금 당신에게 '" + gift + "' 선물을 건넸습니다. " + (loved ? "당신이 특히 좋아하는 선물입니다." : "특별히 좋아하는 선물은 아닙니다.") + " 이 캐릭터가 선물을 받았을 때 보일 반응을 성격과 말투 그대로 보여 주세요 (기뻐함·쑥스러움·당연하다는 태도·퉁명스러운 감사·떨떠름함 등 성격에 맞는 것).");
    }
    public void OpenFonts(Window? owner = null) { var window = new FontWindow(this) { ShowInTaskbar = false }; if (owner != null) window.Owner = owner; window.ShowDialog(); }
    Window? characterWindow;
    public void OpenCharacter(string? id = null)
    {
        Open(ref characterWindow, () => new CharacterWindow(this), w => characterWindow = w);
        if (id != null) (characterWindow as CharacterWindow)?.Select(id);
    }
    void CloseStageWindows() => characterWindow?.Close();

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
