using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace KeyboardWorker;

// Monotonic milliseconds: wall-clock changes cannot trigger reminders.
public sealed class Activity
{
    public long LastInput { get; private set; }
    public long LastTyping { get; private set; } = long.MinValue / 2;
    public long LastNudge { get; private set; } = long.MinValue / 2;
    public long PauseUntil { get; private set; }
    public bool Nudged { get; private set; }
    public Activity(long now) { LastInput = now; }
    public void Input(long now, bool keyboard)
    {
        LastInput = now; if (keyboard) LastTyping = now; Nudged = false;
    }
    public void Reset(long now) { LastInput = now; LastTyping = long.MinValue / 2; Nudged = false; }
    public void Pause(long now, int minutes) { PauseUntil = now + minutes * 60000L; Reset(now); }
    public void Resume(long now) { PauseUntil = 0; Reset(now); }
    public bool IsPaused(long now) => now < PauseUntil;
    public bool IsTyping(long now) => now - LastTyping < 300;
    public bool TryNudge(long now, Preferences p)
    {
        if (IsPaused(now) || now - LastInput < p.IdleSeconds * 1000L) return false;
        if (Nudged && (!p.Repeat || now - LastNudge < p.RepeatSeconds * 1000L)) return false;
        Nudged = true; LastNudge = now; return true;
    }
}
public sealed class HandReaction
{
    bool left;
    public string Tap() { left = !left; return left ? "typing-left" : "typing-right"; }
}
public sealed class ReactionLimiter
{
    long last = long.MinValue / 2;
    public bool Accept(long now, int milliseconds)
    {
        if (now - last < Math.Max(0, milliseconds)) return false; last = now; return true;
    }
    public void Reset() => last = long.MinValue / 2;
}
