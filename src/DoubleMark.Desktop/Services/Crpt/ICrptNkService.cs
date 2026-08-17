using DoubleMark.Crpt;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// National catalog (NK) API facade (spec §5, §9.5).
/// </summary>
public interface ICrptNkService
{
    CrptNkClient CreateNkClient(string? bearerToken = null);

    CrptTrueApiProductClient CreateTrueApiProductClient();
}
