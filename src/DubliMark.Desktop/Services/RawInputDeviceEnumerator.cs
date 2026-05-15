using System.Runtime.InteropServices;

namespace DubliMark.Desktop.Services;

public static class RawInputDeviceEnumerator
{
    private const uint RIM_TYPEKEYBOARD = 1;
    private const uint RIDI_DEVICENAME = 0x20000007;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        [Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    public static List<(IntPtr Handle, string Path)> EnumerateKeyboards()
    {
        var result = new List<(IntPtr, string)>();
        uint deviceCount = 0;
        GetRawInputDeviceList(null, ref deviceCount, (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>());

        if (deviceCount == 0)
            return result;

        var devices = new RAWINPUTDEVICELIST[deviceCount];
        GetRawInputDeviceList(devices, ref deviceCount, (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>());

        foreach (var device in devices)
        {
            if (device.dwType != RIM_TYPEKEYBOARD)
                continue;

            var path = GetDevicePath(device.hDevice);
            if (path != null)
                result.Add((device.hDevice, path));
        }

        return result;
    }

    private static string? GetDevicePath(IntPtr device)
    {
        uint size = 0;
        RawInputInterop.GetRawInputDeviceInfo(device, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0)
            return null;

        var buf = Marshal.AllocHGlobal((int)size * 2);
        try
        {
            if (RawInputInterop.GetRawInputDeviceInfo(device, RIDI_DEVICENAME, buf, ref size) > 0)
                return Marshal.PtrToStringUni(buf);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }

        return null;
    }
}
