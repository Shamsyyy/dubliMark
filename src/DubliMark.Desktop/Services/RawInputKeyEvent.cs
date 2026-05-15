namespace DubliMark.Desktop.Services;

public enum RawInputKeyAction
{
    Char,
    GsRestored,
    Terminator,
    IgnoredBreak,
    Modifier
}

public sealed class RawInputKeyEvent
{
    public DateTime UtcTime { get; init; }
    public ushort VKey { get; init; }
    public ushort MakeCode { get; init; }
    public ushort Flags { get; init; }
    public bool IsE0 { get; init; }
    public bool IsBreak { get; init; }
    public bool Ctrl { get; init; }
    public bool Shift { get; init; }
    public bool Alt { get; init; }
    public char? DecodedChar { get; init; }
    public RawInputKeyAction Action { get; init; }
    public string? Note { get; init; }
    public string Source { get; init; } = "raw";

    public string ToLogLine() =>
        $"{UtcTime:HH:mm:ss.fff} src={Source} VK=0x{VKey:X2} Make=0x{MakeCode:X2} Flags=0x{Flags:X2} " +
        $"E0={IsE0} Break={IsBreak} Ctrl={Ctrl} Shift={Shift} Alt={Alt} " +
        $"ch={(DecodedChar.HasValue ? $"U+{((int)DecodedChar.Value):X4}" : "-")} act={Action}" +
        (Note != null ? $" {Note}" : "");
}

public sealed class RawInputScanDiagnostics
{
    public DateTime CompletedUtc { get; init; }
    public string Barcode { get; init; } = "";
    public int GsRestoredCount { get; init; }
    public IReadOnlyList<RawInputKeyEvent> Keys { get; init; } = Array.Empty<RawInputKeyEvent>();

    public string KeysHexDump =>
        string.Join(" ", Keys.Select(k =>
            $"VK{k.VKey:X2}:MK{k.MakeCode:X2}{(k.IsBreak ? ":UP" : ":DN")}"));

    public string KeysDetailedDump =>
        string.Join(Environment.NewLine, Keys.Select(k => k.ToLogLine()));
}
