using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

// Everything the pets say on their own: greetings, nudges, random lines, remarks about what the user is doing, the two pets
// talking to each other, and anniversaries. Each has an AI form (when AI lines are on and a request is allowed) and a form
// that uses the lines the user wrote (see LineBook). The program has no lines of its own: with nothing written and no AI
// the pets say nothing. Each kind of line has one switch: on, the AI makes it; off, the line the user wrote is said.
// Buddy calls Step once a second; everything shown goes back through Buddy (bubbles, poses, voice).
public sealed class AutoLines
{
    readonly Buddy pet;
    readonly AutomaticClock aiClock = new(), duoClock = new(), localClock = new();
    readonly AutomaticBatch batch = new();
    readonly SpeakerTurns turns = new();
    readonly long started = Environment.TickCount64;
    CancellationTokenSource? cancellation; bool busy, busyNudge, playing;
    string lastLine = "", lastDuoLine = "", lastLocal = "", loggedFailure = "", lastMusicTrack = "", mediaConsidered = "", greeted = "";
    long lastMusicTalk = long.MinValue / 2, lastMediaLine = long.MinValue / 2;
    public AutoLines(Buddy pet) { this.pet = pet; }

    Preferences Pref => pet.Pref;
    ChatRepository Chat => pet.Chat;
    CompanionWindow? Partner => pet.DisplayedCompanion;
    public string Status { get; set; } = "아직 자동 대사를 생성하지 않았어요.";
    public bool Busy => busy;
    // A nudge is being prepared: typing again makes it pointless.
    public bool BusyWithNudge => busy && busyNudge;
    public void Cancel() => cancellation?.Cancel();
    // The last exchange between the two pets, so the user can join in or keep it. Empty when none is recent.
    public (string Title, string Text, long At) LastPair { get; private set; } = ("", "", long.MinValue / 2);

    // ---- shared plumbing ----
    // Marks the pets as busy with one automatic line; nothing else starts until End.
    public CancellationTokenSource Begin(string status, bool nudge = false) { busy = true; busyNudge = nudge; var source = new CancellationTokenSource(); cancellation = source; Status = status; return source; }
    public void End(CancellationTokenSource source) { if (ReferenceEquals(cancellation, source)) cancellation = null; source.Dispose(); busy = false; playing = false; }
    public void Failed(string what, Exception error, bool penalize = true)
    {
        if (penalize) aiClock.Failed(Environment.TickCount64);
        Status = what + " 실패 · 5분 쉬기: " + error.Message;
        if (error.Message != loggedFailure) { loggedFailure = error.Message; ErrorLog.Write(what + " 실패", error.Message); }
    }
    // Automatic lines pause only when they would get in the way: hidden, quiet or resting, a chat answer in progress,
    // or the user typing into the quick box. Other open windows (settings, chat, shop…) do not stop them.
    public bool Eligible(bool allowBubble = false) => pet.MaySpeak && (allowBubble || !pet.MainBubble.IsVisible);
    // With pacing on, automatic AI requests wait once today's share for this hour is used; chat is never held back.
    public bool BudgetOpen()
    {
        var budget = Chat.Preferences.Budget; if (!budget.Enabled || !budget.Pace) return true;
        return AiUsage.Current.Snapshot().AutomaticCalls < AiUsage.Allowance(budget, DateTime.Now);
    }
    AutomaticOptions LocalSchedule => new() { RandomLines = Pref.LocalRandom, RandomMinMinutes = Pref.LocalRandomMinMinutes, RandomMaxMinutes = Pref.LocalRandomMaxMinutes, CooldownSeconds = 60, HourlyLimit = 60 };

    // With two pets out (and sharing on) either of them says the next line; null means the main pet.
    CompanionWindow? NextSpeaker()
    {
        var partner = Partner; if (partner == null || !Pref.Duo.Enabled || !Pref.Duo.ShareLines || partner.Opacity == 0) return null;
        return turns.NextIsSecond() ? partner : null;
    }
    string IdOf(CompanionWindow? speaker) => speaker?.Preset.Id ?? pet.CharacterId;
    string NameOf(CompanionWindow? speaker) => speaker?.Preset.Profile.Name ?? Chat.Preferences.Profile.Name;
    int StageOf(CompanionWindow? speaker) => Affection.Stage(pet.Adventure.Friend(IdOf(speaker), NameOf(speaker)).Bond);
    // A line the user wrote for this pet and situation, or null. The main pet's lines are in the settings, the second pet's in its preset.
    string? Written(CompanionWindow? speaker, string situation, params (string Key, string Value)[] extra)
    {
        string text = speaker == null ? LineBook.Text(Pref.LineBook, Pref.Lines, Pref.PatLines, situation) : LineBook.Text(speaker.Preset.LineBook, speaker.Preset.NudgeLines, speaker.Preset.PatLines ?? new Preferences().PatLines, situation);
        var other = speaker == null ? Partner?.Preset.Profile.Name : Chat.Preferences.Profile.Name;
        var values = new Dictionary<string, string> { ["user"] = (speaker?.Preset.Profile.UserName ?? Chat.Preferences.Profile.UserName) ?? "", ["name"] = NameOf(speaker), ["partner"] = other ?? "" };
        foreach (var (key, value) in extra) values[key] = value;
        return LineBook.Pick(text, StageOf(speaker), values, ref lastLocal);
    }
    void Speak(CompanionWindow? speaker, string text, string mood, bool nudge, string situation = "speak")
    {
        int seconds = nudge ? Pref.BubbleSeconds : Pref.Desktop.ReplySeconds; bool second = speaker != null && ReferenceEquals(speaker, Partner);
        if (second) { if (nudge) speaker!.Nudge(text, seconds); else { speaker!.Express(mood); speaker.Speak(text, true, seconds); } }
        else { if (!nudge) pet.ChatMood(mood); pet.MainBubble.ShowText(text, !nudge, seconds); }
        pet.PlayVoice(second, nudge ? "nudge" : situation, mood, text);
    }
    // The chat settings with the speaking pet's own profile and prompts, plus what it may know right now.
    ChatPreferences Voice(ChatPreferences preferences, CompanionWindow? speaker)
    {
        var copy = preferences.Snapshot(); if (speaker != null) { copy.Profile = speaker.Preset.Profile.Snapshot(); copy.Profile.Prompts = (copy.Profile.Prompts ?? new()).Normalize(); }
        copy.Profile.Extra += pet.Feelings(speaker != null);
        // The part of the day goes along when that is shared, so a morning line is not said at night.
        if (Pref.Awareness.Clock) copy.Profile.Extra += DayClock.Rough(DateTime.Now);
        return copy;
    }

    // ---- once a second ----
    // In order of priority: an anniversary, a greeting, a story step, a remark about the program or media, the two pets
    // talking, a random line, and the nudge after a long pause. At most one starts per tick, and none while a bubble is showing.
    public void Step(long now)
    {
        // Once, for settings from before remarks and talk of songs had switches of their own: they used to follow the AI
        // nudge / random line, so whoever had those off does not suddenly get AI requests.
        if (!Pref.AiChoicesSet)
        {
            var before = Chat.Preferences.Automatic; Pref.AiChoicesSet = true;
            if (!before.AiNudge && !before.RandomLines) { Pref.Awareness = Pref.Awareness with { CommentsUseAi = false }; Pref.Music = Pref.Music with { AiConversation = false }; }
            Storage.Save(Pref);
        }
        bool eligible = Eligible(true); var automatic = Chat.Preferences.Automatic; var pairSchedule = Pref.Duo.Schedule; var partner = Partner;
        aiClock.Observe(automatic, now, eligible);
        duoClock.Observe(pairSchedule, now, eligible && partner != null && Pref.Duo.Enabled);
        localClock.Observe(LocalSchedule, now, eligible);
        if (playing) return;   // the two pets are in the middle of an exchange
        if (!eligible) { Cancel(); return; }
        if (pet.MainBubble.IsVisible) { Cancel(); return; }
        if (busy || partner?.BubbleVisible == true) return;
        if (Anniversary()) return;
        if (Greeting(now)) return;
        if (StoryStepDue(automatic, now)) { aiClock.Reserve(true, automatic with { AiNudge = true }, now); _ = pet.GenerateAdventure(false); return; }
        if (Topic(now) is { } topic) { _ = SpeakTopic(topic.Instruction, topic.Local, topic.Mood, topic.Ai, topic.Situation); return; }
        if (duoClock.RandomReady(now) && aiClock.CanReserve(automatic, now) && BudgetOpen() && duoClock.Reserve(false, pairSchedule, now)) { aiClock.Reserve(true, automatic with { AiNudge = true }, now); _ = SpeakTogether(); return; }
        if (aiClock.RandomReady(now) && BudgetOpen() && aiClock.Reserve(false, automatic, now)) { _ = SpeakAutomatically(false); return; }
        // The user's own lines fill in whenever the AI is not going to speak.
        if (localClock.RandomReady(now) && localClock.Reserve(false, LocalSchedule, now) && !(automatic.RandomLines && BudgetOpen() && aiClock.CanReserve(automatic, now)) && LocalRandom()) return;
        if (!pet.Idle.TryNudge(now, Pref)) return;
        pet.ShowPose("nudge");
        if (!automatic.AiNudge) LocalNudge();
        else if (BudgetOpen() && aiClock.Reserve(true, automatic, now)) _ = SpeakAutomatically(true);
        else if (automatic.FallbackNudge) LocalNudge();
    }
    // A new quest is wanted, or a finished quest is waiting for its story, and the story feature may ask the AI now.
    bool StoryStepDue(AutomaticOptions automatic, long now)
    {
        var book = pet.Adventure.State; if (!book.Options.Enabled || DateTimeOffset.Now - book.LastAi < TimeSpan.FromMinutes(book.Options.MinAiMinutes)) return false;
        if (!aiClock.CanReserve(automatic, now) || !BudgetOpen()) return false;
        return book.Quests.Any(q => q.Ready && q.StoryId.Length == 0) || !book.Quests.Any(q => q.StoryId.Length == 0);
    }
    // Once a day: whatever is worth celebrating today gets its card, confetti and a line.
    bool Anniversary()
    {
        // A self-check run must not use up the real day's celebration.
        var book = pet.Adventure.State; string day = DateTime.Now.ToString("yyyy-MM-dd"); if (book.AnniversaryDay == day || Pastel.Unattended) return false;
        var events = Anniversaries.Today(book.Anniversaries, DateTime.Now); book.AnniversaryDay = day; pet.Adventure.Touch();
        if (events.Length == 0) return false;
        string title = string.Join(" · ", events); pet.CelebrateWith("오늘은 기념일", title, "함께 축하해요 ♡", "기념일 보기", pet.OpenAnniversaries);
        var speaker = NextSpeaker(); if (Written(speaker, "anniversary", ("event", events[0])) is string line) Speak(speaker, line, "happy", false, "celebrate");
        pet.RecordPair("기념일", title, ""); pet.OfferScene("celebrate"); return true;
    }
    // The first meeting in each part of the day, while the user is at the keyboard.
    bool Greeting(long now)
    {
        if (!Pref.Greetings || now - started < 4000 || Native.IdleMs() > 120000) return false;
        string situation = LineBook.Greeting(DateTime.Now.Hour), key = DateTime.Now.ToString("yyyy-MM-dd") + situation; if (key == greeted) return false;
        greeted = key; var speaker = NextSpeaker(); if (Written(speaker, situation) is not string line) return false;
        Speak(speaker, line, "happy", false, "greet"); if (situation == "greet-night") pet.OfferScene("night"); return true;
    }
    // A line of the user's own at random: one pet's, or (with two pets and exchanges written) the two talking.
    bool LocalRandom()
    {
        var partner = Partner;
        if (partner != null && Pref.Duo.Enabled && partner.Opacity > 0 && Random.Shared.Next(100) < 40 && Written(null, "pair") is string exchange) { _ = Play(LineBook.Turns(exchange).Select(t => new DuoTurn(t.Second, t.Text, PetMood.Guess(t.Text))).ToArray(), partner, "둘이 주고받은 대화"); return true; }
        var speaker = NextSpeaker(); if (Written(speaker, "random") is not string line) return false;
        Speak(speaker, line, PetMood.Guess(line), false); Status = "직접 쓴 한마디 표시 · " + DateTime.Now.ToString("HH:mm"); return true;
    }
    void LocalNudge() { var speaker = NextSpeaker(); if ((Written(speaker, LineBook.Nudge) ?? Written(null, LineBook.Nudge)) is string line) Speak(speaker, line, "neutral", true); }
    // Says a line the user wrote for a moment (rest, sulking, a new stage…); silent when none is written.
    // second: null lets either pet say it.
    public bool Say(string situation, bool? second, string mood, params (string Key, string Value)[] values)
    {
        var speaker = second == null ? NextSpeaker() : second == true ? Partner : null; if (second == true && speaker == null) return false;
        if (!pet.MayShow || Written(speaker, situation, values) is not string line) return false;
        Speak(speaker, line, mood, false, situation == "anniversary" || situation == "stage-up" ? "celebrate" : "speak"); return true;
    }

    public string? Line(bool second, string situation, params (string Key, string Value)[] values) => second && Partner == null ? null : Written(second ? Partner : null, situation, values);
    // What the two said to each other a moment ago, for a chat request that follows it.
    public string RecentPair => LastPair.Text.Length > 0 && Environment.TickCount64 - LastPair.At < 300000 ? "\n[방금 두 친구가 나눈 대화]\n" + LastPair.Text + "\n사용자가 이 대화에 끼어드는 것일 수 있으니 자연스럽게 이어 주세요." : "";

    // ---- remarks about what is going on ----
    // Something worth an unprompted remark: the program in front (tease about a game, praise for work), or a newly started video or song.
    (string Instruction, Func<CompanionWindow?, string?> Local, string Mood, bool Ai, string Situation)? Topic(long now)
    {
        var aware = Pref.Awareness;
        if (pet.Apps.RemarkDue(aware, now))
        {
            var about = pet.Apps.Current; long spent = pet.AppToday; pet.Apps.Remarked(now); bool longUse = spent >= 3600000;
            string situation = (longUse ? "long-" : "") + (about.Kind == "game" ? "game" : "work"); if (situation == "long-work") pet.OfferScene("work");
            return (AppKinds.Instruction(about, spent), speaker => Written(speaker, situation, ("app", about.Title), ("time", UsageLog.Span(spent))), about.Kind == "game" ? "angry" : "happy", aware.CommentsUseAi, "speak");
        }
        var song = pet.Music.Song;
        if (aware.Media && Pref.Music.Enabled && song.Playing && song.Key != mediaConsidered)
        {
            mediaConsidered = song.Key;   // each video or song gets one chance
            if (now - lastMediaLine >= 600000 && Random.Shared.Next(3) == 0) { lastMediaLine = now; return (MusicMonitor.Instruction(song), speaker => Written(speaker, "music", ("title", SafeFiles.OneLine(song.Title, 80))), "happy", Pref.Music.AiConversation, "speak"); }
        }
        return null;
    }
    // One line about a topic: by AI when AI lines are on and a request is allowed now, otherwise the line the user wrote for it - or nothing.
    async Task SpeakTopic(string instruction, Func<CompanionWindow?, string?> local, string mood, bool useAi, string situation)
    {
        long now = Environment.TickCount64; var automatic = Chat.Preferences.Automatic; var speaker = NextSpeaker();
        void Fallback() { if (local(speaker) is string line) Speak(speaker, line, mood, false, situation); }
        if (!useAi || !BudgetOpen() || !aiClock.Reserve(true, automatic with { AiNudge = true }, now)) { Fallback(); return; }
        var source = Begin("상황에 맞는 한마디 생성 중…"); var preferences = Chat.Preferences;
        try
        {
            var voice = Voice(preferences, speaker); voice.Profile.Prompts = (voice.Profile.Prompts ?? new()).Normalize() with { Random = instruction };
            var reply = await AutomaticGenerator.Generate(voice, Chat.Context(), false, 0, lastLine, source.Token, expressions: Pref.AdvancedImages);
            if (source.IsCancellationRequested || pet.IsClosing || pet.IsHidden || !ReferenceEquals(preferences, Chat.Preferences) || !Eligible()) return;
            lastLine = reply.Text; Speak(speaker, reply.Text, reply.Mood, false, situation); Status = "상황 한마디 표시 완료 · " + DateTime.Now.ToString("HH:mm");
        }
        catch (OperationCanceledException) { Status = "직접 대화 또는 작업 상태 변경으로 자동 생성을 중단했어요."; }
        catch (Exception e) { Failed("상황 한마디", e); if (Eligible()) Fallback(); }
        finally { End(source); }
    }

    // ---- one pet, by AI ----
    async Task SpeakAutomatically(bool nudge, bool test = false)
    {
        var source = Begin(nudge ? "AI 재촉 대사 생성 중…" : "랜덤 한마디 생성 중…", nudge); var preferences = Chat.Preferences; long inputAtStart = pet.Idle.LastInput; var speaker = NextSpeaker();
        try
        {
            var reply = await batch.Get(Voice(preferences, speaker), Chat.Context(), nudge, (Environment.TickCount64 - inputAtStart) / 1000, lastLine, source.Token, expressions: Pref.AdvancedImages && !nudge, speaker: speaker?.Preset.Id ?? "");
            if (source.IsCancellationRequested || pet.IsClosing || pet.IsHidden || !ReferenceEquals(preferences, Chat.Preferences)) return;
            if (!test && (!Eligible() || (nudge && (inputAtStart != pet.Idle.LastInput || !pet.Idle.Nudged)))) return;
            lastLine = reply.Text; Speak(speaker, reply.Text, reply.Mood, nudge);
            Status = "자동 대사 표시 완료 · " + DateTime.Now.ToString("HH:mm") + " · 최근 1시간 " + aiClock.Count(Environment.TickCount64) + "회 호출";
        }
        catch (OperationCanceledException) { Status = "직접 대화 또는 작업 상태 변경으로 자동 생성을 중단했어요."; }
        catch (Exception e)
        {
            Failed("자동 대사", e, !test);
            if (nudge && preferences.Automatic.FallbackNudge && ReferenceEquals(preferences, Chat.Preferences) && Eligible() && inputAtStart == pet.Idle.LastInput && pet.Idle.Nudged) LocalNudge();
        }
        finally { End(source); }
    }

    // ---- the two pets talking to each other ----
    async Task SpeakTogether(bool test = false)
    {
        var partner = Partner; if (partner == null) return; var options = Pref.Duo; var preferences = Chat.Preferences; var source = Begin("두 친구의 상호작용 대사 생성 중…");
        try
        {
            var pair = pet.Adventure.Pair(pet.CharacterId, partner.Preset.Id); string firstName = preferences.Profile.Name, secondName = partner.Preset.Profile.Name;
            string prompt = options.Prompt + (Pref.Awareness.Clock ? DayClock.Rough(DateTime.Now) : "") + pair.Context(firstName, secondName) + pet.Feelings(false, firstName) + pet.Feelings(true, secondName);
            var songAtStart = pet.Music.Song;
            bool aboutMusic = Pref.Music.AiConversation && songAtStart.Playing && songAtStart.Key != lastMusicTrack && Environment.TickCount64 - lastMusicTalk >= 1800000 && Random.Shared.Next(5) == 0;
            if (aboutMusic)
            {
                prompt += "\n이번에만 가끔 듣는 곡을 소재로 짧게 이야기하세요. 곡 제목: \"" + SafeFiles.OneLine(songAtStart.Title, 150) + "\" / 가수: \"" + SafeFiles.OneLine(songAtStart.Artist, 80) + "\". 가사나 실제 소리를 전달받은 것은 아닙니다. 가사 인용·근거 없는 음악 분석은 하지 말고 음악에만 매몰되지 마세요." + SafeFiles.NotAnInstruction;
                lastMusicTalk = Environment.TickCount64; lastMusicTrack = songAtStart.Key;
            }
            var reply = await DuoInteraction.Generate(preferences, partner.Preset.Profile.Snapshot(), options with { Prompt = prompt }, lastDuoLine, Chat.Context(), source.Token, usageKind: aboutMusic ? "AutoMusicDuo" : "AutoDuo", turns: options.Turns);
            if (source.IsCancellationRequested || pet.IsClosing || pet.IsHidden || !ReferenceEquals(partner, Partner) || !ReferenceEquals(preferences, Chat.Preferences)) return;
            if (!test && (Pref.Duo with { Left = options.Left, Top = options.Top } != options || !Eligible() || partner.BubbleVisible)) return;
            if (aboutMusic && (pet.Music.Song.Key != songAtStart.Key || !pet.Music.Song.Playing)) return;
            lastDuoLine = string.Join(" / ", reply.Turns.Select(t => t.Text));
            Status = "두 친구 대사 표시 완료 · " + DateTime.Now.ToString("HH:mm");
            await Show(reply.Turns, partner, "둘이 주고받은 대화", source.Token);
        }
        catch (OperationCanceledException) { Status = "상호작용 생성 중단"; }
        // A failed try from the test button does not put the automatic schedule on hold.
        catch (Exception e) { if (!test) duoClock.Failed(Environment.TickCount64); Failed("상호작용", e, !test); }
        finally { End(source); }
    }
    // An exchange written by the user: no request, just the lines in turn.
    async Task Play(IReadOnlyList<DuoTurn> lines, CompanionWindow partner, string title)
    {
        if (lines.Count == 0) return; var source = Begin("둘이 이야기하는 중…");
        try { await Show(lines, partner, title, source.Token); Status = "직접 쓴 대화 표시 · " + DateTime.Now.ToString("HH:mm"); }
        catch (OperationCanceledException) { }
        finally { End(source); }
    }
    // Shows the lines one after another, each pet in its own bubble, leaving time to read.
    async Task Show(IReadOnlyList<DuoTurn> lines, CompanionWindow partner, string title, CancellationToken token)
    {
        playing = true; string firstName = Chat.Preferences.Profile.Name, secondName = partner.Preset.Profile.Name;
        LastPair = (title, string.Join("\n", lines.Select(t => (t.Second ? secondName : firstName) + ": " + t.Text)), Environment.TickCount64);
        pet.Adventure.TalkPair(pet.CharacterId, partner.Preset.Id);
        for (int i = 0; i < lines.Count; i++)
        {
            if (pet.IsClosing || pet.IsHidden || !ReferenceEquals(partner, Partner)) break;
            Speak(lines[i].Second ? partner : null, lines[i].Text, lines[i].Mood, false);
            if (i < lines.Count - 1) await Task.Delay(Math.Clamp(1800 + lines[i].Text.Length * 70, 2600, 7000), token);
        }
        pet.OfferScene("talk");
    }

    // ---- for the settings windows ----
    // Why the pets are (not) about to speak.
    public string Outlook
    {
        get
        {
            long now = Environment.TickCount64; var automatic = Chat.Preferences.Automatic;
            if (pet.RestReason is string resting) return resting;
            if (busy) return playing ? "두 친구가 이야기하는 중이에요…" : "지금 대사를 만드는 중이에요…";
            string local = Pref.LocalRandom ? "직접 쓴 한마디: " + localClock.Outlook(LocalSchedule, now) : "직접 쓴 한마디는 꺼져 있어요";
            if (!automatic.AiNudge && !automatic.RandomLines && !Pref.Duo.Interactions) return "재촉·랜덤 한마디·두 친구의 대화는 대사집의 대사로 나와요. " + local;
            if (!BudgetOpen())
            {
                var budget = Chat.Preferences.Budget;
                return $"오늘 자동 요청을 하루에 고르게 나눠 쓰는 중이라 잠시 쉬어요 (오늘 {AiUsage.Current.Snapshot().AutomaticCalls}회 사용 · 지금까지 허용 {AiUsage.Allowance(budget, DateTime.Now)}회 · 하루 {budget.AutomaticCalls}회). 그동안은 직접 쓴 대사가 나와요. " + local;
            }
            string text = automatic.RandomLines ? "다음 AI 한마디: " + aiClock.Outlook(automatic, now) : "AI 랜덤 한마디는 꺼져 있어요";
            if (Partner != null && Pref.Duo.Enabled) text += Pref.Duo.Interactions ? " · 다음 상호작용: " + duoClock.Outlook(Pref.Duo.Schedule, now) : " · 상호작용은 꺼져 있어요";
            return text + " · " + local;
        }
    }
    // Speak once right now with the saved settings, so the user can see it work (or see the reason it fails).
    public string Test(bool together)
    {
        if (busy || pet.IsChatBusy) return "지금 다른 대사를 만드는 중이에요. 잠시 후 다시 눌러 주세요.";
        if (pet.IsHidden) return "캐릭터가 숨겨져 있어요. 먼저 보이게 해 주세요.";
        if (together && Partner == null) return "둘째 친구를 먼저 불러내 주세요.";
        pet.MainBubble.Hide(); _ = together ? SpeakTogether(true) : SpeakAutomatically(false, true); return "대사를 만드는 중… 잠시 후 캐릭터가 말해요.";
    }
}
