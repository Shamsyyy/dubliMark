using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services;

public sealed record ScannerSettingsApplyResult(
    bool Success,
    string UserMessage,
    bool OpenLogsHint = false);

public static class ScannerSettingsApplier
{
    public static ScannerSettingsApplyResult Apply(
        AppSettings current,
        ScannerMode selectedMode,
        string? hidDevicePath,
        string? rawInputDevicePath)
    {
        var previousMode = current.ScannerMode;
        LoggingService.Info("Scanner.Settings", "Apply started");
        LoggingService.Info("Scanner.Settings", $"Selected mode = {selectedMode}");
        LoggingService.Info("Scanner.Settings", $"Previous mode = {previousMode}");
        LoggingService.Info("Scanner.Settings",
            $"COM preserved = {(!string.IsNullOrWhiteSpace(current.ComPort))} port={current.ComPort ?? "—"}");

        try
        {
            return selectedMode switch
            {
                ScannerMode.Auto => ApplyAuto(current, hidDevicePath),
                ScannerMode.Com => ApplyCom(current),
                ScannerMode.Hid => ApplyHid(current, hidDevicePath),
                ScannerMode.RawInput => ApplyRawInput(current, rawInputDevicePath),
                _ => new ScannerSettingsApplyResult(false, "Выберите режим: Авто, COM или HID.")
            };
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner.Settings", "Apply failed", ex);
            return new ScannerSettingsApplyResult(
                false,
                "Не удалось сохранить настройки сканера.",
                OpenLogsHint: true);
        }
    }

    private static ScannerSettingsApplyResult ApplyAuto(AppSettings current, string? hidDevicePath)
    {
        current.ScannerMode = ScannerMode.Auto;

        if (!string.IsNullOrWhiteSpace(hidDevicePath))
        {
            current.SelectedHidDeviceId = hidDevicePath;
            current.ScannerDevicePath = hidDevicePath;
        }

        var ports = SerialScannerService.GetAvailablePorts();
        if (string.IsNullOrWhiteSpace(current.ComPort) && ports.Length > 0)
            current.ComPort = ports[0];

        current.NormalizeScannerFields();
        current.Save();
        LoggingService.Info("Scanner.Settings", "Auto mode saved");

        return new ScannerSettingsApplyResult(
            true,
            "Режим «Авто» сохранён. Приложение слушает COM и HID; источник определяется по первому скану.");
    }

    private static ScannerSettingsApplyResult ApplyCom(AppSettings current)
    {
        current.ScannerMode = ScannerMode.Com;
        current.NormalizeScannerFields();
        current.Save();
        LoggingService.Info("Scanner.Settings", "COM mode saved; connect via dashboard panel");
        return new ScannerSettingsApplyResult(
            true,
            "Режим COM сохранён. Выберите порт на главной панели и нажмите «Подключить».");
    }

    private static ScannerSettingsApplyResult ApplyHid(AppSettings current, string? hidDevicePath)
    {
        LoggingService.Info("Scanner.HID", "Init started");
        var devices = HidDeviceDiscoveryService.EnumerateKeyboardDevices();

        if (devices.Count == 0)
        {
            LoggingService.Warn("Scanner.HID", "No devices for HID mode");
            return new ScannerSettingsApplyResult(
                false,
                "HID-сканер не найден. Подключите сканер или используйте RawInput/COM.",
                OpenLogsHint: true);
        }

        var path = hidDevicePath;
        if (string.IsNullOrWhiteSpace(path))
            path = devices[0].DevicePath;
        else if (!devices.Any(d => d.DevicePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            LoggingService.Warn("Scanner.HID", "Selected device missing: " + path);
            current.SelectedHidDeviceId = null;
            current.ScannerDevicePath = null;
            return new ScannerSettingsApplyResult(
                false,
                "Выбранное HID-устройство не найдено. Нажмите «Обновить HID» и выберите устройство снова.",
                OpenLogsHint: true);
        }

        current.ScannerMode = ScannerMode.Hid;
        current.SelectedHidDeviceId = path;
        current.ScannerDevicePath = path;
        current.NormalizeScannerFields();
        current.Save();

        LoggingService.Info("Scanner.HID", "Selected device = " + path);

        var comHint = BuildSerialHintForHidDevice(path);
        return new ScannerSettingsApplyResult(
            true,
            "HID режим сохранён. Подключение выполняется…" + comHint);
    }

    private static ScannerSettingsApplyResult ApplyRawInput(AppSettings current, string? rawInputDevicePath)
    {
        LoggingService.Info("Scanner.RawInput", "Init started");
        var devices = HidDeviceDiscoveryService.EnumerateKeyboardDevices();
        LoggingService.Info("Scanner.RawInput", $"Devices found count = {devices.Count}");

        if (!string.IsNullOrWhiteSpace(rawInputDevicePath)
            && !devices.Any(d => d.DevicePath.Equals(rawInputDevicePath, StringComparison.OrdinalIgnoreCase)))
        {
            LoggingService.Warn("Scanner.RawInput", "Selected device missing: " + rawInputDevicePath);
            current.SelectedRawInputDeviceId = null;
            return new ScannerSettingsApplyResult(
                false,
                "Выбранное RawInput-устройство не найдено. Обновите список или оставьте «Все клавиатуры».",
                OpenLogsHint: true);
        }

        current.ScannerMode = ScannerMode.RawInput;
        current.SelectedRawInputDeviceId = string.IsNullOrWhiteSpace(rawInputDevicePath) ? null : rawInputDevicePath;
        current.NormalizeScannerFields();
        current.Save();

        LoggingService.Info("Scanner.RawInput",
            "Selected device = " + (current.SelectedRawInputDeviceId ?? "(all keyboards)"));

        return new ScannerSettingsApplyResult(true, "RawInput режим сохранён. Подключение выполняется…");
    }

    private static string BuildSerialHintForHidDevice(string hidPath)
    {
        var info = HidDeviceInfo.FromPath(hidPath);
        if (info.VendorId is not ("1A86" or "—"))
            return "";

        var ports = SerialScannerService.GetAvailablePorts();
        if (ports.Length == 0)
            return "";

        return " Если скан не приходит, переключите сканер в режим COM и подключите порт "
               + string.Join(" или ", ports) + ".";
    }
}
