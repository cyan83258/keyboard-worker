using System;

namespace KeyboardWorker;

public static class ReactionFeatureTests
{
    public static int Run()
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        var old = new DesktopOptions { TypingReactionMs = 350, BounceReactionMs = null }; old.Normalize(); Check(old.BodyReactionMs == 350, "Legacy pacing remains shared until explicitly split");
        old.BounceReactionMs = 500; Check(old.TypingReactionMs == 350 && old.BodyReactionMs == 500, "Body interval is independent");
        var hand = new ReactionLimiter(); var body = new ReactionLimiter(); int handCount = 0, bodyCount = 0;
        for (int time = 0; time <= 1000; time += 50) { if (hand.Accept(time, 100)) handCount++; if (body.Accept(time, 400)) bodyCount++; }
        Check(handCount == 11 && bodyCount == 3, "Different hand and body rates accepted independently");
        var source = new Preferences(); source.Desktop.TypingReactionMs = 150; source.Desktop.BounceReactionMs = 450; source.Desktop.BounceStrength = 3; source.Desktop.TypingBounce = false;
        var preset = CharacterPreset.Capture("motion", source, new Persona()); source.Desktop.BounceStrength = 10;
        var destination = new Preferences(); destination.Desktop.WorkGapMs = 7000; destination.Desktop.FocusMinutes = 40;
        var prepared = preset.Prepare(destination, new ChatPreferences());
        Check(prepared.Settings.Desktop.TypingReactionMs == 150 && prepared.Settings.Desktop.BodyReactionMs == 450 && prepared.Settings.Desktop.BounceStrength == 3 && !prepared.Settings.Desktop.TypingBounce, "Preset restores independent pacing and strength");
        Check(prepared.Settings.Desktop.WorkGapMs == 7000 && prepared.Settings.Desktop.FocusMinutes == 40, "Motion preset does not alter work timer settings");
        preset.Motion = null;
        destination.Desktop.TypingReactionMs = 900;
        Check(preset.Prepare(destination, new ChatPreferences()).Settings.Desktop.TypingReactionMs == 900, "Old preset preserves current motion");
        var quiet = new QuietMode(); Check(!quiet.Active(0), "Quiet mode starts off"); quiet.Start(1000, 1);
        Check(quiet.Active(60999) && quiet.Seconds(1000) == 60, "Timed quiet mode active until deadline");
        Check(!quiet.Active(61000) && quiet.Seconds(61000) == 0, "Timed quiet mode expires exactly");
        quiet.Start(70000, 0);
        Check(quiet.Active(999999999) && quiet.Seconds(70000) == -1, "Manual quiet mode has no deadline");
        quiet.Stop();
        Check(!quiet.Active(70001), "Quiet mode stops immediately");
        var saved = destination.Desktop.Snapshot();
        quiet.Start(0, 10);
        quiet.Stop();
        Check(saved.TypingReactionMs == destination.Desktop.TypingReactionMs && saved.TypingBounce == destination.Desktop.TypingBounce, "Quiet state does not mutate preferences");
        var disabled = new DesktopOptions(); disabled.Shortcuts["quiet"] = ""; disabled.Normalize(); Check(disabled.Shortcuts["quiet"] == "", "Disabled quiet shortcut stays disabled");
        return count;
    }
}
