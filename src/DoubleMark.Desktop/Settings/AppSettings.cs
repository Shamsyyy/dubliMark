using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Services.Cloud;

namespace DoubleMark.Desktop.Settings;

public enum ScannerMode
{
    Unset,
    /// <summary>Listen on COM and HID; use whichever delivers a scan first.</summary>
    Auto,
    Com,
    Hid,
    RawInput
}

public enum PrintMode
{
    Manual,
    Auto
}

public sealed class AppSettings
{
    public string? ComPort { get; set; }
    public int ComBaudRate { get; set; } = 9600;
    public string? ScannerDevicePath { get; set; }
    public string? SelectedHidDeviceId { get; set; }
    public string? SelectedRawInputDeviceId { get; set; }
    public ScannerMode ScannerMode { get; set; } = ScannerMode.Auto;
    /// <summary>After a fast HID wedge scan, save the Raw Input device path automatically.</summary>
    public bool ScannerAutoBindHid { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    public bool LocalTemplatesMigratedToCloud { get; set; }
    public ScanHistoryDuplicateMode ScanHistoryDuplicateMode { get; set; } = ScanHistoryDuplicateMode.IgnoreRecentDuplicates;
    public bool AutoSaveExports { get; set; } = true;
    public string? ExportDirectory { get; set; }
    public PrintMode PrintMode { get; set; } = PrintMode.Manual;
    public bool AutoPrintEnabled { get; set; }
    public string? PrinterName { get; set; }
    public int PrintCopies { get; set; } = 1;
    public bool PrintWithoutConfirmation { get; set; }
    public int PrintDelayMs { get; set; }
    public int DuplicatePrintBlockSeconds { get; set; } = 2;
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
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DoubleMark");

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
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.NormalizePrintMode();
            settings.NormalizeScannerFields();
            return settings;
        }
        catch (Exception ex)
        {
            LoggingService.Error("Settings", "Load failed", ex);
            TryArchiveCorruptedSettings();
            return new AppSettings();
        }
    }

    private static void TryArchiveCorruptedSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return;

            var backup = Path.Combine(SettingsDirectory, $"settings.corrupted.{DateTime.Now:yyyyMMddHHmmss}.json");
            File.Move(SettingsFilePath, backup, overwrite: false);
            LoggingService.Warn("Settings", "Corrupted settings archived to " + backup);
        }
        catch (Exception ex)
        {
            LoggingService.Error("Settings", "Failed to archive corrupted settings", ex);
        }
    }

    public void Save()
    {
        NormalizePrintMode();
        NormalizeScannerFields();
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }

    public void NormalizePrintMode()
    {
        if (ComBaudRate <= 0)
            ComBaudRate = 9600;

        if (DuplicatePrintBlockSeconds < 0)
            DuplicatePrintBlockSeconds = 2;

        AutoPrintEnabled = PrintMode == PrintMode.Auto;
        if (PrintMode == PrintMode.Auto)
            PrintWithoutConfirmation = true;
    }

    public void NormalizeScannerFields()
    {
        if (ScannerMode == ScannerMode.Unset)
            ScannerMode = ScannerMode.Auto;

        if (!string.IsNullOrWhiteSpace(SelectedHidDeviceId))
            ScannerDevicePath = SelectedHidDeviceId;
        else if (!string.IsNullOrWhiteSpace(ScannerDevicePath))
            SelectedHidDeviceId = ScannerDevicePath;

        if (ScannerMode == ScannerMode.RawInput
            && !string.IsNullOrWhiteSpace(EffectiveHidDevicePath)
            && string.IsNullOrWhiteSpace(SelectedRawInputDeviceId))
        {
            SelectedHidDeviceId ??= ScannerDevicePath;
            ScannerMode = ScannerMode.Hid;
        }
    }

    public string? EffectiveHidDevicePath =>
        !string.IsNullOrWhiteSpace(SelectedHidDeviceId)
            ? SelectedHidDeviceId
            : ScannerDevicePath;

    public void Reset()
    {
        ComPort = null;
        ComBaudRate = 9600;
        ScannerDevicePath = null;
        SelectedHidDeviceId = null;
        SelectedRawInputDeviceId = null;
        ScannerMode = ScannerMode.Unset;
        ScannerAutoBindHid = true;
        AutoCheckUpdates = true;
        AutoSaveExports = true;
        ExportDirectory = null;
        PrintMode = PrintMode.Manual;
        AutoPrintEnabled = false;
        PrinterName = null;
        PrintCopies = 1;
        PrintWithoutConfirmation = false;
        PrintDelayMs = 0;
        DuplicatePrintBlockSeconds = 2;
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
            PrintWithoutConfirmation = PrintWithoutConfirmation || PrintMode == PrintMode.Auto,
            DelayBeforePrintMs = Math.Max(0, PrintDelayMs),
            DuplicateProtectionSeconds = Math.Max(0, DuplicatePrintBlockSeconds),
            SaveFileBeforePrint = SavePrintFileBeforePrint,
            PrintRoot = EffectivePrintDirectory,
            DefaultTemplateName = DefaultPrintTemplateName,
            Dpi = 300
        };
}
