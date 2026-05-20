using System.Windows;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services;

/// <summary>
/// Listens on COM and HID (Raw Input) at the same time; first successful scan wins (deduped).
/// </summary>
public sealed class AutoScannerSource : IScannerSource, IScannerTransportAware, IDisposable
{
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromMilliseconds(800);

    private readonly object _dedupeLock = new();
    private SerialScannerService? _serial;
    private RawInputScannerService? _rawInput;
    private string? _lastBarcode;
    private DateTime _lastBarcodeUtc = DateTime.MinValue;

    public event EventHandler<string>? BarcodeReceived;
    public event EventHandler<string>? ConnectionLost;

    public string? LastBarcodeTransport { get; private set; }
    public string? ActiveTransportSummary { get; private set; }

    public void Start(Window window, AppSettings settings)
    {
        Stop();
        var parts = new List<string>();

        var ports = SerialScannerService.GetAvailablePorts();
        var port = !string.IsNullOrWhiteSpace(settings.ComPort)
            ? settings.ComPort.Trim()
            : ports.FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(port))
        {
            try
            {
                var baud = settings.ComBaudRate > 0 ? settings.ComBaudRate : 9600;
                _serial = new SerialScannerService(port, baud);
                _serial.BarcodeReceived += OnComBarcode;
                _serial.ConnectionLost += OnSerialConnectionLost;
                _serial.Start();
                parts.Add($"COM {port} ({baud})");
                LoggingService.Info("Scanner.Auto", $"COM listening on {port}");
            }
            catch (Exception ex)
            {
                LoggingService.Warn("Scanner.Auto", $"COM open failed for {port}: {ex.Message}");
            }
        }
        else
        {
            LoggingService.Warn("Scanner.Auto", "No COM port available for auto mode");
        }

        try
        {
            var gs = ScannerGsSettings.FromAppSettings(settings);
            var (attachPath, listenAll) = ScannerSourceFactory.ResolveHidListen(settings, filterByDevice: true);

            _rawInput = new RawInputScannerService();
            _rawInput.ConfigureGsMapping(gs);
            _rawInput.BarcodeReceived += OnHidBarcode;
            _rawInput.AttachWhenReady(window, attachPath, wizardMode: listenAll, gs);

            parts.Add(listenAll
                ? "HID (все клавиатуры)"
                : "HID (выбранное устройство)");
            LoggingService.Info("Scanner.Auto",
                "HID listening " + (attachPath ?? "(all keyboards)"));
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner.Auto", "HID attach failed", ex);
        }

        ActiveTransportSummary = parts.Count > 0 ? string.Join(" + ", parts) : "нет активных каналов";
    }

    private void OnSerialConnectionLost(object? sender, string reason) =>
        ConnectionLost?.Invoke(this, reason);

    private void OnComBarcode(object? sender, string raw) => Emit(raw, "COM");

    private void OnHidBarcode(object? sender, string raw) => Emit(raw, "HID");

    private void Emit(string raw, string transport)
    {
        if (string.IsNullOrEmpty(raw))
            return;

        var now = DateTime.UtcNow;
        lock (_dedupeLock)
        {
            if (raw == _lastBarcode && now - _lastBarcodeUtc < DedupeWindow)
                return;
            _lastBarcode = raw;
            _lastBarcodeUtc = now;
        }

        LastBarcodeTransport = transport;
        LoggingService.Info("Scanner.Auto", $"Scan from {transport} length={raw.Length}");
        BarcodeReceived?.Invoke(this, raw);
    }

    public void Stop()
    {
        if (_serial != null)
        {
            _serial.BarcodeReceived -= OnComBarcode;
            _serial.ConnectionLost -= OnSerialConnectionLost;
            _serial.Stop();
            _serial.Dispose();
            _serial = null;
        }

        if (_rawInput != null)
        {
            _rawInput.BarcodeReceived -= OnHidBarcode;
            _rawInput.Stop();
            _rawInput = null;
        }

        ActiveTransportSummary = null;
    }

    public void Dispose() => Stop();
}
