namespace DoubleMark.Desktop.Services.Update;

public static class VersionComparer
{
    public static bool IsNewer(string remoteVersion, string currentVersion)
    {
        if (!TryParse(remoteVersion, out var remote))
            return false;
        if (!TryParse(currentVersion, out var current))
            return true;

        return remote > current;
    }

    public static bool IsBelowMinimum(string currentVersion, string minSupportedVersion)
    {
        if (!TryParse(minSupportedVersion, out var minimum))
            return false;
        if (!TryParse(currentVersion, out var current))
            return false;

        return current < minimum;
    }

    public static bool TryParse(string value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[1..];

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        if (!int.TryParse(parts[0], out var major))
            return false;

        var minor = parts.Length > 1 && int.TryParse(parts[1], out var m) ? m : 0;
        var build = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 0;
        var revision = parts.Length > 3 && int.TryParse(parts[3], out var r) ? r : 0;
        version = new Version(major, minor, build, revision);
        return true;
    }
}
