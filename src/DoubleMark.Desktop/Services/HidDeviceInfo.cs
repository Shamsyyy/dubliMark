using System.Text.RegularExpressions;

namespace DoubleMark.Desktop.Services;

public sealed record HidDeviceInfo(
    string DevicePath,
    string? Name,
    string? VendorId,
    string? ProductId,
    string DisplayName)
{
    private static readonly Regex VidPidRegex = new(
        @"VID_([0-9A-Fa-f]{4}).*?PID_([0-9A-Fa-f]{4})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool SharesVidPid(string? pathA, string? pathB)
    {
        if (string.IsNullOrWhiteSpace(pathA) || string.IsNullOrWhiteSpace(pathB))
            return false;

        var a = FromPath(pathA);
        var b = FromPath(pathB);
        return a.VendorId != "—"
               && a.ProductId != "—"
               && string.Equals(a.VendorId, b.VendorId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(a.ProductId, b.ProductId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesConfiguredDevice(string? incomingPath, string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return true;
        if (string.IsNullOrWhiteSpace(incomingPath))
            return false;

        return incomingPath.Equals(configuredPath, StringComparison.OrdinalIgnoreCase)
               || SharesVidPid(incomingPath, configuredPath);
    }

    public static HidDeviceInfo FromPath(string devicePath)
    {
        var vid = "—";
        var pid = "—";
        var match = VidPidRegex.Match(devicePath);
        if (match.Success)
        {
            vid = match.Groups[1].Value.ToUpperInvariant();
            pid = match.Groups[2].Value.ToUpperInvariant();
        }

        var shortName = devicePath.Contains('#')
            ? devicePath.Split('#', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? devicePath
            : devicePath;

        return new HidDeviceInfo(
            devicePath,
            shortName,
            vid,
            pid,
            $"VID={vid} PID={pid}");
    }
}
