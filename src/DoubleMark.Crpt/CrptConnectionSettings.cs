namespace DoubleMark.Crpt;

public sealed class CrptConnectionSettings
{
    public string Inn { get; set; } = "";
    public string TrueApiBaseUrl { get; set; } = "https://markirovka.crpt.ru/";
    public string SuzBaseUrl { get; set; } = "https://suzgrid.crpt.ru/";
    public string NkBaseUrl { get; set; } = CrptUrl.ProductionNkBaseUrl;
    public int NkHttpTimeoutSeconds { get; set; } = CrptRiskMitigations.NkHttpTimeoutSeconds;

    public string OmsId { get; set; } = "";
    public string ConnectionId { get; set; } = "";
    public string? CertificateThumbprint { get; set; }
    public string ContactPerson { get; set; } = "";
    public string ProductGroup { get; set; } = "chemistry";
    public int? TemplateId { get; set; } = 46;
    public string? TnvedCode { get; set; }
    public string? CertificateDocType { get; set; } = "CONFORMITY_DECLARATION";
    public string? CertificateDocNumber { get; set; }
    public string? CertificateDocDate { get; set; }
    public string? UtilisationProductionDate { get; set; }
    public string? UtilisationExpirationDate { get; set; }
}

/// <summary>
/// True API or SUZ auth token (spec §6.4).
/// </summary>
public sealed record CrptAuthToken(
    string Value,
    DateTimeOffset ExpiresAt,
    bool IsUnitedUuidToken);

public sealed record CrptCreateOrderResult(string OrderId, long? ExpectedCompleteTimestampMs, string RawResponse);

public sealed record CrptOrderFlowResult(string OrderId, string CodesResponse);

public sealed record CrptDocumentSubmitResult(string DocumentId, string RawResponse);
