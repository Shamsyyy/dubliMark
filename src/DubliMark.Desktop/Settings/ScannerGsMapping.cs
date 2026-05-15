namespace DubliMark.Desktop.Settings;

/// <summary>
/// How the scanner may encode GS (ASCII 0x1D) over HID keyboard.
/// </summary>
public enum ScannerGsMappingMode
{
    /// <summary>Recognize Ctrl+], direct 0x1D, and optional visible/custom chords.</summary>
    Auto,
    /// <summary>Only accept literal GS (0x1D) from the device — no chord remapping.</summary>
    None
}

public sealed class ScannerGsSettings
{
    public ScannerGsMappingMode Mode { get; init; } = ScannerGsMappingMode.Auto;
    /// <summary>If set (e.g. '|'), this visible character is mapped to GS on key down.</summary>
    public char? VisibleGsChar { get; init; } = '|';
    public ushort? CustomGsVKey { get; init; }
    public ushort? CustomGsMakeCode { get; init; }
    public bool CustomGsRequiresCtrl { get; init; }
    public bool CustomGsRequiresShift { get; init; }
    public bool CustomGsRequiresAlt { get; init; }

    public static ScannerGsSettings FromAppSettings(AppSettings s) => new()
    {
        Mode = s.ScannerGsMappingMode,
        VisibleGsChar = string.IsNullOrEmpty(s.ScannerVisibleGsChar) ? null : s.ScannerVisibleGsChar[0],
        CustomGsVKey = s.ScannerCustomGsVkey,
        CustomGsMakeCode = s.ScannerCustomGsMakeCode,
        CustomGsRequiresCtrl = s.ScannerCustomGsRequiresCtrl,
        CustomGsRequiresShift = s.ScannerCustomGsRequiresShift,
        CustomGsRequiresAlt = s.ScannerCustomGsRequiresAlt
    };
}
