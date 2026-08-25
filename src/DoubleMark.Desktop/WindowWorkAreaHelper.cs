using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DoubleMark.Desktop;

internal static class WindowWorkAreaHelper
{
    private const int WmGetMinMaxInfo = 0x0024;

    public static void EnableWorkAreaMaximize(Window window)
    {
        if (window.IsLoaded)
            Hook(window);
        else
            window.SourceInitialized += (_, _) => Hook(window);
    }

    public static System.Windows.Rect GetWorkAreaDip(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var fallback = SystemParameters.WorkArea;
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero && window.Owner != null)
                hwnd = new WindowInteropHelper(window.Owner).Handle;
            if (hwnd == IntPtr.Zero)
                return fallback;

            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
                return fallback;

            var source = PresentationSource.FromVisual(window)
                ?? (window.Owner == null ? null : PresentationSource.FromVisual(window.Owner));
            var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var work = monitorInfo.rcWork;
            var topLeft = fromDevice.Transform(new System.Windows.Point(work.Left, work.Top));
            var bottomRight = fromDevice.Transform(new System.Windows.Point(work.Right, work.Bottom));
            return new System.Windows.Rect(topLeft, bottomRight);
        }
        catch
        {
            return fallback;
        }
    }

    private static void Hook(Window window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source)
            return;

        source.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
            return IntPtr.Zero;

        ApplyWorkAreaMaximize(hwnd, lParam);
        handled = true;
        return IntPtr.Zero;
    }

    private static void ApplyWorkAreaMaximize(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
            return;

        var work = monitorInfo.rcWork;
        var monitorRect = monitorInfo.rcMonitor;

        mmi.ptMaxPosition.X = work.Left - monitorRect.Left;
        mmi.ptMaxPosition.Y = work.Top - monitorRect.Top;
        mmi.ptMaxSize.X = work.Right - work.Left;
        mmi.ptMaxSize.Y = work.Bottom - work.Top;

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint ptReserved;
        public NativePoint ptMaxSize;
        public NativePoint ptMaxPosition;
        public NativePoint ptMinTrackSize;
        public NativePoint ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
