namespace DoubleMark.Crpt;

/// <summary>
/// Utilisation report payload for GIS MT / True API (spec §10.1).
/// </summary>
public sealed record UtilisationReportRequest
{
    public required string ProductGroup { get; init; }
    public required IReadOnlyList<string> RawPayloads { get; init; }
    public string? ProductionDate { get; init; }
    public string? ExpirationDate { get; init; }
    public string? CertificateDocType { get; init; }
    public string? CertificateDocNumber { get; init; }
    public string? CertificateDocDate { get; init; }
}

/// <summary>
/// LP_INTRODUCE_GOODS document skeleton (spec §10.2, phase 2).
/// </summary>
public sealed record IntroduceGoodsDocument
{
    public required string DocumentFormat { get; init; }
    public required string ProductGroup { get; init; }
    public required IReadOnlyList<string> Codes { get; init; }
    public DateOnly? ProductionDate { get; init; }
}

public sealed class CrptGisMtException : Exception
{
    public CrptGisMtException(string message) : base(message)
    {
    }
}
