using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services;

/// <summary>
/// Binds the HID scanner device path after a fast wedge scan (same idea as the setup wizard).
/// </summary>
public static class HidDeviceAutoBinder
{
    public static bool TryBindFromLastScan(AppSettings settings, out string? message) =>
        TryBind(settings, RawInputScannerService.LastScanEvent, out message);

    public static bool TryBind(AppSettings settings, RawInputScanEventArgs? scan, out string? message)
    {
        message = null;
        if (!settings.ScannerAutoBindHid)
            return false;

        if (settings.ScannerMode is not (ScannerMode.Hid or ScannerMode.Auto))
            return false;

        if (scan == null)
            return false;

        if (!scan.IsFastScan || string.IsNullOrWhiteSpace(scan.DevicePath))
            return false;

        var path = scan.DevicePath!;
        if (HidDeviceInfo.MatchesConfiguredDevice(path, settings.EffectiveHidDevicePath))
            return false;

        settings.SelectedHidDeviceId = path;
        settings.ScannerDevicePath = path;
        settings.NormalizeScannerFields();
        settings.Save();
        ScannerSourceFactory.HidListenAllUntilBound = false;

        var info = HidDeviceInfo.FromPath(path);
        message = $"HID-сканер определён автоматически: {info.DisplayName} (VID={info.VendorId} PID={info.ProductId})";
        LoggingService.Info("Scanner.HID", "Auto-bound device path = " + path);
        return true;
    }
}
