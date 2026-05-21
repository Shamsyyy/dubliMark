using System.IO;
using System.Reflection;
using System.Text.Json;
using DoubleMark.Desktop.Services.Update;

namespace DoubleMark.Desktop.Services;

public sealed record AppReleaseInfo(
    string Version,
    DateTimeOffset? BuiltAtUtc,
    string BuildId,
    bool AutoUpdateAvailable,
    string InstallPath,
    string? InstallWarning)
{
    public string VersionLabel => "DoubleMark " + Version;

    public string BuiltAtLabel =>
        BuiltAtUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

}

public static class AppReleaseInfoProvider
{
    private static AppReleaseInfo? _cached;

    public static AppReleaseInfo Current => _cached ??= Load();

    public static void ResetCache() => _cached = null;

    private static AppReleaseInfo Load()
    {
        var installPath = Environment.ProcessPath ?? AppContext.BaseDirectory;
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyVersionText = ReadAssemblyVersionText(assembly);
        var fileVersionText = ReadFileVersionText(installPath);

        string? buildInfoVersion = null;
        DateTimeOffset? builtAt = null;
        var buildId = "";
        var autoUpdate = false;

        var buildInfoPath = Path.Combine(AppContext.BaseDirectory, "buildinfo.json");
        if (File.Exists(buildInfoPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(buildInfoPath));
                var root = document.RootElement;
                if (root.TryGetProperty("version", out var versionProp) &&
                    versionProp.ValueKind == JsonValueKind.String)
                {
                    buildInfoVersion = versionProp.GetString();
                }

                if (root.TryGetProperty("buildUtc", out var buildUtcProp) &&
                    buildUtcProp.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(buildUtcProp.GetString(), out var parsed))
                {
                    builtAt = parsed;
                }

                if (root.TryGetProperty("buildId", out var buildIdProp) &&
                    buildIdProp.ValueKind == JsonValueKind.String)
                {
                    buildId = buildIdProp.GetString() ?? "";
                }

                autoUpdate = root.TryGetProperty("autoUpdateAvailable", out var autoProp) &&
                             autoProp.ValueKind == JsonValueKind.True;
            }
            catch (Exception ex)
            {
                LoggingService.Warn("AppRelease", "buildinfo.json read failed: " + ex.Message);
            }
        }

        if (builtAt == null && !string.IsNullOrWhiteSpace(installPath) && File.Exists(installPath))
            builtAt = File.GetLastWriteTimeUtc(installPath);

        var resolvedVersion = ResolveBestVersion(buildInfoVersion, fileVersionText, assemblyVersionText);
        var warning = BuildInstallWarning(resolvedVersion, buildInfoVersion, fileVersionText, assemblyVersionText, buildId);

        LoggingService.Info(
            "AppRelease",
            "Version resolved=" + resolvedVersion
            + " assembly=" + assemblyVersionText
            + " file=" + fileVersionText
            + " buildinfo=" + (buildInfoVersion ?? "—")
            + " path=" + installPath);

        return new AppReleaseInfo(resolvedVersion, builtAt, buildId, autoUpdate, installPath, warning);
    }

    private static string ResolveBestVersion(string? buildInfoVersion, string fileVersion, string assemblyVersion)
    {
        var best = NormalizeVersion(assemblyVersion);
        best = PreferNewerVersion(best, NormalizeVersion(fileVersion));
        if (!string.IsNullOrWhiteSpace(buildInfoVersion))
            best = PreferNewerVersion(best, NormalizeVersion(buildInfoVersion));
        return best;
    }

    private static string? BuildInstallWarning(
        string resolvedVersion,
        string? buildInfoVersion,
        string fileVersion,
        string assemblyVersion,
        string buildId)
    {
        var sources = new List<string> { NormalizeVersion(assemblyVersion), NormalizeVersion(fileVersion) };
        if (!string.IsNullOrWhiteSpace(buildInfoVersion))
            sources.Add(NormalizeVersion(buildInfoVersion));
        sources = sources.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (sources.Count <= 1)
            return null;

        var newest = sources.Aggregate((a, b) => PreferNewerVersion(a, b));
        if (string.Equals(resolvedVersion, newest, StringComparison.OrdinalIgnoreCase)
            && VersionComparer.IsNewer(newest, NormalizeVersion(assemblyVersion)))
        {
            return "В папке установки смешаны версии файлов. Удалите папку DoubleMark и установите "
                   + newest + " заново"
                   + (string.IsNullOrWhiteSpace(buildId) ? "." : " (сборка " + buildId + ").");
        }

        return null;
    }

    private static string ReadAssemblyVersionText(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return NormalizeVersion(informational);

        var version = assembly.GetName().Version;
        return version == null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string ReadFileVersionText(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return "0.0.0";

        try
        {
            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
            if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                return NormalizeVersion(fvi.ProductVersion);
            if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                return NormalizeVersion(fvi.FileVersion);
        }
        catch (Exception ex)
        {
            LoggingService.Warn("AppRelease", "FileVersionInfo failed: " + ex.Message);
        }

        return "0.0.0";
    }

    private static string NormalizeVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "0.0.0";

        var text = value.Trim();
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
            text = text[..plus];

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        if (space >= 0)
            text = text[..space];

        return text;
    }

    private static string PreferNewerVersion(string primary, string secondary)
    {
        if (VersionComparer.IsNewer(secondary, primary))
            return secondary;
        return primary;
    }
}
