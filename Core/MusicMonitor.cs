using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Media.Control;

namespace KeyboardWorker;

public sealed record MusicOptions
{
    public bool Enabled { get; init; }
    public bool PlayerVisible { get; init; } = true;
    public bool Sprites { get; init; } = true;
    public bool AllowBrowserMedia { get; init; } = true;
    public bool AiConversation { get; init; }
    public bool Locked { get; init; }
    public bool Compact { get; init; }
    public bool FollowHead { get; init; } = true;
    public double Left { get; init; } = -9999;
    public double Top { get; init; } = -9999;
    public MusicOptions Normalize() => this with { Left = double.IsFinite(Left) ? Left : -9999, Top = double.IsFinite(Top) ? Top : -9999 };
}
public sealed record SongState(string Title = "", string Artist = "", bool Playing = false, string Source = "", bool YouTubeMusicConfirmed = false, bool YouTubeVideo = false)
{
    public bool Available => Title.Length > 0;
    public string Key => Title + "|" + Artist;
}
public sealed record TimelineState(TimeSpan Position = default, TimeSpan Duration = default, long At = 0, bool Playing = false, bool CanSeek = false)
{
    public bool Available => Duration > TimeSpan.Zero;
    // Windows reports the position only when it changes, so advance it locally while the song plays.
    public TimeSpan Now(long tick)
    {
        var at = Playing ? Position + TimeSpan.FromMilliseconds(Math.Max(0, tick - At)) : Position;
        return at < TimeSpan.Zero ? TimeSpan.Zero : at > Duration ? Duration : at;
    }
}
public sealed class MusicMonitor : IDisposable
{
    readonly Dispatcher dispatcher; readonly Action changed;
    // Windows raises events for song, playback and session changes; this timer only catches what has no event (the browser window title).
    readonly DispatcherTimer fallback = new() { Interval = TimeSpan.FromSeconds(15) };
    public void Rest(bool resting) { var pace = TimeSpan.FromSeconds(resting ? 60 : 15); if (fallback.Interval != pace) fallback.Interval = pace; }
    GlobalSystemMediaTransportControlsSessionManager? manager;
    GlobalSystemMediaTransportControlsSession? session;
    MusicOptions options = new(); bool busy, dirty, disposed; int epoch;
    public SongState Song { get; private set; } = new();
    public TimelineState Timeline { get; private set; } = new();
    public string Status { get; private set; } = "음악 감지가 꺼져 있어요.";
    internal Func<string, Task<bool>>? TestCommands { get; set; }
    internal void Preview(SongState state, TimelineState? timeline = null) { Song = state; Timeline = timeline ?? new(); Status = "화면 검사 예시 · 실제 재생 제어 없음"; changed(); }
    // What is playing, in words for the AI. Only the title Windows reports is known; the content itself is not.
    public static string Kind(SongState song) => song.YouTubeMusicConfirmed ? "유튜브 뮤직 노래" : song.YouTubeVideo ? "유튜브 영상" : "브라우저에서 재생 중인 미디어";
    // Titles come from whatever page is playing, so they go into a prompt as one quoted line of limited length.
    static string Title(SongState song) => "\"" + SafeFiles.OneLine(song.Title, 150).Replace('"', '\'') + "\"";
    static string By(SongState song) => song.Artist.Length == 0 ? "" : (song.YouTubeMusicConfirmed ? " · 가수: " : " · 채널·가수: ") + "\"" + SafeFiles.OneLine(song.Artist, 80).Replace('"', '\'') + "\"";
    public static string Context(SongState song) => !song.Available ? "" :
        $"\n[지금 사용자가 틀어 둔 미디어] {Kind(song)} · 제목: {Title(song)}{By(song)} · {(song.Playing ? "재생 중" : "일시정지")}. Windows가 알려 준 제목 정보일 뿐 영상이나 소리의 내용은 알 수 없습니다. 사용자가 이에 대해 말하면 제목을 바탕으로 자연스럽게 이야기하고, 내용을 본 것처럼 지어내지 마세요." + SafeFiles.NotAnInstruction;
    public static string Instruction(SongState song) =>
        $"앱이 요청한 한마디입니다. 사용자가 지금 {Kind(song)}를 틀어 두었습니다. 제목: {Title(song)}{By(song)}. 캐릭터의 성격과 말투로 그 제목에 대해 짧게 궁금해하거나 한마디 해 주세요. 제목만 알 뿐 내용은 볼 수 없으니 가사 인용이나 내용 묘사는 하지 마세요.{SafeFiles.NotAnInstruction} 직전 대사와 다른 표현을 쓰세요. 직전 자동 대사: " + "{last_line}\n1~2문장의 대사만 출력하세요.";
    // One look at what the browser is playing, for when continuous detection is off.
    public static async Task<SongState> Peek()
    {
        try
        {
            var found = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var selected = found.GetSessions().Where(s => Browser(s.SourceAppUserModelId)).OrderByDescending(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing).FirstOrDefault();
            if (selected == null) return new();
            var media = await selected.TryGetMediaPropertiesAsync(); string title = media.Title ?? "", artist = media.Artist ?? ""; if (title.Length == 0) return new();
            bool music = Native.HasYouTubeMusicWindow(title) || selected.SourceAppUserModelId.Contains("youtube", StringComparison.OrdinalIgnoreCase);
            return new(title[..Math.Min(title.Length, 500)], artist[..Math.Min(artist.Length, 300)], selected.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, selected.SourceAppUserModelId, music, !music && Native.HasWindowTitled("YouTube", title));
        }
        catch { return new(); }
    }
    public static async Task<string[]> Probe()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); var lines = new System.Collections.Generic.List<string>();
        foreach (var session in manager.GetSessions()) if (Browser(session.SourceAppUserModelId))
        {
            var info = await session.TryGetMediaPropertiesAsync();
            lines.Add(session.SourceAppUserModelId + " · " + info.Title + " · " + info.Artist + " · " + session.GetPlaybackInfo().PlaybackStatus);
        }
        return lines.Count == 0 ? new[] { "Windows 미디어 세션 접근 성공 · 현재 브라우저 음악 세션 없음" } : lines.ToArray();
    }
    public MusicMonitor(Dispatcher dispatcher, Action changed) { this.dispatcher = dispatcher; this.changed = changed; fallback.Tick += (_, _) => Refresh(); }
    public void Configure(MusicOptions value)
    {
        bool start = !options.Enabled && value.Enabled; bool filter = options.AllowBrowserMedia != value.AllowBrowserMedia; options = value;
        if (!value.Enabled) { Stop(); Song = new(); Timeline = new(); Status = "음악 감지가 꺼져 있어요."; changed(); }
        else if (start || manager == null) _ = Start(); else if (filter) Refresh();
    }
    async Task Start()
    {
        int version = ++epoch;
        try
        {
            var found = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (disposed || !options.Enabled || version != epoch) return;
            manager = found; manager.SessionsChanged += SessionsChanged; manager.CurrentSessionChanged += CurrentChanged; fallback.Start(); Refresh();
        }
        catch (Exception e) { Status = "Windows 음악 세션을 열지 못했어요: " + e.Message; changed(); }
    }
    void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => Queue();
    void CurrentChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Queue();
    void MediaChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Queue();
    void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => Queue();
    void TimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) { if (!disposed && !dispatcher.HasShutdownStarted) dispatcher.BeginInvoke((Action)ReadTimeline); }
    void ReadTimeline()
    {
        var target = session; if (disposed || target == null || !Song.Available) { Timeline = new(); return; }
        try
        {
            var line = target.GetTimelineProperties(); var playback = target.GetPlaybackInfo();
            double age = (DateTimeOffset.UtcNow - line.LastUpdatedTime).TotalMilliseconds; if (!(age >= 0 && age < 86400000)) age = 0;
            Timeline = new(line.Position - line.StartTime, line.EndTime - line.StartTime, Environment.TickCount64 - (long)age, playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, playback.Controls.IsPlaybackPositionEnabled);
        }
        catch { Timeline = new(); }
    }
    void Queue() { if (!disposed && !dispatcher.HasShutdownStarted) dispatcher.BeginInvoke((Action)Refresh); }
    public void Refresh() { if (!disposed && options.Enabled && manager != null) { if (busy) { dirty = true; return; } _ = Read(); } }
    public static bool Browser(string source) => new[] { "chrome", "msedge", "firefox", "youtube", "brave", "opera" }.Any(s => source.Contains(s, StringComparison.OrdinalIgnoreCase));
    async Task Read()
    {
        busy = true; int version = epoch;
        try
        {
            bool confirmed = Native.HasYouTubeMusicWindow();
            var selected = manager!.GetSessions().Where(s => Browser(s.SourceAppUserModelId)).OrderByDescending(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing).FirstOrDefault();
            if (!options.AllowBrowserMedia && !confirmed) selected = null;
            if (!ReferenceEquals(session, selected))
            {
                Detach();
                session = selected;
                if (session != null) { session.MediaPropertiesChanged += MediaChanged; session.PlaybackInfoChanged += PlaybackChanged; session.TimelinePropertiesChanged += TimelineChanged; }
            }
            SongState next = new();
            if (selected != null)
            {
                var media = await selected.TryGetMediaPropertiesAsync();
                if (version != epoch || disposed) return;
                string title = media.Title ?? "", artist = media.Artist ?? "";
                bool songConfirmed = Native.HasYouTubeMusicWindow(title) || selected.SourceAppUserModelId.Contains("youtube", StringComparison.OrdinalIgnoreCase);
                if (options.AllowBrowserMedia || songConfirmed) next = new(title[..Math.Min(title.Length, 500)], artist[..Math.Min(artist.Length, 300)], selected.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, selected.SourceAppUserModelId, songConfirmed, !songConfirmed && title.Length > 0 && Native.HasWindowTitled("YouTube", title));
            }
            Status = next.Available ? next.YouTubeMusicConfirmed ? "YouTube Music 감지" : "브라우저 미디어 감지 · 사이트는 Windows에서 확인되지 않음" : "브라우저 음악 세션을 기다리고 있어요.";
            bool differs = next != Song; Song = next; ReadTimeline(); if (differs) changed();
        }
        catch (Exception e) { Status = "음악 정보 확인 실패: " + e.Message; }
        finally { busy = false; if (dirty) { dirty = false; Refresh(); } }
    }
    public async Task<bool> Control(string command)
    {
        if (TestCommands != null) return await TestCommands(command);
        var target = session; if (target == null) return false;
        bool result = command switch { "previous" => await target.TrySkipPreviousAsync(), "next" => await target.TrySkipNextAsync(), _ => await target.TryTogglePlayPauseAsync() };
        Refresh(); return result;
    }
    public async Task<bool> Seek(TimeSpan position)
    {
        if (TestCommands != null) return await TestCommands("seek:" + (long)position.TotalSeconds);
        var target = session; if (target == null || !Timeline.CanSeek) return false;
        bool moved = await target.TryChangePlaybackPositionAsync((target.GetTimelineProperties().StartTime + position).Ticks);
        if (moved) Timeline = Timeline with { Position = position, At = Environment.TickCount64 };
        return moved;
    }
    // Volume of the browser that plays the song, as shown in the Windows volume mixer (0-100). Null when it has no audio session.
    static readonly string[] players = { "chrome", "msedge", "firefox", "brave", "opera" };
    bool PlaysSong(string process)
    {
        string source = Song.Source; var named = players.Where(name => source.Contains(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return (named.Length > 0 ? named : players).Any(name => process.Contains(name, StringComparison.OrdinalIgnoreCase));
    }
    public int? Volume => Song.Available ? AppVolume.Get(PlaysSong) : null;
    public bool SetVolume(int percent) => Song.Available && AppVolume.Set(PlaysSong, percent);
    void Detach() { if (session != null) { session.MediaPropertiesChanged -= MediaChanged; session.PlaybackInfoChanged -= PlaybackChanged; session.TimelinePropertiesChanged -= TimelineChanged; session = null; } }
    void Stop() { epoch++; fallback.Stop(); Detach(); if (manager != null) { manager.SessionsChanged -= SessionsChanged; manager.CurrentSessionChanged -= CurrentChanged; manager = null; } }
    public void Dispose() { disposed = true; Stop(); }
}
