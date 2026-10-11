using System;

namespace KeyboardWorker;

public sealed class QuietMode
{
    bool started;
    long until;
    public bool Active(long now) => started && (until == 0 || now < until);
    public long Seconds(long now) => !Active(now) ? 0 : until == 0 ? -1 : Math.Max(0, (until - now + 999) / 1000);
    public void Start(long now, int minutes) { started = true; until = minutes <= 0 ? 0 : now + Math.Clamp(minutes, 1, 1440) * 60000L; }
    public void Stop() { started = false; until = 0; }
}
