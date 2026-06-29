using System.Security.Cryptography.X509Certificates;

namespace DoubleMark.Crpt;

public static class CrptCertificateProvider
{
    public static X509Certificate2 FindCertificate(CrptConnectionSettings settings)
    {
        CrptRiskMitigations.EnsureWindowsForCryptoPro();

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        var candidates = store.Certificates
            .Find(X509FindType.FindByTimeValid, DateTime.Now, false)
            .Cast<X509Certificate2>()
            .Where(c => c.HasPrivateKey)
            .ToList();

        if (!string.IsNullOrWhiteSpace(settings.CertificateThumbprint))
        {
            var byThumb = candidates.FirstOrDefault(c =>
                c.Thumbprint.Equals(settings.CertificateThumbprint, StringComparison.OrdinalIgnoreCase));
            if (byThumb is null)
                throw new InvalidOperationException($"Certificate not found: {settings.CertificateThumbprint}");
            return byThumb;
        }

        var byInn = candidates.FirstOrDefault(c =>
            c.Subject.Contains($"ИНН ЮЛ={settings.Inn}", StringComparison.OrdinalIgnoreCase) ||
            c.Subject.Contains($"INN={settings.Inn}", StringComparison.OrdinalIgnoreCase) ||
            c.Subject.Contains(settings.Inn, StringComparison.Ordinal));

        if (byInn is null)
            throw new InvalidOperationException($"No valid certificate found for INN {settings.Inn}");

        return byInn;
    }

    public static IReadOnlyList<CrptCertificateDescriptor> ListEligibleCertificates(string? innFilter = null)
    {
        CrptRiskMitigations.EnsureWindowsForCryptoPro();

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        var candidates = store.Certificates
            .Find(X509FindType.FindByTimeValid, DateTime.Now, false)
            .Cast<X509Certificate2>()
            .Where(c => c.HasPrivateKey)
            .Select(c => new CrptCertificateDescriptor(c.Subject, c.Thumbprint, c.NotAfter))
            .OrderBy(c => c.Subject, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(innFilter))
            return candidates;

        return candidates
            .Where(c =>
                c.Subject.Contains($"ИНН ЮЛ={innFilter}", StringComparison.OrdinalIgnoreCase) ||
                c.Subject.Contains($"INN={innFilter}", StringComparison.OrdinalIgnoreCase) ||
                c.Subject.Contains(innFilter, StringComparison.Ordinal))
            .ToList();
    }
}
