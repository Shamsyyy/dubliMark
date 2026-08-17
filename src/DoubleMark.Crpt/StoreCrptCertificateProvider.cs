using System.Security.Cryptography.X509Certificates;

namespace DoubleMark.Crpt;

/// <summary>
/// Windows certificate store adapter for <see cref="ICrptCertificateProvider"/>.
/// </summary>
public sealed class StoreCrptCertificateProvider : ICrptCertificateProvider
{
    public X509Certificate2 FindCertificate(CrptConnectionSettings settings) =>
        CrptCertificateProvider.FindCertificate(settings);

    public IReadOnlyList<CrptCertificateDescriptor> ListEligibleCertificates(string? innFilter = null) =>
        CrptCertificateProvider.ListEligibleCertificates(innFilter);
}
