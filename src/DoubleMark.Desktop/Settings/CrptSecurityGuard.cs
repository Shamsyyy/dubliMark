using System.Text.RegularExpressions;

namespace DoubleMark.Desktop.Settings;

/// <summary>
/// Validates CRPT settings persistence rules (spec §12.1, §7.1 DPAPI).
/// </summary>
public static class CrptSecurityGuard
{
    private static readonly string[] ForbiddenPlainJsonKeys =
    [
        "omsId",
        "connectionId",
        "certificateThumbprint",
        "nkApiKey",
        "token",
        "clientToken",
        "accessToken",
        "refreshToken",
    ];

    private static readonly Regex ForbiddenKeyPattern = new(
        "\"(" + string.Join("|", ForbiddenPlainJsonKeys) + ")\"\\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void ValidatePlainSettingsJson(string settingsJson, CrptSecrets? secrets = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsJson);

        if (ForbiddenKeyPattern.IsMatch(settingsJson))
        {
            throw new InvalidOperationException(
                "CRPT secrets must not be stored in crpt-settings.json. Use DPAPI crpt-secrets.dat.");
        }

        if (secrets is null)
            return;

        foreach (var value in EnumerateSecretValues(secrets))
        {
            if (settingsJson.Contains(value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "CRPT secret value detected in plain settings JSON.");
            }
        }
    }

    public static void ValidateSettingsSnapshot(CrptSettings settings, CrptSecrets secrets, string settingsJson)
    {
        _ = settings;
        ValidatePlainSettingsJson(settingsJson, secrets);
    }

    private static IEnumerable<string> EnumerateSecretValues(CrptSecrets secrets)
    {
        if (!string.IsNullOrWhiteSpace(secrets.OmsId))
            yield return secrets.OmsId.Trim();
        if (!string.IsNullOrWhiteSpace(secrets.ConnectionId))
            yield return secrets.ConnectionId.Trim();
        if (!string.IsNullOrWhiteSpace(secrets.CertificateThumbprint))
            yield return secrets.CertificateThumbprint.Trim();
        if (!string.IsNullOrWhiteSpace(secrets.NkApiKey))
            yield return secrets.NkApiKey.Trim();
    }
}
