using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public ScannerGsMappingMode ScannerGsMappingMode { get; set; } = ScannerGsMappingMode.Auto;
    public string ScannerVisibleGsChar { get; set; } = "|";
    public ushort? ScannerCustomGsVkey { get; set; }
    public ushort? ScannerCustomGsMakeCode { get; set; }
    public bool ScannerCustomGsRequiresCtrl { get; set; }
    public bool ScannerCustomGsRequiresShift { get; set; }
    public bool ScannerCustomGsRequiresAlt { get; set; }

    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DubliMark");

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
        Save();
    }
}
