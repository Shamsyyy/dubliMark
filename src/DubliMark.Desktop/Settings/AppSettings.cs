using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DubliMark.Core.Print;

namespace DubliMark.Desktop.Settings;

public enum ScannerMode
{
    Unset,
    Com,
    RawInput
}

public sealed class AppSettings
{
    public string? ComPort { get; set; }
    public string? ScannerDevicePath { get; set; }
    public ScannerMode ScannerMode { get; set; } = ScannerMode.Unset;
    public bool AutoSaveExports { get; set; } = true;
    public string? ExportDirectory { get; set; }
    public bool AutoPrintEnabled { get; set; }
    public string? PrinterName { get; set; }
    public int PrintCopies { get; set; } = 1;
    public bool PrintWithoutConfirmation { get; set; }
    public int PrintDelayMs { get; set; }
    public int DuplicatePrintBlockSeconds { get; set; } = 5;
    public bool SavePrintFileBeforePrint { get; set; } = true;
    public string? PrintDirectory { get; set; }
    public string? DefaultPrintTemplateName { get; set; } = "ЧЗ 30x20 мм";

    public ScannerGsMappingMode ScannerGsMappingMode { get; set; } = ScannerGsMappingMode.Auto;
    public string ScannerVisibleGsChar { get; set; } = "|";
    public ushort? ScannerCustomGsVkey { get; set; }
    public ushort? ScannerCustomGsMakeCode { get; set; }
    public bool ScannerCustomGsRequiresCtrl { get; set; }
    public bool ScannerCustomGsRequiresShift { get; set; }
    public bool ScannerCustomGsRequiresAlt { get; set; }

    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DubliMark");

    public static string DefaultExportDirectory => Path.Combine(SettingsDirectory, "exports");
    public static string DefaultPrintDirectory => Path.Combine(SettingsDirectory, "prints");

    private static string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }

    public void Reset()
    {
        ComPort = null;
        ScannerDevicePath = null;
        ScannerMode = ScannerMode.Unset;
        AutoSaveExports = true;
        ExportDirectory = null;
        AutoPrintEnabled = false;
        PrinterName = null;
        PrintCopies = 1;
        PrintWithoutConfirmation = false;
        PrintDelayMs = 0;
        DuplicatePrintBlockSeconds = 5;
        SavePrintFileBeforePrint = true;
        PrintDirectory = null;
        DefaultPrintTemplateName = "ЧЗ 30x20 мм";
        Save();
    }

    public string EffectiveExportDirectory =>
        string.IsNullOrWhiteSpace(ExportDirectory) ? DefaultExportDirectory : ExportDirectory;

    public string EffectivePrintDirectory =>
        string.IsNullOrWhiteSpace(PrintDirectory) ? DefaultPrintDirectory : PrintDirectory;

    public PrintPipelineSettings ToPrintPipelineSettings() =>
        new()
        {
            AutoPrintEnabled = AutoPrintEnabled,
            PrinterName = PrinterName,
            Copies = Math.Max(1, PrintCopies),
            PrintWithoutConfirmation = PrintWithoutConfirmation,
            DelayBeforePrintMs = Math.Max(0, PrintDelayMs),
            DuplicateProtectionSeconds = Math.Max(0, DuplicatePrintBlockSeconds),
            SaveFileBeforePrint = SavePrintFileBeforePrint,
            PrintRoot = EffectivePrintDirectory,
            DefaultTemplateName = DefaultPrintTemplateName,
            Dpi = 300
        };
}
