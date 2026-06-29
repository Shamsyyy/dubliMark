namespace DoubleMark.Core.Crpt;

/// <summary>
/// Local product catalog entry synced from NK + True API (spec §6.2).
/// </summary>
public sealed class CrptProductCatalogItem
{
    public required string Gtin { get; init; }
    public int? GoodId { get; init; }
    public string Name { get; init; } = "";
    public string? TnvedCode { get; init; }
    public string? TnvedGroup { get; init; }
    public string? ProductGroup { get; init; }
    public int? TemplateId { get; init; }
    public string NkStatus { get; init; } = "";
    public NkProductState NkProductState { get; init; }
    public NkCardType NkCardType { get; init; }
    public string NkCardStatusPrimary { get; init; } = "";
    public string[] NkDetailedStatuses { get; init; } = [];
    public string? CategoryName { get; init; }
    public int? NkCategoryId { get; init; }
    public DateTimeOffset? NkUpdatedAt { get; init; }
    public string? NkStatusRaw { get; init; }
    public bool IsSigned { get; init; }
    public bool CanOrderCodes { get; init; }
    public string? CertificateDocType { get; init; }
    public string? CertificateDocNumber { get; init; }
    public string? CertificateDocDate { get; init; }
    public DateTimeOffset SyncedAt { get; init; }
    public string? SyncError { get; init; }
    /// <summary>NK content hash from <c>GET /v3/etagslist</c> for incremental sync.</summary>
    public string? NkEtag { get; init; }
}

/// <summary>
/// Progress reported during NK catalog sync (spec §6.2).
/// </summary>
public sealed record CrptCatalogSyncProgress(
    string Stage,
    int Processed,
    int Total,
    string? CurrentGtin);

public sealed record CrptCatalogSyncResult(
    int Added,
    int Updated,
    int Skipped,
    int Errors,
    int ListedInNk = 0,
    int FilteredBySettings = 0,
    int FilteredByPublished = 0,
    int FilteredBySigned = 0);
