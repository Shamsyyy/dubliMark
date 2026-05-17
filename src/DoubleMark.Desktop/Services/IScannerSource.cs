namespace DoubleMark.Desktop.Services;

public interface IScannerSource
{
    event EventHandler<string>? BarcodeReceived;
    void Stop();
}
