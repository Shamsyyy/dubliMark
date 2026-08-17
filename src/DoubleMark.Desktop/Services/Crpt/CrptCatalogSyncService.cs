using System.Diagnostics;
using System.Text.Json;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// Orchestrates NK → local catalog sync (spec §9.5.4, Phase C7.1 incremental etagslist).
/// </summary>
public sealed class CrptCatalogSyncService : ICrptCatalogSyncService
{
    private const int ProductListPageSize = 1000;
    private const int FeedProductBatchSize = 25;
    private const int EtagsListPageSize = 100;
    /// <summary>Below this size, product-list full sync is faster than paginating etagslist.</summary>
    internal const int IncrementalSyncMinCatalogSize = 100;
    private static readonly TimeSpan ProductInfoDelay = TimeSpan.FromMilliseconds(200);

    private readonly ICrptNkService _nkService;
    private readonly ICrptAuthService _authService;
    private readonly ICrptSettingsStore _settingsStore;
    private readonly ICrptProductCatalogStore _catalogStore;

    public CrptCatalogSyncService(
        ICrptNkService nkService,
        ICrptAuthService authService,
        ICrptSettingsStore settingsStore,
        ICrptProductCatalogStore catalogStore)
    {
        _nkService = nkService;
        _authService = authService;
        _settingsStore = settingsStore;
        _catalogStore = catalogStore;
    }

    public async Task<CrptCatalogSyncResult> SyncAsync(
        IProgress<CrptCatalogSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.LoadSettings();
        var existing = _catalogStore.List();

        if (ShouldUseIncrementalSync(settings, existing))
        {
            try
            {
                return await SyncIncrementalAsync(settings, existing, progress, cancellationToken);
            }
            catch (Exception ex) when (IsIncrementalFallback(ex))
            {
                Trace.WriteLine($"[CrptCatalogSync] incremental fallback: {ex.Message}");
            }
        }

        return await SyncFullAsync(settings, progress, cancellationToken);
    }

    private static CrptProductCatalogItem CopyWithSyncedAt(CrptProductCatalogItem item, DateTimeOffset syncedAt) =>
        new()
        {
            Gtin = item.Gtin,
            GoodId = item.GoodId,
            Name = item.Name,
            TnvedCode = item.TnvedCode,
            TnvedGroup = item.TnvedGroup,
            ProductGroup = item.ProductGroup,
            TemplateId = item.TemplateId,
            NkStatus = item.NkStatus,
            NkStatusRaw = item.NkStatusRaw,
            NkProductState = item.NkProductState,
            NkCardType = item.NkCardType,
            NkCardStatusPrimary = item.NkCardStatusPrimary,
            NkDetailedStatuses = item.NkDetailedStatuses,
            CategoryName = item.CategoryName,
            NkCategoryId = item.NkCategoryId,
            NkUpdatedAt = item.NkUpdatedAt,
            IsSigned = item.IsSigned,
            CanOrderCodes = item.CanOrderCodes,
            CertificateDocType = item.CertificateDocType,
            CertificateDocNumber = item.CertificateDocNumber,
            CertificateDocDate = item.CertificateDocDate,
            SyncedAt = syncedAt,
            SyncError = item.SyncError,
            NkEtag = item.NkEtag,
        };

    internal static bool ShouldUseIncrementalSync(CrptSettings settings, IReadOnlyList<CrptProductCatalogItem> existing) =>
        settings.NkIncrementalSyncEnabled && existing.Count >= IncrementalSyncMinCatalogSize;

    internal static bool ShouldFetchEtagsList(int catalogItemCount) =>
        catalogItemCount >= IncrementalSyncMinCatalogSize;

    private async Task<CrptCatalogSyncResult> SyncIncrementalAsync(
        CrptSettings settings,
        IReadOnlyList<CrptProductCatalogItem> existingItems,
        IProgress<CrptCatalogSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var nkHost = RedactHost(settings.NkBaseUrl);
        var stage = "init";

        try
        {
            stage = "connectivity-check";
            progress?.Report(new CrptCatalogSyncProgress(stage, 0, 0, null));
            await CrptNkConnectivity.CheckReachableAsync(settings.NkBaseUrl, cancellationToken);

            var jwt = settings.NkUseJwtFromTrueApi
                ? await _authService.GetValidTokenAsync(cancellationToken)
                : null;

            using var nkClient = _nkService.CreateNkClient(jwt);
            using var productClient = _nkService.CreateTrueApiProductClient();

            var syncedAt = DateTimeOffset.UtcNow;
            var existingByGtin = existingItems.ToDictionary(item => item.Gtin, StringComparer.Ordinal);
            var existingByGoodId = BuildGoodIdIndex(existingItems);
            var merged = new Dictionary<string, CrptProductCatalogItem>(StringComparer.Ordinal);

            var added = 0;
            var updated = 0;
            var skipped = 0;
            var errors = 0;

            stage = "etagslist";
            var knownGoodIds = existingByGoodId.Keys.ToHashSet();
            var remoteEntries = await FetchAllEtagsAsync(
                nkClient,
                progress,
                cancellationToken,
                knownGoodIds);
            if (remoteEntries.Count == 0 && existingByGoodId.Count > 0)
                throw new InvalidOperationException("etagslist не вернул etag для известных карточек каталога.");

            var remoteEtagMap = CrptNkEtagsListDiff.BuildRemoteEtagMap(remoteEntries);
            var changedGoodIds = CrptNkEtagsListDiff.FindChangedGoodIds(remoteEntries, existingByGoodId);
            var changedGoodIdSet = changedGoodIds.ToHashSet();

            foreach (var item in existingItems)
            {
                if (item.GoodId is not int goodId)
                {
                    merged[item.Gtin] = CopyWithSyncedAt(item, syncedAt);
                    continue;
                }

                if (!changedGoodIdSet.Contains(goodId))
                {
                    var withEtag = CrptNkProductMapper.ApplyRemoteEtag(item, remoteEtagMap);
                    merged[item.Gtin] = CopyWithSyncedAt(withEtag, syncedAt);
                }
            }

            var gtinsToRefresh = new List<string>();
            var goodIdsWithoutGtin = new List<int>();

            foreach (var goodId in changedGoodIds)
            {
                if (existingByGoodId.TryGetValue(goodId, out var known))
                    gtinsToRefresh.Add(known.Gtin);
                else
                    goodIdsWithoutGtin.Add(goodId);
            }

            stage = "feed-product";
            var refreshTotal = gtinsToRefresh.Count + goodIdsWithoutGtin.Count;
            var refreshProcessed = 0;

            for (var index = 0; index < gtinsToRefresh.Count; index += FeedProductBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = gtinsToRefresh.Skip(index).Take(FeedProductBatchSize).ToList();
                progress?.Report(new CrptCatalogSyncProgress(stage, refreshProcessed, refreshTotal, batch[0]));

                try
                {
                    await RefreshFeedProductBatchAsync(
                        nkClient,
                        productClient,
                        settings,
                        jwt,
                        batch,
                        existingByGtin,
                        merged,
                        syncedAt,
                        cancellationToken);
                }
                catch
                {
                    errors += batch.Count;
                }

                refreshProcessed += batch.Count;
            }

            foreach (var goodId in goodIdsWithoutGtin)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new CrptCatalogSyncProgress(stage, refreshProcessed, refreshTotal, null));

                try
                {
                    await RefreshFeedProductByGoodIdAsync(
                        nkClient,
                        productClient,
                        settings,
                        jwt,
                        goodId,
                        existingByGtin,
                        merged,
                        syncedAt,
                        cancellationToken);
                }
                catch
                {
                    errors++;
                }

                refreshProcessed++;
            }

            stage = "apply-defaults";
            ApplyDefaultsAndPreserve(settings, existingByGtin, merged);

            foreach (var gtin in merged.Keys.ToList())
            {
                var item = merged[gtin];
                if (item.GoodId is int goodId)
                    item = CrptNkProductMapper.ApplyRemoteEtag(item, remoteEtagMap);

                if (existingByGtin.TryGetValue(gtin, out var previous))
                {
                    if (CatalogItemChanged(previous, item))
                        updated++;
                    else
                        skipped++;
                }
                else
                {
                    added++;
                }

                merged[gtin] = item;
            }

            _catalogStore.Save(merged.Values.ToList());
            UpdateDiscoveredCategories(settings, merged.Values);

            progress?.Report(new CrptCatalogSyncProgress("complete", merged.Count, merged.Count, null));
            return new CrptCatalogSyncResult(
                added,
                updated,
                skipped,
                errors,
                remoteEntries.Count);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[CrptCatalogSync] stage={stage} host={nkHost} error={ex.Message}");
            throw WrapSyncFailure(ex, stage, nkHost, settings);
        }
    }

    private async Task<CrptCatalogSyncResult> SyncFullAsync(
        CrptSettings settings,
        IProgress<CrptCatalogSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var nkHost = RedactHost(settings.NkBaseUrl);
        var stage = "init";

        try
        {
            stage = "connectivity-check";
            progress?.Report(new CrptCatalogSyncProgress(stage, 0, 0, null));
            await CrptNkConnectivity.CheckReachableAsync(settings.NkBaseUrl, cancellationToken);

            var jwt = settings.NkUseJwtFromTrueApi
                ? await _authService.GetValidTokenAsync(cancellationToken)
                : null;

            using var nkClient = _nkService.CreateNkClient(jwt);
            using var productClient = _nkService.CreateTrueApiProductClient();

            var syncedAt = DateTimeOffset.UtcNow;
            var existing = _catalogStore.List().ToDictionary(item => item.Gtin, StringComparer.Ordinal);
            var merged = new Dictionary<string, CrptProductCatalogItem>(StringComparer.Ordinal);

            var added = 0;
            var updated = 0;
            var skipped = 0;
            var errors = 0;
            var listedInNk = 0;

            var offset = 0;
            var total = int.MaxValue;
            var processed = 0;

            stage = "product-list";
            while (offset < total)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var listJson = await GetProductListWithRetryAsync(
                    nkClient,
                    settings,
                    offset,
                    cancellationToken);

                var (goods, pageTotal) = CrptNkProductMapper.ParseProductListResponse(listJson);
                total = pageTotal;
                progress?.Report(new CrptCatalogSyncProgress(stage, processed, total, null));

                if (offset == 0 && total == 0)
                    throw BuildEmptyProductListException(settings, nkHost);

                foreach (var good in goods)
                {
                    listedInNk++;
                    try
                    {
                        var item = CrptNkProductMapper.MapProductListEntry(good, syncedAt);
                        merged[item.Gtin] = item;
                    }
                    catch
                    {
                        errors++;
                    }
                }

                processed += goods.Count;
                offset += ProductListPageSize;

                if (goods.Count == 0)
                    break;
            }

            var gtins = merged.Keys.ToList();
            stage = "feed-product";
            for (var index = 0; index < gtins.Count; index += FeedProductBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = gtins.Skip(index).Take(FeedProductBatchSize).ToList();
                progress?.Report(new CrptCatalogSyncProgress(stage, index, gtins.Count, batch[0]));

                try
                {
                    var feedJson = await GetFeedProductWithRetryAsync(nkClient, batch, cancellationToken);
                    var feedEntries = CrptNkProductMapper.ParseFeedProductEntries(feedJson);

                    foreach (var feedEntry in feedEntries)
                    {
                        var gtin = ReadGtin(feedEntry);
                        if (gtin is null || !merged.TryGetValue(gtin, out var baseline))
                            continue;

                        string? productGroup = null;
                        if (settings.NkUseJwtFromTrueApi && jwt is not null)
                        {
                            stage = "product-info";
                            await Task.Delay(ProductInfoDelay, cancellationToken);
                            var infoJson = await GetProductInfoWithRetryAsync(productClient, jwt, [gtin], cancellationToken);
                            productGroup = CrptNkProductMapper.ReadProductGroupFromInfoResponse(infoJson, gtin);
                        }

                        var templateId = settings.ResolveTemplateId(productGroup);
                        merged[gtin] = CrptNkProductMapper.MergeFeedProduct(
                            baseline,
                            feedEntry,
                            productGroup,
                            templateId,
                            syncedAt);
                    }
                }
                catch
                {
                    errors += batch.Count;
                }
            }

            if (ShouldFetchEtagsList(merged.Count))
            {
                stage = "etagslist";
                try
                {
                    var remoteEntries = await FetchAllEtagsAsync(nkClient, progress, cancellationToken);
                    var remoteEtagMap = CrptNkEtagsListDiff.BuildRemoteEtagMap(remoteEntries);
                    foreach (var gtin in merged.Keys.ToList())
                        merged[gtin] = CrptNkProductMapper.ApplyRemoteEtag(merged[gtin], remoteEtagMap);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[CrptCatalogSync] etagslist after full sync skipped: {ex.Message}");
                }
            }

            ApplyDefaultsAndPreserve(settings, existing, merged);

            foreach (var item in merged.Values)
            {
                if (existing.TryGetValue(item.Gtin, out var previous))
                {
                    if (CatalogItemChanged(previous, item))
                        updated++;
                    else
                        skipped++;
                }
                else
                {
                    added++;
                }
            }

            _catalogStore.Save(merged.Values.ToList());
            UpdateDiscoveredCategories(settings, merged.Values);

            progress?.Report(new CrptCatalogSyncProgress("complete", gtins.Count, gtins.Count, null));
            return new CrptCatalogSyncResult(
                added,
                updated,
                skipped,
                errors,
                listedInNk);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[CrptCatalogSync] stage={stage} host={nkHost} error={ex.Message}");
            throw WrapSyncFailure(ex, stage, nkHost, settings);
        }
    }

    private async Task<List<CrptNkEtagsListDiff.EtagsListEntry>> FetchAllEtagsAsync(
        CrptNkClient nkClient,
        IProgress<CrptCatalogSyncProgress>? progress,
        CancellationToken cancellationToken,
        IReadOnlySet<int>? limitToGoodIds = null)
    {
        var allEntries = new List<CrptNkEtagsListDiff.EtagsListEntry>();
        var offset = 0;
        var scanned = 0;
        int? totalFromApi = null;
        HashSet<int>? foundGoodIds = limitToGoodIds is not null ? [] : null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = await ExecuteWithRetryAsync(
                () => nkClient.GetEtagsListAsync(offset, cancellationToken),
                cancellationToken);
            var page = CrptNkProductMapper.ParseEtagsListResponse(json);

            if (page.Entries.Count == 0)
                break;

            if (totalFromApi is null && page.GoodsCount > 0)
                totalFromApi = page.GoodsCount;

            foreach (var entry in page.Entries)
            {
                if (limitToGoodIds is not null && !limitToGoodIds.Contains(entry.GoodId))
                    continue;

                allEntries.Add(entry);
                foundGoodIds?.Add(entry.GoodId);
            }

            scanned += page.Entries.Count;
            var progressTotal = limitToGoodIds is not null
                ? limitToGoodIds.Count
                : totalFromApi ?? scanned;
            var progressProcessed = limitToGoodIds is not null
                ? foundGoodIds!.Count
                : scanned;
            progress?.Report(new CrptCatalogSyncProgress("etagslist", progressProcessed, progressTotal, null));

            if (foundGoodIds is not null && foundGoodIds.Count >= limitToGoodIds!.Count)
                break;

            if (page.Entries.Count < EtagsListPageSize)
                break;

            offset = page.LastProductNumber ?? offset + page.Entries.Count;
        }

        return allEntries;
    }

    private async Task RefreshFeedProductBatchAsync(
        CrptNkClient nkClient,
        CrptTrueApiProductClient productClient,
        CrptSettings settings,
        string? jwt,
        IReadOnlyList<string> gtins,
        IReadOnlyDictionary<string, CrptProductCatalogItem> existingByGtin,
        Dictionary<string, CrptProductCatalogItem> merged,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken)
    {
        var feedJson = await GetFeedProductWithRetryAsync(nkClient, gtins, cancellationToken);
        var feedEntries = CrptNkProductMapper.ParseFeedProductEntries(feedJson);

        foreach (var feedEntry in feedEntries)
        {
            var gtin = ReadGtin(feedEntry);
            if (gtin is null)
                continue;

            var baseline = existingByGtin.TryGetValue(gtin, out var previous)
                ? previous
                : CrptNkProductMapper.MapFeedProductEntry(feedEntry, syncedAt);

            string? productGroup = null;
            if (settings.NkUseJwtFromTrueApi && jwt is not null)
            {
                await Task.Delay(ProductInfoDelay, cancellationToken);
                var infoJson = await GetProductInfoWithRetryAsync(productClient, jwt, [gtin], cancellationToken);
                productGroup = CrptNkProductMapper.ReadProductGroupFromInfoResponse(infoJson, gtin);
            }

            var templateId = settings.ResolveTemplateId(productGroup);
            merged[gtin] = CrptNkProductMapper.MergeFeedProduct(
                baseline,
                feedEntry,
                productGroup,
                templateId,
                syncedAt);
        }
    }

    private async Task RefreshFeedProductByGoodIdAsync(
        CrptNkClient nkClient,
        CrptTrueApiProductClient productClient,
        CrptSettings settings,
        string? jwt,
        int goodId,
        IReadOnlyDictionary<string, CrptProductCatalogItem> existingByGtin,
        Dictionary<string, CrptProductCatalogItem> merged,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken)
    {
        var feedJson = await ExecuteWithRetryAsync(
            () => nkClient.GetFeedProductByGoodIdAsync(goodId, cancellationToken),
            cancellationToken);
        var feedEntries = CrptNkProductMapper.ParseFeedProductEntries(feedJson);

        foreach (var feedEntry in feedEntries)
        {
            var gtin = ReadGtin(feedEntry);
            if (gtin is null)
                continue;

            var baseline = existingByGtin.TryGetValue(gtin, out var previous)
                ? previous
                : CrptNkProductMapper.MapFeedProductEntry(feedEntry, syncedAt);

            string? productGroup = null;
            if (settings.NkUseJwtFromTrueApi && jwt is not null)
            {
                await Task.Delay(ProductInfoDelay, cancellationToken);
                var infoJson = await GetProductInfoWithRetryAsync(productClient, jwt, [gtin], cancellationToken);
                productGroup = CrptNkProductMapper.ReadProductGroupFromInfoResponse(infoJson, gtin);
            }

            var templateId = settings.ResolveTemplateId(productGroup);
            merged[gtin] = CrptNkProductMapper.MergeFeedProduct(
                baseline,
                feedEntry,
                productGroup,
                templateId,
                syncedAt);
        }
    }

    private void ApplyDefaultsAndPreserve(
        CrptSettings settings,
        IReadOnlyDictionary<string, CrptProductCatalogItem> existing,
        Dictionary<string, CrptProductCatalogItem> merged)
    {
        var defaultGroup = settings.PrimaryProductGroup;
        var defaultTemplateId = settings.ResolveTemplateId(defaultGroup);

        foreach (var gtin in merged.Keys.ToList())
        {
            merged[gtin] = CrptNkProductMapper.ApplyCatalogDefaults(
                merged[gtin],
                defaultGroup,
                defaultTemplateId);

            if (existing.TryGetValue(gtin, out var previous))
                merged[gtin] = CrptNkProductMapper.PreservePreviousCatalogFields(merged[gtin], previous);
        }
    }

    private void UpdateDiscoveredCategories(CrptSettings settings, IEnumerable<CrptProductCatalogItem> items)
    {
        var secrets = _settingsStore.LoadSecrets();
        var discovered = CrptNkCategoryDiscovery.CollectCategoryNames(items);
        var mergedKnown = CrptNkCategoryDiscovery.MergeKnownCategories(settings.NkKnownCategories, discovered);
        if (!mergedKnown.SequenceEqual(settings.NkKnownCategories, StringComparer.Ordinal))
        {
            settings.NkKnownCategories = mergedKnown;
            _settingsStore.Save(settings, secrets);
        }
    }

    private static Dictionary<int, CrptProductCatalogItem> BuildGoodIdIndex(IReadOnlyList<CrptProductCatalogItem> items)
    {
        var index = new Dictionary<int, CrptProductCatalogItem>();
        foreach (var item in items)
        {
            if (item.GoodId is int goodId)
                index[goodId] = item;
        }

        return index;
    }

    private static bool IsIncrementalFallback(Exception ex) =>
        ex is InvalidOperationException io && (
            IsNkConnectionMessage(io.Message)
            || io.Message.Contains("0 карточек", StringComparison.Ordinal)
            || io.Message.Contains("etagslist", StringComparison.OrdinalIgnoreCase));

    internal static InvalidOperationException BuildEmptyProductListException(CrptSettings settings, string redactedHost)
    {
        var contour = settings.Environment == CrptEnvironment.Production
            ? "Production"
            : "Sandbox";
        var authHint = settings.NkUseJwtFromTrueApi
            ? "Проверьте ИНН, выбранный УКЭП и кнопку «Сохранить» в настройках маркировки."
            : "Проверьте API KEY NK и кнопку «Сохранить» в настройках маркировки.";

        return new InvalidOperationException(
            $"NK ({contour}, хост {redactedHost}) вернул 0 карточек. {authHint} " +
            "Убедитесь, что контур совпадает с картами в личном кабинете НК (промышленный vs sandbox).");
    }

    internal static InvalidOperationException WrapSyncFailure(
        Exception ex,
        string stage,
        string redactedHost,
        CrptSettings settings)
    {
        if (ex is InvalidOperationException io && IsNkConnectionMessage(io.Message))
            return io;

        var authHint = settings.NkUseJwtFromTrueApi
            ? "Аутентификация: JWT из True API (УКЭП)."
            : "Аутентификация: API KEY в настройках NK (без JWT).";

        return new InvalidOperationException(
            $"Синхронизация каталога NK не удалась на этапе «{stage}» (хост {redactedHost}). {authHint} {ex.Message}",
            ex);
    }

    private static bool IsNkConnectionMessage(string message) =>
        message.Contains("Националь", StringComparison.Ordinal) ||
        message.Contains("Порт 443", StringComparison.Ordinal) ||
        message.Contains("Таймаут подключения", StringComparison.Ordinal);

    internal static string RedactHost(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return "[invalid-url]";

        var host = uri.Host;
        if (host.Length <= 12)
            return host;

        return host[..6] + "…" + host[^6..];
    }

    private static bool CatalogItemChanged(CrptProductCatalogItem previous, CrptProductCatalogItem current) =>
        previous.Name != current.Name
        || previous.TnvedCode != current.TnvedCode
        || previous.ProductGroup != current.ProductGroup
        || previous.TemplateId != current.TemplateId
        || previous.CanOrderCodes != current.CanOrderCodes
        || previous.NkStatus != current.NkStatus
        || previous.IsSigned != current.IsSigned
        || previous.NkProductState != current.NkProductState
        || previous.NkCardType != current.NkCardType
        || previous.NkCardStatusPrimary != current.NkCardStatusPrimary
        || !previous.NkDetailedStatuses.SequenceEqual(current.NkDetailedStatuses)
        || previous.CategoryName != current.CategoryName
        || previous.NkCategoryId != current.NkCategoryId
        || previous.NkUpdatedAt != current.NkUpdatedAt
        || previous.NkEtag != current.NkEtag;

    private static string? ReadGtin(JsonElement element) => CrptNkProductMapper.ReadGtin(element);

    private static async Task<string> GetProductListWithRetryAsync(
        CrptNkClient client,
        CrptSettings settings,
        int offset,
        CancellationToken cancellationToken)
    {
        var fromDate = CrptNkProductMapper.DefaultProductListFromDate;
        var toDate = CrptNkProductMapper.DefaultProductListToDate();
        return await ExecuteWithRetryAsync(
            () => client.GetProductListAsync(ProductListPageSize, offset, goodStatus: null, fromDate, toDate, cancellationToken),
            cancellationToken);
    }

    private static async Task<string> GetFeedProductWithRetryAsync(
        CrptNkClient client,
        IReadOnlyList<string> gtins,
        CancellationToken cancellationToken) =>
        await ExecuteWithRetryAsync(() => client.GetFeedProductAsync(gtins, cancellationToken), cancellationToken);

    private static async Task<string> GetProductInfoWithRetryAsync(
        CrptTrueApiProductClient client,
        string jwt,
        IReadOnlyList<string> gtins,
        CancellationToken cancellationToken) =>
        await ExecuteWithRetryAsync(() => client.GetProductInfoAsync(jwt, gtins, cancellationToken), cancellationToken);

    private static async Task<string> ExecuteWithRetryAsync(
        Func<Task<string>> action,
        CancellationToken cancellationToken,
        int maxAttempts = 4)
    {
        var delay = TimeSpan.FromSeconds(1);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (InvalidOperationException ex) when (attempt < maxAttempts && IsRetryable(ex))
            {
                await Task.Delay(delay, cancellationToken);
                delay += delay;
            }
        }

        return await action();
    }

    private static bool IsRetryable(Exception ex) =>
        IsRateLimited(ex) || IsConnectionFailure(ex);

    private static bool IsConnectionFailure(Exception ex) =>
        ex is InvalidOperationException &&
        (ex.Message.Contains("Таймаут подключения", StringComparison.Ordinal) ||
         ex.Message.Contains("Порт 443", StringComparison.Ordinal) ||
         ex.Message.Contains("Не удалось подключиться", StringComparison.Ordinal) ||
         ex.Message.Contains("Сетевая ошибка", StringComparison.Ordinal));

    private static bool IsRateLimited(Exception ex) =>
        ex is CrptApiException api && api.IsRateLimited
        || ex.Message.Contains("429", StringComparison.Ordinal)
        || ex.Message.Contains("Too Many", StringComparison.OrdinalIgnoreCase);
}
