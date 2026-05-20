using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DoubleMark.Desktop.Services;

public class SerialScannerService : IScannerSource, IDisposable
{
    private static readonly Encoding PortEncoding = Encoding.GetEncoding(28591);
    private static readonly Regex ComNumberRegex = new(@"^COM(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public event EventHandler<string>? BarcodeReceived;
    public event EventHandler<string>? ConnectionLost;

    private readonly SerialPort _port;
    private readonly StringBuilder _buffer = new();
    private readonly object _bufferLock = new();
    private System.Threading.Timer? _idleFlushTimer;
    private DateTime _lastByteTime = DateTime.MinValue;
    private bool _disposed;

    private const int IdleFlushMs = 150;
    private const int MinIdleBarcodeLength = 8;

    public string PortName => _port.PortName;
    public int BaudRate => _port.BaudRate;
    public bool IsOpen => _port.IsOpen;

    public SerialScannerService(string portName, int baudRate = 9600)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 500,
            Handshake = Handshake.None,
            DtrEnable = true,
            RtsEnable = true,
            Encoding = PortEncoding,
            NewLine = "\r\n"
        };
        _port.DataReceived += OnDataReceived;
        _port.ErrorReceived += OnErrorReceived;
    }

    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SerialScannerService));

        if (!_port.IsOpen)
            _port.Open();

        LoggingService.Info("Scanner.COM", $"Connected port={_port.PortName} baud={_port.BaudRate}");
    }

    public void Stop()
    {
        _idleFlushTimer?.Dispose();
        _idleFlushTimer = null;

        if (!_port.IsOpen)
            return;

        var port = _port.PortName;
        _port.Close();
        LoggingService.Info("Scanner.COM", $"Disconnected port={port}");
    }

    private void ScheduleIdleFlush()
    {
        _idleFlushTimer?.Dispose();
        _idleFlushTimer = new System.Threading.Timer(
            _ => FlushIdleBuffer(),
            null,
            IdleFlushMs,
            Timeout.Infinite);
    }

    private void FlushIdleBuffer()
    {
        try
        {
            if (TryExtractBarcode(requireTerminator: false, out var barcode))
                BarcodeReceived?.Invoke(this, barcode);
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner.COM", "Idle flush failed", ex);
        }
    }

    private bool TryExtractBarcode(bool requireTerminator, out string barcode)
    {
        barcode = "";
        lock (_bufferLock)
        {
            if (_buffer.Length == 0)
                return false;

            var data = _buffer.ToString();
            var termIdx = data.IndexOfAny(new[] { '\r', '\n' });
            if (termIdx >= 0)
            {
                barcode = data[..termIdx];
                var removeFrom = termIdx + 1;
                if (termIdx + 1 < data.Length && data[termIdx] == '\r' && data[termIdx + 1] == '\n')
                    removeFrom = termIdx + 2;
                _buffer.Remove(0, removeFrom);
            }
            else if (!requireTerminator && data.Length >= MinIdleBarcodeLength)
            {
                barcode = data;
                _buffer.Clear();
            }
            else
            {
                return false;
            }
        }

        return barcode.Length > 0;
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        LoggingService.Warn("Scanner.COM", $"Serial error on {_port.PortName}: {e.EventType}");
        NotifyConnectionLost($"Ошибка порта {_port.PortName}: {e.EventType}");
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var bytesToRead = _port.BytesToRead;
            if (bytesToRead <= 0)
                return;

            var buffer = new byte[bytesToRead];
            var read = _port.Read(buffer, 0, bytesToRead);
            if (read <= 0)
                return;

            var chunk = PortEncoding.GetString(buffer, 0, read);
            var now = DateTime.UtcNow;

            lock (_bufferLock)
            {
                if ((now - _lastByteTime).TotalMilliseconds > 500)
                    _buffer.Clear();
                _lastByteTime = now;
                _buffer.Append(chunk);
            }

            if (TryExtractBarcode(requireTerminator: true, out var barcode))
                BarcodeReceived?.Invoke(this, barcode);
            else
                ScheduleIdleFlush();
        }
        catch (IOException ex)
        {
            LoggingService.Error("Scanner.COM", $"Read failed on {_port.PortName}", ex);
            NotifyConnectionLost("COM-порт отключён");
        }
        catch (InvalidOperationException ex)
        {
            LoggingService.Error("Scanner.COM", $"Read invalid on {_port.PortName}", ex);
            NotifyConnectionLost("COM-порт недоступен");
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner.COM", "Unexpected read error", ex);
        }
    }

    private void NotifyConnectionLost(string reason)
    {
        try
        {
            if (_port.IsOpen)
                _port.Close();
        }
        catch
        {
            // ignored
        }

        ConnectionLost?.Invoke(this, reason);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _idleFlushTimer?.Dispose();
        _idleFlushTimer = null;
        _port.DataReceived -= OnDataReceived;
        _port.ErrorReceived -= OnErrorReceived;
        try
        {
            if (_port.IsOpen)
                _port.Close();
        }
        catch
        {
            // ignored
        }

        _port.Dispose();
    }

    public static string[] GetAvailablePorts()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var ports = GetAvailablePortsOnce();
            if (ports.Length > 0)
                return ports;

            if (attempt < 2)
                Thread.Sleep(200);
        }

        return [];
    }

    private static string[] GetAvailablePortsOnce()
    {
        try
        {
            var ports = SerialPort.GetPortNames();
            var registryPorts = EnumeratePortsFromRegistry();
            return SortPortsNaturally(ports.Concat(registryPorts));
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner.COM", "Refresh ports failed", ex);
            return [];
        }
    }

    private static string[] SortPortsNaturally(IEnumerable<string> ports) =>
        ports
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, Comparer<string>.Create(CompareComPorts))
            .ToArray();

    private static int CompareComPorts(string? a, string? b)
    {
        var na = ParseComNumber(a);
        var nb = ParseComNumber(b);
        var cmp = na.CompareTo(nb);
        return cmp != 0 ? cmp : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseComNumber(string? port)
    {
        if (string.IsNullOrWhiteSpace(port))
            return int.MaxValue;

        var match = ComNumberRegex.Match(port.Trim());
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : int.MaxValue;
    }

    private static string[] EnumeratePortsFromRegistry()
    {
        var found = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key == null)
                return [];

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is string port && !string.IsNullOrWhiteSpace(port))
                    found.Add(port);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Warn("Scanner.COM", "Registry enumeration failed: " + ex.Message);
        }

        return SortPortsNaturally(found);
    }
}
