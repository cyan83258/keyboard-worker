using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyboardWorker;

// Per-application volume from the Windows volume mixer (Core Audio sessions on the default output device).
public static class AppVolume
{
    public static int? Get(Func<string, bool> process)
    {
        float? playing = null, any = null;
        Visit(process, (volume, active) => { if (volume.GetMasterVolume(out float level) != 0) return; any ??= level; if (active) playing ??= level; });
        float? found = playing ?? any; return found == null ? null : (int)Math.Round(found.Value * 100);
    }
    public static bool Set(Func<string, bool> process, int percent)
    {
        bool changed = false; var context = Guid.Empty; float level = Math.Clamp(percent, 0, 100) / 100f;
        Visit(process, (volume, _) => { if (volume.SetMasterVolume(level, ref context) == 0) changed = true; });
        return changed;
    }
    static void Visit(Func<string, bool> process, Action<ISimpleAudioVolume, bool> action)
    {
        try
        {
            var devices = (IMMDeviceEnumerator)(object)new MMDeviceEnumerator();
            if (devices.GetDefaultAudioEndpoint(0, 1, out var device) != 0) return;
            var iid = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref iid, 23, IntPtr.Zero, out object activated) != 0) return;
            if (((IAudioSessionManager2)activated).GetSessionEnumerator(out var sessions) != 0 || sessions.GetCount(out int count) != 0) return;
            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out object session) != 0) continue;
                if (session is not IAudioSessionControl2 control || control.GetProcessId(out uint id) < 0 || id == 0) continue;
                string name; try { using var owner = Process.GetProcessById((int)id); name = owner.ProcessName; } catch { continue; }
                if (!process(name) || session is not ISimpleAudioVolume volume) continue;
                control.GetState(out int state); action(volume, state == 1);
            }
        }
        catch (COMException) { }
        catch (InvalidCastException) { }
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] sealed class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr session, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr session, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }
    // Method order is the COM vtable: the nine IAudioSessionControl slots come before the IAudioSessionControl2 ones.
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(IntPtr group, IntPtr context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint process);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
    }
}
