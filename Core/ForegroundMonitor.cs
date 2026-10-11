using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace KeyboardWorker;

public sealed class ForegroundMonitor : IDisposable
{
    readonly Dispatcher dispatcher;
    readonly Action changed;
    readonly Callback callback;
    IntPtr hook;
    bool queued, disposed;
    public bool Registered => hook != IntPtr.Zero;
    public ForegroundMonitor(Dispatcher dispatcher, Action changed)
    {
        this.dispatcher = dispatcher; this.changed = changed; callback = Event;
        hook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 0);
    }
    void Event(IntPtr hook, uint type, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (disposed || queued || dispatcher.HasShutdownStarted) return;
        queued = true; dispatcher.BeginInvoke(() => { queued = false; if (!disposed) changed(); });
    }
    public void Dispose() { disposed = true; if (hook != IntPtr.Zero) { UnhookWinEvent(hook); hook = IntPtr.Zero; } GC.KeepAlive(callback); }
    delegate void Callback(IntPtr hook, uint type, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, Callback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
}
