using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace KeyboardWorker;

// Closeness and keepsakes: what the pets say on their own (AutoLines), their voices, closeness stages, anniversaries,
// kept lines, and how the two pets get along (sulking, scenes, their album).
public sealed partial class Buddy
{
    // ---- automatic lines (see AutoLines) ----
    AutoLines? auto;
    internal AutoLines Auto => auto ??= new AutoLines(this);
    public string AutomaticStatus => Auto.Status;
    public string AutomaticOutlook => Auto.Outlook;
    public string TestAutomatic(bool together) => Auto.Test(together);
    public void CancelAutomatic() => auto?.Cancel();
    internal bool IsClosing => closing;
    internal bool IsHidden => hidden;
    internal Activity Idle => activity;
    internal PetBubbleWindow MainBubble => bubble;
    internal AppTracker Apps => apps;
    internal void ShowPose(string pose) => Display(pose);
    // A bubble may be shown at all.
    internal bool MayShow => !closing && !hidden && !IsQuiet && !IsChatBusy;
    // The pets may start talking by themselves: not while resting, in a program set to "no reminders", or while the user is typing to them.
    internal bool MaySpeak => RestReason == null && !closing;
    // Why the pets keep quiet right now; null when nothing holds them back.
    internal string? RestReason
    {
        get
        {
            if (hidden) return "캐릭터가 숨겨져 있는 동안에는 쉬어요.";
            if (IsQuiet) return "조용한 작업 모드 중에는 쉬어요.";
            if (activity.IsPaused(Environment.TickCount64) || FocusTimer.Phase == "휴식") return "휴식 시간 중에는 쉬어요.";
            if (policy.NoReminder) return "지금 쓰는 프로그램이 재촉 제외로 설정돼 있어요.";
            if (IsChatBusy) return "채팅 답변을 만드는 중이라 기다리고 있어요.";
            if (quickWindow?.IsVisible == true || reactionPreview?.IsVisible == true) return "한마디 입력 창이나 반응 미리보기가 열려 있어 기다리고 있어요.";
            return null;
        }
    }
    static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    // ---- voice ----
    readonly VoicePlayer voice = new();
    // Plays the clip registered for this moment (situation first, then the expression, then the general one).
    internal void PlayVoice(bool second, string situation, string mood, string text)
    {
        var options = Pref.Voice; if (!options.Enabled || closing || hidden || IsQuiet || light || policy.Mute) return;
        var clips = second ? companion?.Preset.VoicePaths : Pref.VoicePaths;
        string clip = VoiceSlots.Find(clips, situation, mood);
        if (clip.Length > 0 && File.Exists(clip)) voice.Play(clip, options.Volume);
        else if (options.ReadAloud && text.Length > 0) voice.Read(text, options.Volume);
    }
    public void PreviewVoice(string path, int volume) => voice.Play(path, volume);
    public void PreviewReading(string text, int volume) => voice.Read(text, volume);

    // ---- closeness ----
    // The stage of closeness, and for two pets how this one feels about being left out, in words for the AI.
    internal string Feelings(bool second, string? label = null)
    {
        var partner = companion; if (second && partner == null) return "";
        string id = second ? partner!.Preset.Id : CharacterId, name = second ? partner!.Preset.Profile.Name : Chat.Preferences.Profile.Name;
        string text = Affection.Context(Adventure.Friend(id, name).Bond);
        if (partner != null && Pref.Duo.Enabled)
        {
            var pair = Adventure.Pair(CharacterId, partner.Preset.Id);
            if (pair.Moods) text += PairMood.Context(pair.Mood, id, second ? Chat.Preferences.Profile.Name : partner.Preset.Profile.Name, Today);
        }
        // An anniversary is worth knowing about all day.
        var days = Anniversaries.Today(Adventure.State.Anniversaries, DateTime.Now); if (days.Length > 0) text += "\n[오늘의 기념일] 오늘은 " + SafeFiles.OneLine(string.Join(", ", days), 200) + "입니다. 어울리는 때에 자연스럽게 축하해 주세요.";
        return label == null ? text : text.Replace("\n[", "\n[" + label + " · ");
    }
    void StageUp(string id, string name, int stage)
    {
        if (closing) return; bool second = companion?.Preset.Id == id; if (!second && id != CharacterId) return;
        CelebrateWith("더 가까워졌어요", name + " · " + Affection.Name(stage), "친밀도 " + stage + "단계가 되었어요 ♡ " + Affection.Tone(stage), "친구 카드 보기", () => OpenAdventure(0));
        Auto.Say("stage-up", second, "happy", ("stage", Affection.Name(stage)));
        RecordPair("친밀도", name + " · " + stage + "단계 " + Affection.Name(stage), "");
    }
    // A pet was stroked: it may make up with the user, and says one of its petting lines.
    internal void Patted(bool second, bool counted = false)
    {
        string id = second ? companion?.Preset.Id ?? "" : CharacterId; if (id.Length == 0) return;
        if (counted) ProgressCount("pats");
        if (Cared(id) || IsChatBusy) return;
        if (Auto.Line(second, LineBook.Pat) is not string line) return;
        if (second) companion?.Say(line); else Say(line);
        PlayVoice(second, "pat", "happy", line);
    }

    // ---- the two pets: feeling left out ----
    PairCard? CurrentPair => companion != null && Pref.Duo.Enabled ? Adventure.Pair(CharacterId, companion.Preset.Id) : null;
    // The user chatted with "primary", "secondary" or "both".
    void NoteChat(string target)
    {
        ProgressCount("chats");
        var partner = companion; var pair = CurrentPair; if (partner == null || pair == null || !pair.Moods) return;
        if (target == "both") { PairMood.Together(pair.Mood, Today); return; }
        bool toSecond = target == "secondary"; string spoken = toSecond ? partner.Preset.Id : CharacterId, other = toSecond ? CharacterId : partner.Preset.Id;
        string result = PairMood.Chat(pair.Mood, spoken, other, Today); if (result.Length == 0) return;
        string spokenName = toSecond ? partner.Preset.Profile.Name : Chat.Preferences.Profile.Name, otherName = toSecond ? Chat.Preferences.Profile.Name : partner.Preset.Profile.Name;
        if (result == "sulk")
        {
            pair.Record("토라짐", otherName + "이(가) 토라졌어요", spokenName + "하고만 이야기해서 서운했대요."); Adventure.Touch();
            // After the answer is on screen, the one left out speaks up.
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => { if (toSecond) Express("sad", 8); else companion?.Express("sad"); Auto.Say("sulk", !toSecond, "sad", ("partner", spokenName)); });
        }
        else { pair.Record("화해", spokenName + "의 서운함이 풀렸어요", "먼저 말을 걸어 줘서 금방 풀렸대요."); Adventure.Touch(); }
    }
    // A pat or a gift. True when it ended a sulk (and the pet said so).
    public bool Cared(string id)
    {
        var partner = companion; var pair = CurrentPair; if (partner == null || pair == null || !pair.Moods) return false;
        if (PairMood.Care(pair.Mood, id, Today) != "makeup") return false;
        bool second = partner.Preset.Id == id; string name = second ? partner.Preset.Profile.Name : Chat.Preferences.Profile.Name;
        pair.Record("화해", name + "의 서운함이 풀렸어요", "챙겨 주니 금방 풀렸대요."); Adventure.Touch();
        if (second) partner.Express("happy"); else Express("happy", 6);
        return Auto.Say("makeup", second, "happy", ("partner", second ? Chat.Preferences.Profile.Name : partner.Preset.Profile.Name));
    }
    // The pet that got a gift answers in its own voice (by AI when a connection works; otherwise the plain notice).
    public void GiftReaction(string id, string gift, bool loved, string notice)
    {
        bool second = companion?.Preset.Id == id; if (!second && id != CharacterId) return;   // a pet that is not on screen has no bubble to answer in
        string instruction = "앱이 요청한 한마디입니다. 사용자가 방금 당신에게 '" + gift + "' 선물을 줬습니다. " + (loved ? "당신이 특히 좋아하는 선물입니다." : "특별히 좋아하는 선물은 아닙니다.")
            + " 이 캐릭터가 선물을 받았을 때 보일 반응을 성격과 말투 그대로 1~2문장으로 말하세요 (기뻐함·쑥스러움·당연하다는 태도·퉁명스러운 감사·떨떠름함 등 성격에 맞는 것). 프로필에 없는 다정함을 덧붙이지 마세요. 직전 자동 대사: {last_line}\n대사만 출력하세요.";
        _ = Auto.Respond(second, instruction, () => SayTo(id, notice, "happy"));
    }
    public void RecordPair(string kind, string title, string text) { var pair = CurrentPair; if (pair == null) return; pair.Record(kind, title, text); Adventure.Touch(); }
    // The last exchange between the two, kept in their album.
    public bool KeepLastExchange()
    {
        var last = Auto.LastPair; if (last.Text.Length == 0 || CurrentPair == null) return false;
        RecordPair("대화", last.Title, last.Text); return true;
    }

    // ---- the two pets: pictures of them together ----
    SceneWindow? sceneWindow; long lastScene = long.MinValue / 2;
    // Shows the picture registered for this moment, now and then (always when asked to).
    internal void OfferScene(string key, bool always = false)
    {
        var pair = CurrentPair; if (pair == null || closing || hidden || IsQuiet || light) return;
        if (!pair.Scenes.TryGetValue(key, out var path) || !File.Exists(path)) return;
        long now = Environment.TickCount64; if (!always && (now - lastScene < 600000 || Random.Shared.Next(3) != 0)) return;
        lastScene = now; sceneWindow?.Close();
        var shown = sceneWindow = new SceneWindow(this, path, PairScenes.Title(key)); shown.Closed += (_, _) => { if (ReferenceEquals(sceneWindow, shown)) sceneWindow = null; }; shown.Show();
    }
    public void PreviewScene(string key) => OfferScene(key, true);

    // ---- keepsakes ----
    public LineCollection Collection { get; } = new();
    // The id of the pet with this name, when it is one of the two on screen.
    public string IdOf(string speaker) => speaker == Chat.Preferences.Profile.Name ? CharacterId : companion?.Preset.Profile.Name == speaker ? companion.Preset.Id : "";

    // ---- windows ----
    Window? lineBookWindow, voiceWindow, anniversaryWindow, collectionWindow, pairWindow, guideWindow;
    void Open(ref Window? slot, Func<Window> make, Action<Window?> clear)
    {
        if (slot != null) { Pastel.Front(slot); return; }
        var window = make(); slot = window; window.Closed += (_, _) => clear(null); window.Show();
    }
    public void OpenLineBook() => Open(ref lineBookWindow, () => new LineBookWindow(this), w => lineBookWindow = w);
    public void OpenVoices() => Open(ref voiceWindow, () => new VoiceWindow(this), w => voiceWindow = w);
    public void OpenAnniversaries() => Open(ref anniversaryWindow, () => new AnniversaryWindow(this), w => anniversaryWindow = w);
    public void OpenCollection() => Open(ref collectionWindow, () => new CollectionWindow(this), w => collectionWindow = w);
    public void OpenPair() => Open(ref pairWindow, () => new PairWindow(this), w => pairWindow = w);
    public void ShowPairPage(int page) => (pairWindow as PairWindow)?.ShowPage(page);
    public void OpenGuide() => Open(ref guideWindow, () => new GuideWindow(this), w => guideWindow = w);
    public void OpenGuide(string section) { OpenGuide(); (guideWindow as GuideWindow)?.Select(section); }
    void CloseBondWindows() { foreach (var window in new[] { lineBookWindow, voiceWindow, anniversaryWindow, collectionWindow, pairWindow, guideWindow, sceneWindow }) window?.Close(); }
    // The first start on this PC: a short hello with the few things worth knowing.
    void Welcome()
    {
        if (closing || Pref.WelcomeSeen || Pastel.Unattended) return;
        Pref.WelcomeSeen = true; Storage.Save(Pref); new WelcomeWindow(this).Show();
    }
    public void ShowWelcome() => new WelcomeWindow(this).Show();
}
