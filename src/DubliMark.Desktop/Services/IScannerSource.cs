namespace DubliMark.Desktop.Services;

public interface IScannerSource
{
    event EventHandler<string> BarcodeReceived;
    void Start();
    void Stop();
}
