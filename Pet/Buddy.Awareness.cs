using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace KeyboardWorker;

// What the pet knows about its surroundings: the program in front and time spent in it, lighter running during games,
// lines it has said, its own resource use, and resizing with the mouse wheel.
public sealed partial class Buddy
{
    public UsageLog Usage { get; } = StartupTrace.Time("통계", () => new UsageLog());
    public PerfMeter Meter { get; } = new();
    long lastUsageTick = Environment.TickCount64;
    internal long AppToday => Usage.AppMs(WorkTimeStore.Today, apps.Current.Title);
    readonly LineHistory history = new();
    public System.Collections.Generic.IReadOnlyList<SpokenLine> SpokenLines => history.Items;
    internal void Remember(string speaker, string text) => history.Remember(speaker, text, DateTime.Now);
    LineHistoryWindow? linesWindow;
    public void OpenLines() { if (linesWindow != null) { Pastel.Front(linesWindow); return; } linesWindow = new LineHistoryWindow(this); linesWindow.Closed += (_, _) => linesWindow = null; linesWindow.Show(); }
    public void OpenStats() => new StatsWindow(this) { ShowInTaskbar = false }.Show();
    // Ctrl + mouse wheel over a pet: both pets share one size setting.
    System.Windows.Threading.DispatcherTimer? sizeSave;
    public void ResizeBy(int change)
    {
        int size = Math.Clamp(Pref.Size + change, 100, 400); if (size == Pref.Size || closing) return;
        var next = Pref.Snapshot(); next.Size = size; Apply(next, configureKeys: false, persist: false);
        if (sizeSave == null) { sizeSave = new() { Interval = TimeSpan.FromMilliseconds(700) }; sizeSave.Tick += (_, _) => { sizeSave!.Stop(); if (!closing) Storage.Save(Pref); }; }
        sizeSave.Stop(); sizeSave.Start();
    }
    // The program the user is working in; see AppTracker.
    readonly AppTracker apps = new(Random.Shared.Next(100));
    public string AppSummary => apps.Summary;
    // While a game or a full-screen program is in front the pets keep still: no typing sound, bounce or hearts, slower hand reactions,
    // and this process steps aside for the other program.
    bool light;
    public bool IsLight => light;
    void RefreshLight()
    {
        bool lighter = Pref.Awareness.GameLight && !lockedSession && apps.InFront && (apps.Current.Kind == "game" || (fullscreenNow && apps.Current.Kind != "work"));
        if (lighter == light) return; light = lighter; Native.Yield(light);
        if (light) { bounce.Reset(); hearts.Clear(); audio.Stop(); Native.TrimMemory(); }
    }
    // What the pets may know about right now, added to a chat request: the program in use and the video or song playing.
    async System.Threading.Tasks.Task<string> Surroundings(bool? media)
    {
        string text = (Pref.Awareness.Clock ? DayClock.Exact(DateTime.Now) : "") + (Pref.Awareness.Apps ? AppKinds.Context(apps.Current, AppToday) : "") + TodoNote;
        if (media ?? Pref.Awareness.Media)
        {
            var song = Pref.Music.Enabled && Music.Song.Available ? Music.Song : media == true ? await MusicMonitor.Peek() : new SongState();
            text += MusicMonitor.Context(song);
        }
        return text;
    }
    public void OpenAwareness(Window owner) => new AwarenessWindow(this) { Owner = owner, ShowInTaskbar = false }.ShowDialog();
}
