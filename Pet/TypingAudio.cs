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

public sealed class TypingAudio : IDisposable
{
    readonly MediaPlayer[] players = { new(), new(), new() };
    readonly bool[] ready = new bool[3];
    string path = "";
    int cursor;
    long last = long.MinValue / 2;
    public string Error { get; private set; } = "";
    public TypingAudio()
    {
        for (int i = 0; i < players.Length; i++)
        {
            int slot = i;
            players[i].MediaOpened += (_, _) => { ready[slot] = true; };
            players[i].MediaEnded += (_, _) => { players[slot].Pause(); players[slot].Position = TimeSpan.Zero; };
            players[i].MediaFailed += (_, e) => { ready[slot] = false; Error = "소리를 재생할 수 없습니다: " + e.ErrorException.Message; };
        }
    }
    public void Configure(Preferences p)
    {
        foreach (var player in players) player.Volume = p.Volume / 100.0;
        if (path == p.SoundPath) return;
        path = p.SoundPath; Error = "";
        for (int i = 0; i < players.Length; i++)
        {
            players[i].Close(); ready[i] = false;
            if (File.Exists(path)) players[i].Open(new Uri(path));
        }
    }
    public bool Play(Preferences p, bool preview = false)
    {
        long now = Environment.TickCount64;
        if ((!p.Sound && !preview) || now - last < 60) return false;
        for (int n = 0; n < 3; n++)
        {
            int slot = cursor++ % 3;
            if (!ready[slot]) continue;
            last = now; players[slot].Position = TimeSpan.Zero; players[slot].Play(); return true;
        }
        return false;
    }
    public void Stop() { foreach (var player in players) player.Stop(); }
    public void Dispose() { foreach (var player in players) player.Close(); }
}
