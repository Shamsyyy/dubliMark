namespace DoubleMark.Desktop.Services;

/// <summary>No-op scanner — HID discovery runs only in <see cref="ScannerSetupWindow"/>.</summary>
public sealed class NullScannerSource : IScannerSource
{
#pragma warning disable CS0067
    public event EventHandler<string>? BarcodeReceived;
#pragma warning restore CS0067

    public void Stop() { }
}
