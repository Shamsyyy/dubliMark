using System;
using System.IO.Ports;
using System.Text;

namespace DubliMark.Desktop.Services;

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
            Encoding = Encoding.GetEncoding(28591) // ISO-8859-1 — bytes arrive 1:1
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
        catch { /* ignore serial errors */ }
    }

    public void Dispose() => _port.Dispose();

    public static string[] GetAvailablePorts() => SerialPort.GetPortNames();
}
