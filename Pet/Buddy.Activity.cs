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

// Typing input and the one-second tick. What the pets say on their own is in AutoLines.
public sealed partial class Buddy
{
    void OnInput(bool keyboard)
    {
        if (closing || lockedSession || (!keyboard && !Pref.MouseActivity)) return;
        long now = Environment.TickCount64;
        activity.Input(now, keyboard); if (!bubble.IsReply) bubble.Hide();
        if (auto?.BusyWithNudge == true) CancelAutomatic();
        if (reactionPreview?.IsActive == true) return;
        if (keyboard)
        {
            companion?.Tap(light);
            bool measuring = Pref.Desktop.WorkEnabled && !policy.ExcludeWork && foregroundName != "KeyboardWorker" && !activity.IsPaused(now) && FocusTimer.Phase != "휴식";
            if (measuring)
            {
                long previous = Work.SessionMs; Work.Key(now, WorkTimeStore.Today, Pref.Desktop.WorkGapMs);
                var reward = Game.Work(Work.SessionMs - previous, WorkTimeStore.Today); if (reward.LevelUp) GameNotice(reward.Message);
                ProgressWork(Work.SessionMs - previous);
                if (Game.State.Enabled)
                {
                    Adventure.Work(CharacterId, Chat.Preferences.Profile.Name, Work.SessionMs - previous);
                    if (companion != null) { Adventure.Work(companion.Preset.Id, companion.Preset.Profile.Name, Work.SessionMs - previous); Adventure.WorkPair(CharacterId, companion.Preset.Id, Work.SessionMs - previous); }
                }
            }
            else Work.Suspend();
            if (!light && !IsQuiet && !policy.Mute && !manualHidden && !(Pref.HideFullscreen && fullscreenNow)) audio.Play(Pref);
            if (!hidden && (Pref.Desktop.TypingPriority || !ExpressionActive()))
            {
                if (reactionLimiter.Accept(now, light ? Math.Max(1000, Pref.Desktop.TypingReactionMs) : Pref.Desktop.TypingReactionMs)) Display(hands.Tap());
                if (!light && !IsQuiet && Pref.Desktop.TypingBounce && bodyLimiter.Accept(now, Pref.Desktop.BodyReactionMs)) { bounceLeft = !bounceLeft; bounce.Tap(bounceLeft, Pref.Desktop.BounceStrength); }
                animation.Stop(); animation.Start();
            }
        }
        else if (!hidden) Display(IdlePose());
    }
    // Once a second (every three seconds while hidden), and right away when another window comes to the front.
    void Tick()
    {
        long now = Environment.TickCount64;
        // Stacking is restored from the foreground-change hook; poll only if that hook could not be installed.
        if (foregroundMonitor?.Registered != true) RaisePets();
        bool silent = IsQuiet; if (silent != wasQuiet) { wasQuiet = silent; activity.Reset(now); bounce.Reset(); audio.Stop(); bodyLimiter.Reset(); }
        RefreshPolicy();
        Meter.Sample(now, light);
        CountProgramTime(now);
        ShowOrHide(now);
        bool paused = UpdateRest(now, silent);
        ShowWaitingCelebration();
        bubble.Tick(now, hidden);
        companion?.Tick(now, hidden);
        UpdateMusicPlayer();
        UpdateWorkClock(now, paused);
        var notification = FocusTimer.Tick(now); if (notification != null) { Work.Suspend(); activity.Reset(now); if (!silent) Say(notification); }
        if (!hidden && !animation.IsEnabled && state != IdlePose()) Display(IdlePose());
        ProgressStep(now);
        Auto.Step(now);
    }
    // Time with a program in front counts while the user is around: recent input anywhere, or something playing.
    void CountProgramTime(long now)
    {
        long step = Math.Min(5000, now - lastUsageTick); lastUsageTick = now;
        if (step > 0 && apps.InFront && !lockedSession && (Native.IdleMs() < 600000 || Music.Song.Playing)) Usage.AddApp(WorkTimeStore.Today, apps.Current.Title, apps.Current.Kind, step);
    }
    // Hidden while the screen is locked, when the user hid the pet, by a program rule, in full screen or during a game (if chosen).
    void ShowOrHide(long now)
    {
        bool hide = lockedSession || manualHidden || policy.Hide || (Pref.HideFullscreen && fullscreenNow) || (Pref.Awareness.GameHide && apps.GameInFront);
        if (hide != hidden)
        {
            hidden = hide; Opacity = hide ? 0 : 1; Native.Style(hwnd, hide || Pref.ClickThrough);
            if (hide) { animation.Stop(); bounce.Reset(); bubble.Hide(); if (manualHidden || fullscreenNow) audio.Stop(); }
            else { activity.Reset(now); Display("idle"); RaisePets(); }
            Music.Rest(hide);
        }
        // A hidden pet only has to notice when it may come back (app switches arrive as events), so it ticks less often.
        var pace = TimeSpan.FromSeconds(hidden ? 3 : 1); if (clock.Interval != pace) clock.Interval = pace;
    }
    // Keeps the tray text current and ends a rest cleanly. Returns whether the user is resting.
    bool UpdateRest(long now, bool silent)
    {
        bool paused = activity.IsPaused(now) || FocusTimer.Phase == "휴식";
        string trayText = silent ? "Keyboard Worker · 조용한 작업 모드" : paused ? $"Keyboard Worker · 휴식 {RestSeconds / 60:00}:{RestSeconds % 60:00}" : "Keyboard Worker · 함께 작업하는 중";
        if (tray != null && tray.Text != trayText) tray.Text = trayText;
        if (wasPaused && !paused)
        {
            activity.Reset(now);
            if (resumeFocusAfterRest && !lockedSession && FocusTimer.Running && FocusTimer.Paused) FocusTimer.TogglePause(now);
            resumeFocusAfterRest = false;
            Auto.Say("rest-end", null, "happy");
        }
        wasPaused = paused; return paused;
    }
    void UpdateMusicPlayer()
    {
        if (musicPlayer == null) return;
        if (hidden) musicPlayer.Sleep();
        else if (Pref.Music.Enabled && Pref.Music.PlayerVisible && Music.Song.Available) { if (!musicPlayer.IsVisible) musicPlayer.Show(); musicPlayer.Update(); }
    }
    // Rolls the day over, stops counting when work should not be measured, and saves progress every half minute.
    void UpdateWorkClock(long now, bool paused)
    {
        Work.RollDay(WorkTimeStore.Today);
        Game.RollDay(WorkTimeStore.Today);
        if (!Pref.Desktop.WorkEnabled || Work.Paused || policy.ExcludeWork || foregroundName == "KeyboardWorker" || paused || FocusTimer.Phase == "휴식") Work.Suspend();
        string workText = "오늘 " + TypingWorkClock.Format(Work.TodayMs); if (workLabel.Text != workText) workLabel.Text = workText;
        if (now - lastWorkSave < 30000) return;
        lastWorkSave = now;
        Game.Save();
        Adventure.Save();
        Usage.SetWork(Work.Day, Work.TodayMs); Usage.Save();
        if (savedWorkMs != Work.TodayMs || savedWorkDay != Work.Day) { WorkTimeStore.Save(Work); savedWorkMs = Work.TodayMs; savedWorkDay = Work.Day; }
    }
}
