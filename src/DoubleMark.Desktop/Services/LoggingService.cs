using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DoubleMark.Crpt;

namespace DoubleMark.Desktop.Services;

public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warn,
    Error
}

public static class LoggingService
{
    private static readonly object Gate = new();
    private static readonly string LogsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DoubleMark",
        "logs");

    private static readonly Regex SecretPattern = new(
        @"(access_token|refresh_token|password|SUPABASE_ANON_KEY|apikey|bearer|service_role)\s*[:=]\s*\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RawPayloadPattern = new(
        @"(raw_code|rawPayload|rawEscaped|normalizedEscaped)\s*[:=]\s*\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int LogRetentionDays = 14;

    public static string LogsFolderPath => LogsDirectory;

    public static string CurrentLogFilePath =>
        Path.Combine(LogsDirectory, $"doublemark-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Trace(string category, string message) => Write(LogLevel.Trace, category, message);
    public static void Debug(string category, string message) => Write(LogLevel.Debug, category, message);
    public static void Info(string category, string message) => Write(LogLevel.Info, category, message);
    public static void Warn(string category, string message) => Write(LogLevel.Warn, category, message);

    public static void Error(string category, string message, Exception? ex = null)
    {
        var text = ex == null ? message : message + " " + ex.GetType().Name + ": " + ex.Message;
        Write(LogLevel.Error, category, text);
#if DEBUG
        if (ex != null)
            Write(LogLevel.Error, category, "Stack trace: " + ex.StackTrace);
#endif
    }

    public static void LogStartup()
    {
        PurgeOldLogs();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        Info("App", $"DoubleMark started. Version={version} OS={Environment.OSVersion}");
    }

    public static IReadOnlyList<string> ReadRecentSafeLines(int maxLines = 50)
    {
        try
        {
            var path = CurrentLogFilePath;
            if (!File.Exists(path))
                return Array.Empty<string>();

            var lines = File.ReadAllLines(path);
            return lines
                .Reverse()
                .Take(maxLines)
                .Reverse()
                .Select(RedactSensitive)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string BuildSafeDiagnosticReport(
        string appVersion,
        AppSettingsSnapshot snapshot,
        int maxLogLines = 50)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DoubleMark diagnostics");
        sb.AppendLine("version: " + appVersion);
        sb.AppendLine("os: " + Environment.OSVersion);
        sb.AppendLine("scannerMode: " + snapshot.ScannerMode);
        sb.AppendLine("comPort: " + (snapshot.ComPort ?? "—"));
        sb.AppendLine("baudRate: " + snapshot.ComBaudRate);
        sb.AppendLine("comConnected: " + snapshot.ComConnected);
        sb.AppendLine("availableComPorts: " + string.Join(", ", snapshot.AvailableComPorts));
        sb.AppendLine("hidConfigured: " + snapshot.HidConfigured);
        sb.AppendLine("printer: " + (snapshot.PrinterName ?? "default"));
        sb.AppendLine("template: " + (snapshot.TemplateName ?? "—"));
        sb.AppendLine("printMode: " + snapshot.PrintMode);
        sb.AppendLine("autoPrint: " + snapshot.AutoPrintEnabled);
        sb.AppendLine("lastScanSource: " + (snapshot.LastScanSource ?? "—"));
        sb.AppendLine("lastScanLength: " + snapshot.LastScanLength);
        sb.AppendLine("lastGsCount: " + snapshot.LastGsCount);
        sb.AppendLine("hasAi01: " + snapshot.HasAi01);
        sb.AppendLine("hasAi21: " + snapshot.HasAi21);
        sb.AppendLine("hasAi91: " + snapshot.HasAi91);
        sb.AppendLine("hasAi92: " + snapshot.HasAi92);
        sb.AppendLine();
        sb.AppendLine("Recent logs:");
        foreach (var line in ReadRecentSafeLines(maxLogLines))
            sb.AppendLine(line);
        return RedactSensitive(sb.ToString());
    }

    private static bool ShouldWrite(LogLevel level) =>
#if DEBUG
        true;
#else
        level >= LogLevel.Info;
#endif

    private static void Write(LogLevel level, string category, string message)
    {
        if (!ShouldWrite(level))
            return;

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant()}] [{category}] {RedactSensitive(message)}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogsDirectory);
                File.AppendAllText(CurrentLogFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine(line);
            }
        }
    }

    private static void PurgeOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogsDirectory))
                return;

            var threshold = DateTime.UtcNow.AddDays(-LogRetentionDays);
            foreach (var file in Directory.GetFiles(LogsDirectory, "doublemark-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < threshold)
                {
                    try { File.Delete(file); }
                    catch { /* best effort */ }
                }
            }
        }
        catch
        {
            // best effort
        }
    }

    private static string RedactSensitive(string text)
    {
        var redacted = SecretPattern.Replace(text, "$1=[redacted]");
        redacted = RawPayloadPattern.Replace(redacted, "$1=[redacted]");
        return CrptLogRedactor.Redact(redacted);
    }
}

public sealed record AppSettingsSnapshot(
    string ScannerMode,
    string? ComPort,
    int ComBaudRate,
    bool ComConnected,
    IReadOnlyList<string> AvailableComPorts,
    bool HidConfigured,
    string? PrinterName,
    string? TemplateName,
    string PrintMode,
    bool AutoPrintEnabled,
    string? LastScanSource,
    int LastScanLength,
    int LastGsCount,
    bool HasAi01,
    bool HasAi21,
    bool HasAi91,
    bool HasAi92);
