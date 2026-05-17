using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DoubleMark.Desktop.Services;

internal static class RawInputInterop
{
    public const int WM_INPUT = 0x00FF;
    public const int RID_INPUT = 0x10000003;
    public const int RIDI_DEVICENAME = 0x20000007;
    public const int RIM_TYPEKEYBOARD = 1;
    public const int RIDEV_INPUTSINK = 0x00000100;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUT
    {
        public RAWINPUTHEADER Header;
        public RAWKEYBOARD Keyboard;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    public static extern uint GetRawInputData(
        IntPtr hRawInput, uint command, IntPtr data,
        ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint GetRawInputDeviceInfo(
        IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("user32.dll")]
    public static extern int ToUnicode(
        uint virtualKey, uint scanCode, byte[] keyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer,
        int bufferSize, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetKeyboardState(byte[] keyState);

    [DllImport("user32.dll", EntryPoint = "LoadKeyboardLayoutW", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

    [DllImport("user32.dll")]
    public static extern int ToUnicodeEx(
        uint virtualKey, uint scanCode, byte[] keyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer,
        int bufferSize, uint flags, IntPtr dwhkl);
}
