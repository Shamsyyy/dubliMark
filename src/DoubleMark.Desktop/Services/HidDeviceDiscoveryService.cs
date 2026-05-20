namespace DoubleMark.Desktop.Services;

public static class HidDeviceDiscoveryService
{
    public static IReadOnlyList<HidDeviceInfo> EnumerateKeyboardDevices()
    {
        LoggingService.Info("Scanner.HID", "Refresh devices started");
        try
        {
            var raw = RawInputDeviceEnumerator.EnumerateKeyboards();
            var devices = raw
                .Select(d => HidDeviceInfo.FromPath(d.Path))
                .DistinctBy(d => d.DevicePath, StringComparer.OrdinalIgnoreCase)
                .OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (devices.Count == 0)
            {
                LoggingService.Warn("Scanner.HID", "No devices found");
                return devices;
            }

            foreach (var device in devices)
                LoggingService.Info("Scanner.HID",
                    $"Found: VID={device.VendorId}, PID={device.ProductId}, Name={device.Name}, Path={device.DevicePath}");

            LoggingService.Info("Scanner.HID", $"Devices found count = {devices.Count}");
            return devices;
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner.HID", "Refresh failed", ex);
            return Array.Empty<HidDeviceInfo>();
        }
    }
}
