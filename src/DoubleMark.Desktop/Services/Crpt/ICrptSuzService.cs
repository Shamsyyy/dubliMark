using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// SUZ order orchestration facade (spec §9).
/// </summary>
public interface ICrptSuzService
{
    Task<string> CreateOrderAsync(
        string gtin,
        int quantity,
        string productGroup,
        int? templateId = null,
        CancellationToken cancellationToken = default);

    Task<SuzOrderRemoteStatus> PollUntilReadyAsync(
        string remoteOrderId,
        string gtin,
        TimeSpan? timeout = null,
        IProgress<SuzOrderProgress>? progress = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CrptMarkingCodeItem>> DownloadCodesAsync(
        string localOrderId,
        string remoteOrderId,
        string gtin,
        int quantity,
        CancellationToken cancellationToken = default);

    Task CloseOrderAsync(string remoteOrderId, CancellationToken cancellationToken = default);

    Task<CrptSuzOrder> CreateAndDownloadOrderAsync(
        string gtin,
        int quantity,
        string productGroup,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
