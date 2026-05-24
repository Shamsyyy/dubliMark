using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DoubleMark.Desktop.Services.Update;

public sealed record AuthenticodeVerificationResult(
    bool IsValid,
    string? PublisherName,
    string? UserMessage,
    string? LogMessage)
{
    public static AuthenticodeVerificationResult Success(string publisher) =>
        new(true, publisher, null, "Publisher=" + publisher);

    public static AuthenticodeVerificationResult Failed(string userMessage, string? logMessage = null) =>
        new(false, null, userMessage, logMessage ?? userMessage);

    public static AuthenticodeVerificationResult NotSupported(string reason) =>
        new(false, null, reason, reason);
}

public static class AuthenticodeSignatureVerifier
{
    public static readonly string[] DefaultAllowedPublisherMarkers =
    {
        "DoubleMark",
        "DOUBLEMARK"
    };

    public static bool AllowUnsignedUpdates =>
        string.Equals(Environment.GetEnvironmentVariable("DOUBLEMARK_ALLOW_UNSIGNED_UPDATES"), "1", StringComparison.Ordinal);

    public static bool ForceRequireSignedUpdates =>
        string.Equals(Environment.GetEnvironmentVariable("DOUBLEMARK_REQUIRE_SIGNED_UPDATES"), "1", StringComparison.Ordinal);

    public static bool ShouldRequireSignature(bool manifestRequireSignature) =>
        ForceRequireSignedUpdates || (manifestRequireSignature && !AllowUnsignedUpdates);

    public static AuthenticodeVerificationResult VerifyInstaller(
        string filePath,
        bool requireSignature,
        IReadOnlyList<string>? allowedPublisherMarkers = null)
    {
        if (!ShouldRequireSignature(requireSignature))
            return AuthenticodeVerificationResult.Success("unsigned-allowed");

        if (!OperatingSystem.IsWindows())
        {
            return AuthenticodeVerificationResult.Failed(
                "Проверка подписи обновления доступна только в Windows.",
                "Authenticode requires Windows");
        }

        if (!File.Exists(filePath))
        {
            return AuthenticodeVerificationResult.Failed(
                "Файл обновления не найден.",
                "Installer file missing");
        }

        var markers = allowedPublisherMarkers ?? DefaultAllowedPublisherMarkers;

        try
        {
            var cert = X509Certificate.CreateFromSignedFile(filePath);
            using var x509 = new X509Certificate2(cert);

            if (DateTime.UtcNow > x509.NotAfter.ToUniversalTime())
            {
                return AuthenticodeVerificationResult.Failed(
                    "Цифровая подпись обновления просрочена. Скачайте установщик вручную с doublemark.ru.",
                    "Certificate expired");
            }

            if (DateTime.UtcNow < x509.NotBefore.ToUniversalTime())
            {
                return AuthenticodeVerificationResult.Failed(
                    "Цифровая подпись обновления ещё не действительна.",
                    "Certificate not yet valid");
            }

            if (!PublisherNameMatcher.IsAllowed(x509.Subject, markers))
            {
                return AuthenticodeVerificationResult.Failed(
                    "Обновление подписано неизвестным издателем и заблокировано.",
                    "Publisher mismatch subject=" + SanitizeForLog(x509.Subject));
            }

            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            chain.ChainPolicy.VerificationTime = DateTime.UtcNow;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            if (!chain.Build(x509))
            {
                var status = chain.ChainStatus.FirstOrDefault();
                return AuthenticodeVerificationResult.Failed(
                    "Цепочка доверия цифровой подписи обновления не прошла проверку.",
                    "Chain build failed: " + status.StatusInformation);
            }

            var publisher = PublisherNameMatcher.ExtractDisplayName(x509.Subject);
            return AuthenticodeVerificationResult.Success(publisher);
        }
        catch (CryptographicException ex)
        {
            return AuthenticodeVerificationResult.Failed(
                "Установщик обновления не подписан или подпись недействительна. Скачайте DoubleMark вручную с doublemark.ru.",
                "Authenticode failed: " + ex.Message);
        }
        catch (Exception ex)
        {
            return AuthenticodeVerificationResult.Failed(
                "Не удалось проверить цифровую подпись обновления.",
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string SanitizeForLog(string value) =>
        value.Length <= 120 ? value : value[..120] + "...";
}

public static class PublisherNameMatcher
{
    public static bool IsAllowed(string certificateSubject, IReadOnlyList<string> allowedMarkers)
    {
        if (string.IsNullOrWhiteSpace(certificateSubject))
            return false;

        foreach (var marker in allowedMarkers)
        {
            if (string.IsNullOrWhiteSpace(marker))
                continue;

            if (certificateSubject.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string ExtractDisplayName(string certificateSubject)
    {
        const string cnPrefix = "CN=";
        var parts = certificateSubject.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith(cnPrefix, StringComparison.OrdinalIgnoreCase))
                return part[cnPrefix.Length..];
        }

        return certificateSubject;
    }
}
