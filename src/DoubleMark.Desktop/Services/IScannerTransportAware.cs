namespace DoubleMark.Desktop.Services;

/// <summary>
/// Scanner sources that can report which transport delivered the last barcode (e.g. Auto mode).
/// </summary>
public interface IScannerTransportAware
{
    string? LastBarcodeTransport { get; }

    string? ActiveTransportSummary { get; }
}
