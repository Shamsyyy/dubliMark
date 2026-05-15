using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace DubliMark.Desktop.Services;

public class RawInputScannerService : IScannerSource
{
    public event EventHandler<string>? BarcodeReceived;

    private HwndSource? _source;
    private readonly StringBuilder _buffer = new();
    private DateTime _lastKeyTime = DateTime.MinValue;
    private string? _scannerDevicePath;

    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_TAB = 0x09;

    // EN-US keyboard layout handle for device-independent decoding
    private static readonly IntPtr _enLayout =
        RawInputInterop.LoadKeyboardLayout("00000409", 0);

    public void Start(Window window, string? scannerDevicePath = null)
    {
        _scannerDevicePath = scannerDevicePath;

        var helper = new WindowInteropHelper(window);
        _source = HwndSource.FromHwnd(helper.Handle);
        if (_source == null)
            throw new InvalidOperationException("Window not initialized");

        _source.AddHook(WndProc);

        var rid = new RawInputInterop.RAWINPUTDEVICE
        {
            UsagePage = 0x01,
            Usage = 0x06,
            Flags = RawInputInterop.RIDEV_INPUTSINK,
            Target = helper.Handle
        };

        if (!RawInputInterop.RegisterRawInputDevices(
            new[] { rid }, 1,
            (uint)Marshal.SizeOf<RawInputInterop.RAWINPUTDEVICE>()))
        {
            throw new InvalidOperationException(
                "Failed to register Raw Input: " + Marshal.GetLastWin32Error());
        }
    }

    // IScannerSource.Start() — no-op without window, kept for interface compat
    public void Start() { }

    public void Stop()
    {
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != RawInputInterop.WM_INPUT)
            return IntPtr.Zero;

        uint dwSize = 0;
        RawInputInterop.GetRawInputData(lParam, RawInputInterop.RID_INPUT,
            IntPtr.Zero, ref dwSize,
            (uint)Marshal.SizeOf<RawInputInterop.RAWINPUTHEADER>());

        IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
        try
        {
            if (RawInputInterop.GetRawInputData(lParam, RawInputInterop.RID_INPUT,
                buffer, ref dwSize,
                (uint)Marshal.SizeOf<RawInputInterop.RAWINPUTHEADER>()) != dwSize)
                return IntPtr.Zero;

            var raw = Marshal.PtrToStructure<RawInputInterop.RAWINPUT>(buffer);

            if (raw.Header.Type != RawInputInterop.RIM_TYPEKEYBOARD)
                return IntPtr.Zero;

            if ((raw.Keyboard.Flags & 0x01) != 0) // RI_KEY_BREAK (key up)
                return IntPtr.Zero;

            if (_scannerDevicePath != null)
            {
                var path = GetDevicePath(raw.Header.Device);
                if (path == null || !path.Equals(_scannerDevicePath,
                    StringComparison.OrdinalIgnoreCase))
                    return IntPtr.Zero;
            }

            HandleKey(raw.Keyboard);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return IntPtr.Zero;
    }

    private void HandleKey(RawInputInterop.RAWKEYBOARD kb)
    {
        var now = DateTime.UtcNow;
        var gap = (now - _lastKeyTime).TotalMilliseconds;
        _lastKeyTime = now;

        if (kb.VKey == VK_RETURN || kb.VKey == VK_TAB)
        {
            if (_buffer.Length > 0)
            {
                var data = _buffer.ToString();
                _buffer.Clear();
                BarcodeReceived?.Invoke(this, data);
            }
            return;
        }

        if (_buffer.Length > 0 && gap > 300)
            _buffer.Clear();

        var ch = DecodeChar(kb.VKey, kb.MakeCode);
        if (ch.HasValue)
            _buffer.Append(ch.Value);
    }

    private static char? DecodeChar(ushort vKey, ushort scanCode)
    {
        byte[] keyState = new byte[256];
        RawInputInterop.GetKeyboardState(keyState);

        // Force NumLock on and CapsLock off for consistent decoding
        keyState[0x14] = 0;       // CapsLock off
        keyState[0x90] = 1;       // NumLock on

        var sb = new StringBuilder(8);
        // Use EN-US layout to avoid Cyrillic interference
        int result = RawInputInterop.ToUnicodeEx(vKey, scanCode, keyState, sb, sb.Capacity, 0, _enLayout);

        if (result > 0 && sb.Length > 0)
            return sb[0];

        // Ctrl+] → GS (0x1D)
        if (vKey == 0xDD && (keyState[0x11] & 0x80) != 0)
            return (char)0x1D;

        return null;
    }

    private static string? GetDevicePath(IntPtr device)
    {
        uint size = 0;
        RawInputInterop.GetRawInputDeviceInfo(device,
            RawInputInterop.RIDI_DEVICENAME, IntPtr.Zero, ref size);

        if (size == 0) return null;

        IntPtr buf = Marshal.AllocHGlobal((int)size * 2);
        try
        {
            if (RawInputInterop.GetRawInputDeviceInfo(device,
                RawInputInterop.RIDI_DEVICENAME, buf, ref size) > 0)
                return Marshal.PtrToStringUni(buf);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return null;
    }
}
