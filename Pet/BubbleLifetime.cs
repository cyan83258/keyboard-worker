using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Runtime.CompilerServices;

namespace KeyboardWorker;

public sealed class BubbleLifetime
{
    public long Deadline { get; private set; }
    public bool Streaming { get; set; }
    long? hoverSince;
    public void Start(long now, int seconds) { Streaming = false; Deadline = now + Math.Clamp(seconds, 1, 3600) * 1000L; if (hoverSince.HasValue) hoverSince = now; }
    public void Hover(bool hovered, long now)
    {
        if (hovered) { hoverSince ??= now; return; }
        if (hoverSince.HasValue) { Deadline += Math.Max(0, now - hoverSince.Value); hoverSince = null; }
    }
    public bool Expired(long now, bool autoHide) => autoHide && !Streaming && !hoverSince.HasValue && now >= Deadline;
}
