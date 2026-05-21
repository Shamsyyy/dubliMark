using System.Text.Json.Serialization;

namespace DoubleMark.Desktop.Services.Update;

public sealed class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("notes")]
    public List<string> Notes { get; set; } = new();

    [JsonPropertyName("installerUrl")]
    public string InstallerUrl { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("minSupportedVersion")]
    public string? MinSupportedVersion { get; set; }

    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(Title) ? "DoubleMark " + Version : Title;
}

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    UpdateManifest? Manifest,
    string? UserMessage,
    string? LogMessage);

public enum UpdateDownloadStatus
{
    Success,
    HashMismatch,
    Failed
}

public sealed record UpdateDownloadResult(
    UpdateDownloadStatus Status,
    string? FilePath,
    string? UserMessage,
    string? LogMessage);
