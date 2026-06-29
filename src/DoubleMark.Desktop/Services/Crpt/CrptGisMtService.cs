using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// GIS MT utilisation orchestration for Desktop (spec §10).
/// </summary>
public sealed class CrptGisMtService : ICrptGisMtService, IDisposable
{
    private readonly ICrptAuthService _authService;
    private readonly ICrptSettingsStore _settingsStore;
    private readonly ICrptCertificateProvider _certificateProvider;
    private readonly ICrptProductCatalogStore _catalogStore;
    private readonly CrptOrderRepository _orderRepository;
    private readonly Func<CrptConnectionSettings, CrptGisMtClient> _gisMtClientFactory;

    public CrptGisMtService(
        ICrptAuthService authService,
        ICrptSettingsStore settingsStore,
        ICrptCertificateProvider certificateProvider,
        ICrptProductCatalogStore catalogStore,
        CrptOrderRepository orderRepository,
        Func<CrptConnectionSettings, CrptGisMtClient>? gisMtClientFactory = null)
    {
        _authService = authService;
        _settingsStore = settingsStore;
        _certificateProvider = certificateProvider;
        _catalogStore = catalogStore;
        _orderRepository = orderRepository;
        _gisMtClientFactory = gisMtClientFactory ?? (connection => new CrptGisMtClient(connection));
    }

    public Task<CrptUtilisationSubmitResult> SendUtilisationForOrderAsync(
        string orderLocalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderLocalId);
        return SendUtilisationCoreAsync(
            async ct =>
            {
                var order = await _orderRepository.GetByLocalIdAsync(orderLocalId, ct);
                if (order is null)
                    throw new CrptGisMtException($"Order '{orderLocalId}' was not found.");

                var codes = await _orderRepository.ListCodesByOrderAsync(orderLocalId, ct);
                var printed = codes.Where(c => c.Status == CrptCodeLifecycleStatus.Printed).ToList();
                if (printed.Count == 0)
                    throw new CrptGisMtException($"Order '{orderLocalId}' has no printed codes for utilisation.");

                return (order.ProductGroup, order.Gtin, printed);
            },
            cancellationToken);
    }

    public Task<CrptUtilisationSubmitResult> SendUtilisationForCodesAsync(
        IReadOnlyList<int> codeIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codeIds);
        if (codeIds.Count == 0)
            throw new ArgumentException("At least one code id is required.", nameof(codeIds));

        return SendUtilisationCoreAsync(
            async ct =>
            {
                var codes = await _orderRepository.GetCodesByIdsAsync(codeIds, ct);
                if (codes.Count != codeIds.Count)
                {
                    var missing = codeIds.Except(codes.Select(c => c.Id)).ToList();
                    throw new CrptGisMtException($"Marking codes not found: {string.Join(", ", missing)}.");
                }

                EnsureAllPrinted(codes);

                var order = await _orderRepository.GetByLocalIdAsync(codes[0].OrderLocalId, ct);
                if (order is null)
                    throw new CrptGisMtException($"Order '{codes[0].OrderLocalId}' was not found.");

                if (codes.Any(c => c.OrderLocalId != order.LocalId))
                    throw new CrptGisMtException("All code ids must belong to the same order.");

                return (order.ProductGroup, order.Gtin, codes);
            },
            cancellationToken);
    }

    public Task<string> IntroduceGoodsAsync(
        IReadOnlyList<string> markingCodes,
        CancellationToken cancellationToken = default)
    {
        _ = markingCodes;
        _ = cancellationToken;

        if (CrptMvpScope.IsPhase2(CrptIntegrationFeature.IntroduceToCirculation))
        {
            throw new NotImplementedException(
                "LP_INTRODUCE_GOODS orchestration is phase 2 (spec §10.2). Client method exists for probe only.");
        }

        throw new NotImplementedException(
            $"{CrptIntegrationFeature.IntroduceToCirculation} is out of MVP scope.");
    }

    private async Task<CrptUtilisationSubmitResult> SendUtilisationCoreAsync(
        Func<CancellationToken, Task<(string ProductGroup, string Gtin, IReadOnlyList<CrptMarkingCodeItem> Codes)>> loadCodes,
        CancellationToken cancellationToken)
    {
        var (productGroup, gtin, codes) = await loadCodes(cancellationToken);
        EnsureAllPrinted(codes);

        var catalogItem = _catalogStore.List()
            .FirstOrDefault(i => string.Equals(i.Gtin, gtin, StringComparison.Ordinal));
        if (catalogItem is null)
            throw new CrptGisMtException($"Catalog item for GTIN '{gtin}' was not found.");

        var settings = _settingsStore.LoadSettings();
        var secrets = _settingsStore.LoadSecrets();
        var connection = CrptConnectionSettingsBridge.ToConnectionSettings(settings, secrets, productGroup);
        var certificate = _certificateProvider.FindCertificate(connection);
        var jwtToken = await _authService.GetValidTokenAsync(cancellationToken);

        var request = CrptUtilisationBuilder.BuildRequest(
            catalogItem,
            productGroup,
            codes.Select(c => c.RawPayload).ToList(),
            connection);

        using var client = _gisMtClientFactory(connection);
        var submitResult = await client.SendUtilisationAsync(jwtToken, certificate, request, cancellationToken);

        foreach (var code in codes)
        {
            if (!CrptCodeLifecycleTransitions.CanTransition(code.Status, CrptCodeLifecycleStatus.UtilisationSent))
            {
                throw new CrptGisMtException(
                    $"Code id {code.Id} cannot transition from {code.Status} to UtilisationSent.");
            }

            await _orderRepository.UpdateCodeAsync(
                code with { Status = CrptCodeLifecycleStatus.UtilisationSent },
                cancellationToken);
        }

        return new CrptUtilisationSubmitResult(submitResult.DocumentId, codes.Count);
    }

    private static void EnsureAllPrinted(IReadOnlyList<CrptMarkingCodeItem> codes)
    {
        var invalidIds = codes
            .Where(c => c.Status != CrptCodeLifecycleStatus.Printed)
            .Select(c => c.Id)
            .ToList();

        if (invalidIds.Count == 0)
            return;

        throw new CrptGisMtException(
            $"Only printed codes can be sent for utilisation. Invalid code ids: {string.Join(", ", invalidIds)}.");
    }

    public void Dispose()
    {
    }
}
