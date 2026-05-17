using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services;

public sealed class RawInputScanEventArgs : EventArgs
{
    public string Barcode { get; init; } = "";
    public string? DevicePath { get; init; }
    public double AverageIntervalMs { get; init; }
    public bool IsFastScan { get; init; }
    public RawInputScanDiagnostics? Diagnostics { get; init; }
}

public class RawInputScannerService : IScannerSource
{
    public event EventHandler<string>? BarcodeReceived;
    public event EventHandler<RawInputScanEventArgs>? ScanCompleted;
    public event EventHandler<RawInputKeyEvent>? KeyCaptured;

    /// <summary>Last completed HID scan — for diagnostics UI.</summary>
    public static RawInputScanDiagnostics? LastScanDiagnostics { get; private set; }

    private HwndSource? _source;
    private LowLevelKeyboardHook? _lowLevelHook;
    private readonly StringBuilder _buffer = new();
    private readonly List<RawInputKeyEvent> _scanKeys = new();
    private DateTime _lastKeyTime = DateTime.MinValue;
    private readonly List<double> _intervals = new();
    private string? _currentDevicePath;
    private string? _scannerDevicePath;
    private bool _wizardMode;
    private bool _requirePathFilter;
    private int _gsRestoredCount;
    private bool _captureSession;

    private bool _ctrlPressed;
    private bool _shiftPressed;
    private bool _altPressed;

    private ScannerGsSettings _gsSettings = new();

    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_GS = 0x1D;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;
    private const ushort VK_OEM_6 = 0xDD;
    private const ushort VK_OEM_5 = 0xDC;
    private const ushort ScanCodeBracketRight = 0x35;
    private const ushort ScanCodeBackslash = 0x2B;
    private const ushort RI_KEY_BREAK = 0x01;
    private const ushort RI_KEY_E0 = 0x02;
    private const double FastScanThresholdMs = 50;
    private const double ResetIntervalMs = 50;

    private static readonly bool DebugKeys =
        string.Equals(Environment.GetEnvironmentVariable("DUBLIMARK_DEBUG_SCANNER"), "1",
            StringComparison.Ordinal);

    private static readonly bool UseLowLevelHook =
        string.Equals(Environment.GetEnvironmentVariable("DUBLIMARK_LOWLEVEL_HOOK"), "1",
            StringComparison.Ordinal);

    private static readonly IntPtr _enLayout =
        RawInputInterop.LoadKeyboardLayout("00000409", 0);

    private (ushort vKey, ushort makeCode, long ticks) _lastRawDedupe;

    public void ConfigureGsMapping(ScannerGsSettings settings) => _gsSettings = settings;

    public void Attach(Window window, string? scannerDevicePath, bool wizardMode,
        ScannerGsSettings? gsSettings = null)
    {
        _wizardMode = wizardMode;
        _scannerDevicePath = scannerDevicePath;
        _requirePathFilter = !wizardMode && !string.IsNullOrWhiteSpace(scannerDevicePath);
        if (gsSettings != null)
            _gsSettings = gsSettings;

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

        if (UseLowLevelHook)
        {
            _lowLevelHook = new LowLevelKeyboardHook(OnLowLevelKeyDown);
            _lowLevelHook.Install();
        }
    }

    public void Stop()
    {
        _lowLevelHook?.Dispose();
        _lowLevelHook = null;
        _source?.RemoveHook(WndProc);
        _source = null;
        ResetBuffer();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != RawInputInterop.WM_INPUT)
            return IntPtr.Zero;

        uint dwSize = 0;
        RawInputInterop.GetRawInputData(lParam, RawInputInterop.RID_INPUT,
            IntPtr.Zero, ref dwSize,
            (uint)Marshal.SizeOf<RawInputInterop.RAWINPUTHEADER>());

        var buffer = Marshal.AllocHGlobal((int)dwSize);
        try
        {
            if (RawInputInterop.GetRawInputData(lParam, RawInputInterop.RID_INPUT,
                    buffer, ref dwSize,
                    (uint)Marshal.SizeOf<RawInputInterop.RAWINPUTHEADER>()) != dwSize)
                return IntPtr.Zero;

            var raw = Marshal.PtrToStructure<RawInputInterop.RAWINPUT>(buffer);

            if (raw.Header.Type != RawInputInterop.RIM_TYPEKEYBOARD)
                return IntPtr.Zero;

            var path = GetDevicePath(raw.Header.Device);

            if (_requirePathFilter)
            {
                if (path == null || !path.Equals(_scannerDevicePath, StringComparison.OrdinalIgnoreCase))
                    return IntPtr.Zero;
            }

            if (HandleKey(raw.Keyboard, path, "raw"))
                handled = true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return IntPtr.Zero;
    }

    private void OnLowLevelKeyDown(ushort vKey, ushort makeCode, uint flags, bool isE0)
    {
        if (WasRecentlyHandledByRaw(vKey, makeCode))
            return;

        if (!IsModifierKey(vKey))
            TryAppendGsFromChord(vKey, makeCode, isE0, source: "hook");
    }

    private bool WasRecentlyHandledByRaw(ushort vKey, ushort makeCode)
    {
        var now = Environment.TickCount64;
        if (_lastRawDedupe.vKey == vKey && _lastRawDedupe.makeCode == makeCode &&
            now - _lastRawDedupe.ticks < 15)
            return true;
        return false;
    }

    private bool HandleKey(RawInputInterop.RAWKEYBOARD kb, string? devicePath, string source)
    {
        var isBreak = (kb.Flags & RI_KEY_BREAK) != 0;
        var isE0 = (kb.Flags & RI_KEY_E0) != 0;

        if (!isBreak)
        {
            _lastRawDedupe = (kb.VKey, kb.MakeCode, Environment.TickCount64);
            if (!_captureSession)
                _captureSession = true;
        }

        SyncModifiersFromKeyboardState();

        if (IsModifierKey(kb.VKey))
        {
            UpdateModifierState(kb.VKey, pressed: !isBreak);
            RecordKey(kb, isE0, isBreak, null, RawInputKeyAction.Modifier, source);
            return (_wizardMode || _requirePathFilter) && !isBreak;
        }

        if (isBreak)
        {
            RecordKey(kb, isE0, true, null, RawInputKeyAction.IgnoredBreak, source);
            return false;
        }

        if (DebugKeys)
        {
            Debug.WriteLine(
                $"[RawInput] {source} VKey=0x{kb.VKey:X2} Make=0x{kb.MakeCode:X2} Flags=0x{kb.Flags:X2} " +
                $"E0={isE0} Ctrl={_ctrlPressed} Shift={_shiftPressed} Alt={_altPressed}");
        }

        var now = DateTime.UtcNow;
        var gap = _lastKeyTime == DateTime.MinValue ? 0 : (now - _lastKeyTime).TotalMilliseconds;
        _lastKeyTime = now;

        if (kb.VKey == VK_RETURN || kb.VKey == VK_TAB)
        {
            RecordKey(kb, isE0, false, null, RawInputKeyAction.Terminator, source);
            CompleteBarcode(devicePath);
            return true;
        }

        if (_buffer.Length > 0 && gap > ResetIntervalMs)
            ResetBuffer();

        if (_buffer.Length == 0)
        {
            _currentDevicePath = devicePath;
            _intervals.Clear();
            _scanKeys.Clear();
            _gsRestoredCount = 0;
            _captureSession = true;
        }
        else if (gap > 0)
        {
            _intervals.Add(gap);
        }

        if (TryDecodeGs(kb, isE0, out var gsNote))
        {
            _buffer.Append((char)0x1D);
            _gsRestoredCount++;
            RecordKey(kb, isE0, false, (char)0x1D, RawInputKeyAction.GsRestored, source, gsNote);
            return true;
        }

        var ch = DecodeChar(kb.VKey, kb.MakeCode, isE0);
        if (!ch.HasValue)
            ch = UsScanCodeDecoder.TryDecode(kb.MakeCode, _shiftPressed);

        if (ch.HasValue)
        {
            if (IsShiftJGsCandidate(kb, ch.Value))
            {
                RecordKey(kb, isE0, false, ch, RawInputKeyAction.Char, source,
                    "warning: Shift+J is NOT auto-mapped to GS");
            }
            else
            {
                _buffer.Append(ch.Value);
                RecordKey(kb, isE0, false, ch, RawInputKeyAction.Char, source);
            }

            return true;
        }

        RecordKey(kb, isE0, false, null, RawInputKeyAction.Char, source, "undecoded");
        return _wizardMode || _requirePathFilter;
    }

    private static bool IsShiftJGsCandidate(RawInputInterop.RAWKEYBOARD kb, char ch) =>
        ch is 'J' or 'j' && kb.MakeCode == 0x24;

    private bool TryDecodeGs(RawInputInterop.RAWKEYBOARD kb, bool isE0, out string note)
    {
        note = "";
        if (_gsSettings.Mode == ScannerGsMappingMode.None)
            return false;

        if (TryAppendGsFromChord(kb.VKey, kb.MakeCode, isE0, "raw", out note))
            return true;

        var decoded = DecodeChar(kb.VKey, kb.MakeCode, isE0);
        if (decoded == (char)0x1D)
        {
            note = "literal GS from ToUnicodeEx";
            return true;
        }

        if (_gsSettings.VisibleGsChar.HasValue && decoded == _gsSettings.VisibleGsChar.Value)
        {
            note = $"visible GS char '{_gsSettings.VisibleGsChar}'";
            return true;
        }

        return false;
    }

    private bool TryAppendGsFromChord(ushort vKey, ushort makeCode, bool isE0, string source,
        out string note)
    {
        note = "";
        if (_gsSettings.Mode == ScannerGsMappingMode.None)
            return false;

        if (MatchesCustomGsChord(vKey, makeCode))
        {
            note = "custom GS chord";
            return true;
        }

        if (vKey == VK_GS || makeCode == VK_GS)
        {
            note = "direct VK/Make 0x1D";
            return true;
        }

        var ctrl = _ctrlPressed || IsModifierDown(0x11, 0xA2, 0xA3);
        var alt = _altPressed || IsModifierDown(0x12, 0xA4, 0xA5);

        if (ctrl && (makeCode == ScanCodeBracketRight || vKey == VK_OEM_6))
        {
            note = "GS via Ctrl+]";
            return true;
        }

        if (alt && (makeCode == ScanCodeBracketRight || vKey == VK_OEM_6))
        {
            note = "GS via Alt+]";
            return true;
        }

        if (ctrl && (makeCode == ScanCodeBackslash || vKey == VK_OEM_5))
        {
            note = "GS via Ctrl+\\";
            return true;
        }

        return false;
    }

    private bool TryAppendGsFromChord(ushort vKey, ushort makeCode, bool isE0, string source)
    {
        if (!TryAppendGsFromChord(vKey, makeCode, isE0, source, out var note))
            return false;

        if (_buffer.Length == 0)
            return false;

        if (WasRecentlyHandledByRaw(vKey, makeCode))
            return false;

        _buffer.Append((char)0x1D);
        _gsRestoredCount++;
        return true;
    }

    private bool MatchesCustomGsChord(ushort vKey, ushort makeCode)
    {
        if (_gsSettings.CustomGsVKey == null && _gsSettings.CustomGsMakeCode == null)
            return false;

        if (_gsSettings.CustomGsVKey.HasValue && vKey != _gsSettings.CustomGsVKey.Value)
            return false;
        if (_gsSettings.CustomGsMakeCode.HasValue && makeCode != _gsSettings.CustomGsMakeCode.Value)
            return false;

        if (_gsSettings.CustomGsRequiresCtrl && !(_ctrlPressed || IsModifierDown(0x11, 0xA2, 0xA3)))
            return false;
        if (_gsSettings.CustomGsRequiresShift && !(_shiftPressed || IsModifierDown(0x10, 0xA0, 0xA1)))
            return false;
        if (_gsSettings.CustomGsRequiresAlt && !(_altPressed || IsModifierDown(0x12, 0xA4, 0xA5)))
            return false;

        return true;
    }

    private void CompleteBarcode(string? devicePath)
    {
        if (_buffer.Length == 0)
            return;

        var data = _buffer.ToString();
        var avgInterval = _intervals.Count > 0 ? _intervals.Average() : 0;
        var isFast = _intervals.Count > 0 && avgInterval < FastScanThresholdMs;

        var diagnostics = new RawInputScanDiagnostics
        {
            CompletedUtc = DateTime.UtcNow,
            Barcode = data,
            GsRestoredCount = _gsRestoredCount,
            Keys = _scanKeys.ToList()
        };
        LastScanDiagnostics = diagnostics;

        ResetBuffer();

        var args = new RawInputScanEventArgs
        {
            Barcode = data,
            DevicePath = _currentDevicePath ?? devicePath,
            AverageIntervalMs = avgInterval,
            IsFastScan = isFast,
            Diagnostics = diagnostics
        };

        ScanCompleted?.Invoke(this, args);
        BarcodeReceived?.Invoke(this, data);
    }

    private void RecordKey(RawInputInterop.RAWKEYBOARD kb, bool isE0, bool isBreak, char? ch,
        RawInputKeyAction action, string source, string? note = null)
    {
        var ev = new RawInputKeyEvent
        {
            UtcTime = DateTime.UtcNow,
            VKey = kb.VKey,
            MakeCode = kb.MakeCode,
            Flags = kb.Flags,
            IsE0 = isE0,
            IsBreak = isBreak,
            Ctrl = _ctrlPressed,
            Shift = _shiftPressed,
            Alt = _altPressed,
            DecodedChar = ch,
            Action = action,
            Note = note,
            Source = source
        };

        if (_captureSession)
            _scanKeys.Add(ev);

        KeyCaptured?.Invoke(this, ev);

        if (DebugKeys)
            Debug.WriteLine("[RawInput] " + ev.ToLogLine());
    }

    private static bool IsModifierKey(ushort vKey) =>
        vKey is VK_SHIFT or VK_LSHIFT or VK_RSHIFT
            or VK_CONTROL or VK_LCONTROL or VK_RCONTROL
            or VK_MENU or VK_LMENU or VK_RMENU;

    private void UpdateModifierState(ushort vKey, bool pressed)
    {
        switch (vKey)
        {
            case VK_LCONTROL:
            case VK_CONTROL:
            case VK_RCONTROL:
                _ctrlPressed = pressed;
                break;
            case VK_LSHIFT:
            case VK_SHIFT:
            case VK_RSHIFT:
                _shiftPressed = pressed;
                break;
            case VK_LMENU:
            case VK_MENU:
            case VK_RMENU:
                _altPressed = pressed;
                break;
        }
    }

    private void SyncModifiersFromKeyboardState()
    {
        _ctrlPressed = IsModifierDown(0x11, 0xA2, 0xA3);
        _shiftPressed = IsModifierDown(0x10, 0xA0, 0xA1);
        _altPressed = IsModifierDown(0x12, 0xA4, 0xA5);
    }

    private static bool IsModifierDown(params int[] vkeys)
    {
        byte[] keyState = new byte[256];
        if (!RawInputInterop.GetKeyboardState(keyState))
            return false;

        foreach (var vk in vkeys)
        {
            if ((keyState[vk] & 0x80) != 0)
                return true;
        }

        return false;
    }

    private void ResetBuffer()
    {
        _buffer.Clear();
        _intervals.Clear();
        _scanKeys.Clear();
        _currentDevicePath = null;
        _lastKeyTime = DateTime.MinValue;
        _gsRestoredCount = 0;
        _captureSession = false;
    }

    private static char? DecodeChar(ushort vKey, ushort scanCode, bool isE0)
    {
        byte[] keyState = new byte[256];
        RawInputInterop.GetKeyboardState(keyState);

        keyState[0x11] = 0;
        keyState[0xA2] = 0;
        keyState[0xA3] = 0;
        keyState[0x14] = 0;
        keyState[0x90] = 1;

        var sc = scanCode;
        if (isE0)
            sc |= 0xE000;

        var sb = new StringBuilder(8);
        int result = RawInputInterop.ToUnicodeEx(vKey, sc, keyState, sb, sb.Capacity, 0, _enLayout);

        if (result == 1 && sb.Length > 0)
        {
            var ch = sb[0];
            if (ch == (char)0x1D)
                return ch;
            if (ch >= 32 || ch == '\t')
                return ch;
        }

        if (result > 1 && sb.Length > 0)
            return sb[0];

        return null;
    }

    internal static string? GetDevicePath(IntPtr device)
    {
        uint size = 0;
        RawInputInterop.GetRawInputDeviceInfo(device,
            RawInputInterop.RIDI_DEVICENAME, IntPtr.Zero, ref size);

        if (size == 0) return null;

        var buf = Marshal.AllocHGlobal((int)size * 2);
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
