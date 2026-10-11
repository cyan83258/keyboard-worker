using System;
using System.IO;
using System.Windows.Media;

namespace KeyboardWorker;

// Plays a pet's voice clip, or has Windows read a line aloud when there is no clip. One sound at a time: a new one replaces the old.
public sealed class VoicePlayer : IDisposable
{
    MediaPlayer? player; object? reader; bool disposed;
    public void Play(string path, int volume)
    {
        if (disposed || string.IsNullOrWhiteSpace(path) || SafeFiles.IsNetworkPath(path) || !File.Exists(path)) return;
        try
        {
            Silence(); player ??= new MediaPlayer();
            player.Open(new Uri(Path.GetFullPath(path))); player.Volume = Math.Clamp(volume, 0, 100) / 100.0; player.Play();
        }
        catch (Exception e) when (e is InvalidOperationException or UriFormatException or IOException) { ErrorLog.Write("음성 재생 실패", e.Message); }
    }
    // Windows' own text to speech (SAPI). Which voices exist depends on the language packs installed on this PC.
    public void Read(string text, int volume)
    {
        if (disposed || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            Silence();
            if (reader == null) { var type = Type.GetTypeFromProgID("SAPI.SpVoice"); if (type == null) return; reader = Activator.CreateInstance(type); }
            dynamic speaker = reader!; speaker.Volume = Math.Clamp(volume, 0, 100);
            speaker.Speak(SafeFiles.OneLine(text, 400), 1 | 2 | 8);   // asynchronous, replacing what is being read, plain text (never XML)
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or MissingMethodException) { reader = null; ErrorLog.Write("읽어주기 실패", e.Message); }
    }
    public void Silence()
    {
        try { player?.Stop(); if (reader != null) { dynamic speaker = reader; speaker.Speak("", 1 | 2); } }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { reader = null; }
    }
    public void Dispose()
    {
        if (disposed) return; Silence(); disposed = true; player?.Close(); player = null;
        if (reader != null) { try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(reader); } catch (ArgumentException) { } reader = null; }
    }
}
