using System.Security.Cryptography.X509Certificates;

namespace DoubleMark.Crpt;

/// <summary>
/// Resolves the UKEP certificate used for CRPT signing (spec §4.1).
/// </summary>
public interface ICrptCertificateProvider
{
    X509Certificate2 FindCertificate(CrptConnectionSettings settings);

    /// <summary>Lists valid certificates with private keys from the Windows store.</summary>
    IReadOnlyList<CrptCertificateDescriptor> ListEligibleCertificates(string? innFilter = null);
}
