namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// GIS MT document submission facade (spec §4.1, §10).
/// </summary>
public interface ICrptGisMtService
{
    Task<CrptUtilisationSubmitResult> SendUtilisationForOrderAsync(
        string orderLocalId,
        CancellationToken cancellationToken = default);

    Task<CrptUtilisationSubmitResult> SendUtilisationForCodesAsync(
        IReadOnlyList<int> codeIds,
        CancellationToken cancellationToken = default);

    /// <summary>Phase 2 — LP_INTRODUCE_GOODS (spec §10.2).</summary>
    Task<string> IntroduceGoodsAsync(
        IReadOnlyList<string> markingCodes,
        CancellationToken cancellationToken = default);
}

public sealed record CrptUtilisationSubmitResult(string DocumentId, int CodesSubmitted);
