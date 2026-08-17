namespace DoubleMark.Crpt;

/// <summary>
/// Platform constraints for CRPT integration (spec §15 — CryptoPro Windows-only MVP).
/// </summary>
public static class CrptPlatformGuard
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    public static void EnsureWindowsForCertificateOperations(string? operation = null)
    {
        if (IsWindows)
            return;

        var detail = string.IsNullOrWhiteSpace(operation)
            ? "Certificate and CryptoPro signing operations"
            : operation;

        throw new PlatformNotSupportedException(
            $"{detail} require Windows with CryptoPro CSP. {CrptRiskMitigations.CryptoProWindowsDependencyNote}");
    }
}
