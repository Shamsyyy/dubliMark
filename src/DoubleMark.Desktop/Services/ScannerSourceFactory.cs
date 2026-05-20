using System.IO;
using System.IO.Ports;
using System.Windows;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services;

public sealed record ScannerConnectResult(bool Success, string Message, bool IsConnected)
{
    public static ScannerConnectResult Ok(string message) => new(true, message, true);
    public static ScannerConnectResult Idle(string message) => new(true, message, false);
    public static ScannerConnectResult Fail(string message) => new(false, message, false);
}

public static class ScannerSourceFactory
{
    /// <summary>When true, HID listens on all keyboards until the first auto-bind this session.</summary>
    public static bool HidListenAllUntilBound { get; set; } = true;

    public static void ResetHidBindingSession() => HidListenAllUntilBound = true;

    public static IScannerSource Create(Window window, AppSettings settings)
    {
        var result = TryCreate(window, settings, out var source);
        if (!result.Success)
            LoggingService.Warn("Scanner", result.Message);

        return source ?? new NullScannerSource();
    }

    public static ScannerConnectResult TryCreate(Window window, AppSettings settings, out IScannerSource? source)
    {
        source = null;

        try
        {
            if (settings.ScannerMode == ScannerMode.Auto)
            {
                return TryCreateAuto(window, settings, out source);
            }

            if (settings.ScannerMode == ScannerMode.Com
                && !string.IsNullOrWhiteSpace(settings.ComPort))
            {
                return TryCreateCom(settings, out source);
            }

            if (settings.ScannerMode == ScannerMode.Hid)
            {
                return TryCreateRawInput(window, settings, out source, filterByDevice: true);
            }

            if (settings.ScannerMode == ScannerMode.RawInput)
            {
                return TryCreateRawInput(window, settings, out source, filterByDevice: false);
            }

            source = new NullScannerSource();
            return ScannerConnectResult.Idle("Сканер не настроен");
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner", "Create failed", ex);
            source = new NullScannerSource();
            return ScannerConnectResult.Fail("Не удалось подключить сканер: " + ex.Message);
        }
    }

    private static ScannerConnectResult TryCreateAuto(
        Window window,
        AppSettings settings,
        out IScannerSource? source)
    {
        source = null;
        LoggingService.Info("Scanner.Auto", "Init started");

        try
        {
            var ports = SerialScannerService.GetAvailablePorts();
            if (string.IsNullOrWhiteSpace(settings.ComPort) && ports.Length > 0)
                settings.ComPort = ports[0];

            var auto = new AutoScannerSource();
            auto.Start(window, settings);
            source = auto;

            var summary = auto.ActiveTransportSummary ?? "—";
            var hasCom = auto.ActiveTransportSummary?.Contains("COM", StringComparison.Ordinal) == true;
            var hasHid = auto.ActiveTransportSummary?.Contains("HID", StringComparison.Ordinal) == true;

            if (!hasCom && !hasHid)
            {
                return ScannerConnectResult.Fail(
                    "Режим «Авто»: нет COM-порта и не удалось включить HID. Проверьте USB-кабель.");
            }

            return ScannerConnectResult.Ok($"Авто: {summary}");
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner.Auto", "Init failed", ex);
            source = new NullScannerSource();
            return ScannerConnectResult.Fail("Не удалось запустить режим «Авто»: " + ex.Message);
        }
    }

    private static ScannerConnectResult TryCreateCom(AppSettings settings, out IScannerSource? source)
    {
        source = null;
        var port = settings.ComPort!.Trim();

        if (!PortExists(port))
        {
            return ScannerConnectResult.Fail(
                $"Порт {port} не найден. Нажмите «Обновить» и выберите доступный COM-порт.");
        }

        try
        {
            var baud = settings.ComBaudRate > 0 ? settings.ComBaudRate : 9600;
            var serial = new SerialScannerService(port, baud);
            serial.Start();
            source = serial;
            return ScannerConnectResult.Ok($"COM подключён: {port} ({baud})");
        }
        catch (UnauthorizedAccessException)
        {
            return ScannerConnectResult.Fail($"Порт {port} занят другим приложением.");
        }
        catch (IOException ex)
        {
            return ScannerConnectResult.Fail($"Не удалось открыть {port}: {ex.Message}");
        }
        catch (ArgumentException)
        {
            return ScannerConnectResult.Fail($"Порт {port} не существует.");
        }
    }

    private static ScannerConnectResult TryCreateRawInput(
        Window window,
        AppSettings settings,
        out IScannerSource? source,
        bool filterByDevice)
    {
        source = null;
        var category = filterByDevice ? "Scanner.HID" : "Scanner.RawInput";

        try
        {
            LoggingService.Info(category, "Init started");
            var (attachPath, listenAllKeyboards) = ResolveHidListen(settings, filterByDevice);

            if (filterByDevice && !listenAllKeyboards && string.IsNullOrWhiteSpace(attachPath))
            {
                return ScannerConnectResult.Fail(
                    "HID-сканер не найден. Подключите сканер или используйте RawInput/COM.");
            }
            if (!string.IsNullOrWhiteSpace(attachPath))
                LoggingService.Info(category, "Selected device = " + attachPath);
            else
                LoggingService.Info(category, "Listening on all keyboards (HID wedge)");

            var gs = ScannerGsSettings.FromAppSettings(settings);
            var raw = new RawInputScannerService();
            raw.AttachWhenReady(window, attachPath, wizardMode: listenAllKeyboards, gs);
            source = raw;
            LoggingService.Info(category, "Raw Input attached");
            return filterByDevice
                ? ScannerConnectResult.Ok(listenAllKeyboards
                    ? "HID: все клавиатуры (обновите устройство в настройках при необходимости)"
                    : "HID сканер подключен")
                : ScannerConnectResult.Ok(string.IsNullOrWhiteSpace(attachPath)
                    ? "RawInput включён"
                    : "RawInput подключен");
        }
        catch (Exception ex)
        {
            LoggingService.Error(category, "Attach failed", ex);
            var message = filterByDevice
                ? "Не удалось подключить HID. Подробности в логе."
                : "Не удалось включить RawInput. Подробности в логе.";
            return ScannerConnectResult.Fail(message);
        }
    }

    public static bool IsHidConfigured(AppSettings settings) =>
        settings.ScannerMode is ScannerMode.Hid or ScannerMode.Auto or ScannerMode.RawInput;

    /// <summary>
    /// When the saved HID path is missing or stale, listen on all keyboards so wedge input still arrives.
    /// </summary>
    public static (string? AttachPath, bool ListenAllKeyboards) ResolveHidListen(
        AppSettings settings,
        bool filterByDevice)
    {
        if (!filterByDevice)
            return (settings.SelectedRawInputDeviceId, string.IsNullOrWhiteSpace(settings.SelectedRawInputDeviceId));

        if (ShouldListenAllHidKeyboards(settings))
        {
            if (settings.ScannerMode == ScannerMode.Auto)
            {
                LoggingService.Info("Scanner.HID",
                    "Auto mode: listening on all keyboards until HID device is bound by scan.");
            }
            else
            {
                LoggingService.Info("Scanner.HID",
                    "Listening on all keyboards for auto-bind or missing saved device.");
            }

            return (null, true);
        }

        var hidPath = settings.EffectiveHidDevicePath;
        return (hidPath, false);
    }

    private static bool ShouldListenAllHidKeyboards(AppSettings settings)
    {
        if (settings.ScannerMode is not (ScannerMode.Auto or ScannerMode.Hid))
            return false;

        if (settings.ScannerAutoBindHid && HidListenAllUntilBound)
            return true;

        var hidPath = settings.EffectiveHidDevicePath;
        if (string.IsNullOrWhiteSpace(hidPath))
            return true;

        if (!ScannerConnectionAdvisor.IsConfiguredHidPresent(settings))
        {
            LoggingService.Warn("Scanner.HID",
                "Saved HID device is not connected; listening on all keyboards.");
            return true;
        }

        return false;
    }

    public static bool IsRawInputConfigured(AppSettings settings) =>
        settings.ScannerMode == ScannerMode.RawInput;

    public static bool PortExists(string port) =>
        GetAvailablePortsCached().Contains(port, StringComparer.OrdinalIgnoreCase);

    private static string[] GetAvailablePortsCached() => SerialScannerService.GetAvailablePorts();
}
