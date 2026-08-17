using DoubleMark.Core.Crpt;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// Orchestrates NK catalog synchronization (spec §9.5.4).
/// </summary>
public interface ICrptCatalogSyncService
{
    Task<CrptCatalogSyncResult> SyncAsync(
        IProgress<CrptCatalogSyncProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
