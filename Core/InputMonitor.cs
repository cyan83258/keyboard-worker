using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.IO;
using System.Text;

namespace KeyboardWorker;

public sealed class InputMonitor : IDisposable
{
    readonly HwndSource source;
    readonly Action<bool> received;
    readonly KeyPressGate held = new();
    IntPtr buffer = Marshal.AllocHGlobal(1024);
    bool mouse;
    public InputMonitor(HwndSource source, Action<bool> received, bool mouse = false)
    {
        this.source = source; this.received = received;
        source.AddHook(Hook);
        var devices = new[] { new Device { Page = 1, Usage = 6, Flags = 0x100, Target = source.Handle } };
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<Device>()))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        Watch(mouse);
    }
    // A mouse reports hundreds to thousands of moves per second, system-wide. Listen only when mouse activity is actually used.
    public bool WatchesMouse => mouse;
    public void Watch(bool wantMouse)
    {
        if (wantMouse == mouse || buffer == IntPtr.Zero) return;
        var device = wantMouse ? new Device { Page = 1, Usage = 2, Flags = 0x100, Target = source.Handle } : new Device { Page = 1, Usage = 2, Flags = 1 };
        if (RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<Device>())) mouse = wantMouse;
    }
    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x00FF)
        {
            uint length = 1024;
            var headerSize = (uint)Marshal.SizeOf<Header>();
            if (GetRawInputData(lp, 0x10000003, buffer, ref length, headerSize) != uint.MaxValue)
            {
                var header = Marshal.PtrToStructure<Header>(buffer);
                if (header.Type == 1)
                {
                    var key = Marshal.PtrToStructure<Keyboard>(IntPtr.Add(buffer, (int)headerSize));
                    if (held.Process(key.MakeCode, key.VirtualKey, key.Flags)) received(true);
                }
                else if (header.Type == 0) received(false);
            }
        }
        if (msg == 0x0219) held.Clear();
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        var devices = new[] { new Device { Page = 1, Usage = 6, Flags = 1 }, new Device { Page = 1, Usage = 2, Flags = 1 } };
        RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf<Device>());
        source.RemoveHook(Hook);
        if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; }
    }
    public void Reset() => held.Clear();
    [StructLayout(LayoutKind.Sequential)] struct Device { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)] struct Header { public uint Type, Size; public IntPtr Device, WParam; }
    [StructLayout(LayoutKind.Sequential)] struct Keyboard { public ushort MakeCode, Flags, Reserved, VirtualKey; public uint Message, Extra; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr raw, uint command, IntPtr data, ref uint size, uint header);
}
public sealed class KeyPressGate
{
    readonly HashSet<uint> down = new();
    public bool Process(ushort scan, ushort key, ushort flags)
    {
        if (key == 255) return false;
        uint id = (uint)(scan == 0 ? key + 0x10000 : scan) | ((uint)(flags & 6) << 20);
        if ((flags & 1) != 0) { down.Remove(id); return false; }
        return down.Add(id);
    }
    public void Clear() => down.Clear();
}
