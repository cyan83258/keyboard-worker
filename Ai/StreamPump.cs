using System;
using System.Threading;
using System.Windows.Threading;

namespace KeyboardWorker;

public sealed class StreamPump : IDisposable
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly Action<string> update;
    string? latest;
    public StreamPump(Action<string> update, bool enabled = true) { this.update = update; timer.Tick += (_, _) => Flush(); if (enabled) timer.Start(); }
    public void Report(string text) => Interlocked.Exchange(ref latest, text);
    public void Flush() { var text = Interlocked.Exchange(ref latest, null); if (text != null) update(PetMood.DisplayText(text)); }
    public void Dispose() { timer.Stop(); Interlocked.Exchange(ref latest, null); }
}
