using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using Microsoft.Win32;

namespace DoubleMark.Desktop.Services;

public class SerialScannerService : IScannerSource, IDisposable
{
    public event EventHandler<string>? BarcodeReceived;

    private readonly SerialPort _port;
    private readonly StringBuilder _buffer = new();
    private DateTime _lastByteTime = DateTime.MinValue;

    public SerialScannerService(string portName, int baudRate = 9600)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 500,
            Encoding = Encoding.GetEncoding(28591)
        };
        _port.DataReceived += OnDataReceived;
    }

    public void Start()
    {
        if (!_port.IsOpen) _port.Open();
    }

    public void Stop()
    {
        if (_port.IsOpen) _port.Close();
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var chunk = _port.ReadExisting();
            var now = DateTime.UtcNow;

            if ((now - _lastByteTime).TotalMilliseconds > 500)
                _buffer.Clear();
            _lastByteTime = now;

            _buffer.Append(chunk);

            var data = _buffer.ToString();
            int termIdx = data.IndexOfAny(new[] { '\r', '\n' });
            if (termIdx >= 0)
            {
                var barcode = data.Substring(0, termIdx);
                _buffer.Clear();
                if (!string.IsNullOrEmpty(barcode))
                    BarcodeReceived?.Invoke(this, barcode);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SerialScanner] {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _port.Dispose();
    }

    public static string[] GetAvailablePorts()
    {
        var ports = SerialPort.GetPortNames();
        if (ports.Length > 0)
            return ports.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

        return EnumeratePortsFromRegistry();
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
            Debug.WriteLine($"[SerialScanner] Registry enumeration failed: {ex.Message}");
        }

        return found
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
