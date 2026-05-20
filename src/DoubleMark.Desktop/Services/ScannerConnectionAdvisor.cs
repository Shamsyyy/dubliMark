using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services;

/// <summary>
/// Prepares scanner settings before connect (does not override explicit manual HID/COM choice).
/// </summary>
public static class ScannerConnectionAdvisor
{
    public static string? Apply(AppSettings settings, bool persist = true)
    {
        var ports = SerialScannerService.GetAvailablePorts();
        string? message = null;

        if (settings.ScannerMode == ScannerMode.Auto)
        {
            if (string.IsNullOrWhiteSpace(settings.ComPort) && ports.Length > 0)
            {
                settings.ComPort = ports[0];
                message = $"Авто: слушаем COM ({settings.ComPort}) и HID.";
                LoggingService.Info("Scanner", message);
            }

            if (persist && message != null)
                settings.Save();

            return message;
        }

        var keyboards = HidDeviceDiscoveryService.EnumerateKeyboardDevices();
        var hidPath = settings.EffectiveHidDevicePath;
        var hidPresent = !string.IsNullOrWhiteSpace(hidPath)
                         && keyboards.Any(d => HidDeviceInfo.MatchesConfiguredDevice(d.DevicePath, hidPath));

        if (settings.ScannerMode == ScannerMode.Hid
            && !string.IsNullOrWhiteSpace(hidPath)
            && !hidPresent)
        {
            LoggingService.Warn("Scanner",
                "Configured HID device is not connected: " + hidPath);
            settings.SelectedHidDeviceId = null;
            settings.ScannerDevicePath = null;
            message =
                "HID-устройство не найдено. Выберите устройство в настройках или переключитесь на режим «Авто»/COM.";
            LoggingService.Info("Scanner", message);
        }
        else if (settings.ScannerMode == ScannerMode.Com
                 && string.IsNullOrWhiteSpace(settings.ComPort)
                 && ports.Length > 0)
        {
            settings.ComPort = ports[0];
            message = $"Выбран COM-порт {settings.ComPort}. Нажмите «Подключить».";
        }

        if (persist && message != null)
            settings.Save();

        return message;
    }

    public static bool IsConfiguredHidPresent(AppSettings settings)
    {
        var hidPath = settings.EffectiveHidDevicePath;
        if (string.IsNullOrWhiteSpace(hidPath))
            return false;

        return HidDeviceDiscoveryService.EnumerateKeyboardDevices()
            .Any(d => HidDeviceInfo.MatchesConfiguredDevice(d.DevicePath, hidPath));
    }
}
