using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DoubleMark.Desktop.Services;

/// <summary>
/// Optional WH_KEYBOARD_LL hook — supplements Raw Input when DUBLIMARK_LOWLEVEL_HOOK=1.
/// Captures GS chords that may not surface via WM_INPUT on some drivers.
/// </summary>
internal sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardHookProc _proc;
    private readonly Action<ushort, ushort, uint, bool> _onKeyDown;

    private delegate IntPtr LowLevelKeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public LowLevelKeyboardHook(Action<ushort, ushort, uint, bool> onKeyDown)
    {
        _onKeyDown = onKeyDown;
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero)
            return;

        using var cur = Process.GetCurrentProcess();
        var mod = cur.MainModule;
        if (mod == null)
            return;

        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var isE0 = (kb.flags & 0x01) != 0;
            _onKeyDown((ushort)kb.vkCode, (ushort)(kb.scanCode & 0xFF), kb.flags, isE0);
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardHookProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
