using System.Windows;
using DubliMark.Desktop.Settings;

namespace DubliMark.Desktop.Services;

public static class ScannerSourceFactory
{
    public static IScannerSource Create(Window window, AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ComPort) && PortExists(settings.ComPort))
        {
            var serial = new SerialScannerService(settings.ComPort);
            serial.Start();
            return serial;
        }

        if (IsHidConfigured(settings))
        {
            var gs = ScannerGsSettings.FromAppSettings(settings);
            var raw = new RawInputScannerService();
            raw.Attach(window, settings.ScannerDevicePath, wizardMode: false, gs);
            return raw;
        }

        // No discovery on the main window — use «Настроить сканер» to run ScannerSetupWindow.
        return new NullScannerSource();
    }

    public static bool IsHidConfigured(AppSettings settings) =>
        settings.ScannerMode == ScannerMode.RawInput
        && !string.IsNullOrWhiteSpace(settings.ScannerDevicePath);

    private static bool PortExists(string port) =>
        SerialScannerService.GetAvailablePorts().Contains(port, StringComparer.OrdinalIgnoreCase);
}
