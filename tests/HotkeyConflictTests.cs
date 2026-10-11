using System;
using System.Collections.Generic;
using System.Windows.Interop;

namespace KeyboardWorker;

public static class HotkeyConflictTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        using var firstWindow = new HwndSource(new HwndSourceParameters("Hotkey conflict owner") { Width = 1, Height = 1, WindowStyle = 0 });
        using var secondWindow = new HwndSource(new HwndSourceParameters("Hotkey conflict applicant") { Width = 1, Height = 1, WindowStyle = 0 });
        using var owner = new GlobalHotkeys(firstWindow, _ => { });
        using var applicant = new GlobalHotkeys(secondWindow, _ => { });
        Dictionary<string, string>? occupied = null;
        foreach (string key in new[] { "F8", "F9", "F10", "F11", "F7" })
        {
            var candidate = new Dictionary<string, string> { ["quick"] = "Ctrl+Alt+Shift+" + key };
            try { owner.Configure(candidate); occupied = candidate; break; }
            catch (InvalidOperationException) { }
        }
        Check(occupied != null, "Reserve test shortcut without replacing existing registrations");
        bool failed = false;
        try { applicant.Configure(occupied!); } catch (InvalidOperationException) { failed = true; }
        Check(failed && !applicant.IsRegistered("quick"), "Startup shortcut conflict reproduced");
        applicant.ConfigureIfChanged(occupied!, new(occupied!));
        Check(!applicant.IsRegistered("quick"), "Unchanged shortcuts skip registration after startup failure");
        bool changedFailed = false;
        try { applicant.ConfigureIfChanged(new(), occupied!); } catch (InvalidOperationException) { changedFailed = true; }
        Check(changedFailed, "Editing shortcut still reports genuine conflict");
        Check(owner.IsRegistered("quick"), "Settings save leaves other owner's shortcut registered");
        return count;
    }
}
