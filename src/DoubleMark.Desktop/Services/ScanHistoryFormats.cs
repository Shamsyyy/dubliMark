namespace DoubleMark.Desktop.Services;

public static class ScanHistoryFormats
{
    public const string DateTimePattern = "dd.MM.yyyy HH:mm:ss";

    public static string FormatTimestamp(DateTime timestamp) =>
        timestamp.ToString(DateTimePattern);
}
