using System.Globalization;
using System.Text.Json;
using DoubleMark.Core.Crpt;

namespace DoubleMark.Crpt;

/// <summary>
/// Maps NK product-list and feed-product JSON to <see cref="CrptProductCatalogItem"/> (spec §9.5.3).
/// </summary>
public static class CrptNkProductMapper
{
    public const int TnvedAttributeId = 13933;

    private static readonly string[] CardStatusPrimaryPriority =
        ["errors", "moderation", "notsigned", "draft", "published"];

    /// <summary>
    /// NK §3.1.4: without both <c>from_date</c> and <c>to_date</c>, only cards updated in the last month are returned.
    /// With both set, the range may exceed one month (full catalog sync).
    /// </summary>
    public const string DefaultProductListFromDate = "2020-01-01 00:00:00";

    public static string DefaultProductListToDate() =>
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    public static CrptProductCatalogItem MapProductListEntry(JsonElement good, DateTimeOffset syncedAt)
    {
        var gtin = ReadGtin(good) ?? throw new InvalidOperationException("product-list entry missing gtin");
        var statusRaw = ReadString(good, "good_status");
        var detailedStatuses = ReadStringArray(good, "good_detailed_status");
        var status = ResolveNkStatus(good);
        var isSigned = ResolveIsSigned(good);
        var productState = MapProductState(good);
        var cardType = MapCardType(good);
        var cardStatusPrimary = MapCardStatusPrimary(detailedStatuses);

        return new CrptProductCatalogItem
        {
            Gtin = gtin,
            GoodId = ReadInt(good, "good_id"),
            Name = ReadString(good, "good_name") ?? "",
            TnvedGroup = ReadString(good, "tnved"),
            NkStatus = status,
            NkStatusRaw = statusRaw,
            NkProductState = productState,
            NkCardType = cardType,
            NkCardStatusPrimary = cardStatusPrimary,
            NkDetailedStatuses = detailedStatuses.ToArray(),
            CategoryName = NormalizeCategoryName(ReadCategoryName(good)),
            NkCategoryId = ReadNkCategoryId(good),
            NkUpdatedAt = ParseNkUpdatedAt(good),
            IsSigned = isSigned,
            CanOrderCodes = ComputeCanOrderCodes(status, isSigned, productGroup: null, cardType),
            SyncedAt = syncedAt,
        };
    }

    public static CrptProductCatalogItem MergeFeedProduct(
        CrptProductCatalogItem baseline,
        JsonElement feedEntry,
        string? productGroup,
        int? templateId,
        DateTimeOffset syncedAt)
    {
        var statusRaw = ReadString(feedEntry, "good_status") ?? baseline.NkStatusRaw;
        var detailedStatuses = ReadStringArray(feedEntry, "good_detailed_status");
        if (detailedStatuses.Count == 0 && baseline.NkDetailedStatuses.Length > 0)
            detailedStatuses = baseline.NkDetailedStatuses.ToList();

        var status = ResolveNkStatus(feedEntry, baseline.NkStatus);
        var isSigned = ResolveIsSigned(feedEntry, baseline.IsSigned);
        var productState = MapProductState(feedEntry);
        if (productState == NkProductState.Unknown)
            productState = baseline.NkProductState;

        var cardType = MapCardType(feedEntry);
        if (cardType == NkCardType.Unknown)
            cardType = baseline.NkCardType;

        var cardStatusPrimary = MapCardStatusPrimary(detailedStatuses);
        if (string.IsNullOrWhiteSpace(cardStatusPrimary))
            cardStatusPrimary = baseline.NkCardStatusPrimary;

        var categoryName = NormalizeCategoryName(ReadCategoryName(feedEntry));
        if (string.IsNullOrWhiteSpace(categoryName))
            categoryName = NormalizeCategoryName(baseline.CategoryName);

        var nkCategoryId = ReadNkCategoryId(feedEntry) ?? baseline.NkCategoryId;

        var nkUpdatedAt = ParseNkUpdatedAt(feedEntry) ?? baseline.NkUpdatedAt;
        var tnvedCode = ReadTnvedCode(feedEntry) ?? baseline.TnvedCode;
        var certificate = ReadCertificate(feedEntry);
        var resolvedGroup = productGroup ?? baseline.ProductGroup;

        return new CrptProductCatalogItem
        {
            Gtin = baseline.Gtin,
            GoodId = ReadInt(feedEntry, "good_id") ?? baseline.GoodId,
            Name = ReadString(feedEntry, "good_name") ?? baseline.Name,
            TnvedCode = tnvedCode,
            TnvedGroup = ReadString(feedEntry, "tnved") ?? baseline.TnvedGroup,
            ProductGroup = resolvedGroup,
            TemplateId = templateId ?? baseline.TemplateId,
            NkStatus = status,
            NkStatusRaw = statusRaw,
            NkProductState = productState,
            NkCardType = cardType,
            NkCardStatusPrimary = cardStatusPrimary,
            NkDetailedStatuses = detailedStatuses.ToArray(),
            CategoryName = categoryName,
            NkCategoryId = nkCategoryId,
            NkUpdatedAt = nkUpdatedAt,
            IsSigned = isSigned,
            CanOrderCodes = ComputeCanOrderCodes(status, isSigned, resolvedGroup, cardType),
            CertificateDocType = certificate?.DocType ?? baseline.CertificateDocType,
            CertificateDocNumber = certificate?.DocNumber ?? baseline.CertificateDocNumber,
            CertificateDocDate = certificate?.DocDate ?? baseline.CertificateDocDate,
            SyncedAt = syncedAt,
            SyncError = baseline.SyncError,
            NkEtag = baseline.NkEtag,
        };
    }

    public static CrptProductCatalogItem MapFeedProductEntry(JsonElement feedEntry, DateTimeOffset syncedAt)
    {
        var gtin = ReadGtin(feedEntry) ?? throw new InvalidOperationException("feed-product entry missing gtin");
        var statusRaw = ReadString(feedEntry, "good_status");
        var detailedStatuses = ReadStringArray(feedEntry, "good_detailed_status");
        var status = ResolveNkStatus(feedEntry);
        var isSigned = ResolveIsSigned(feedEntry);
        var productState = MapProductState(feedEntry);
        var cardType = MapCardType(feedEntry);
        var cardStatusPrimary = MapCardStatusPrimary(detailedStatuses);

        return new CrptProductCatalogItem
        {
            Gtin = gtin,
            GoodId = ReadInt(feedEntry, "good_id"),
            Name = ReadString(feedEntry, "good_name") ?? "",
            TnvedCode = ReadTnvedCode(feedEntry),
            TnvedGroup = ReadString(feedEntry, "tnved"),
            NkStatus = status,
            NkStatusRaw = statusRaw,
            NkProductState = productState,
            NkCardType = cardType,
            NkCardStatusPrimary = cardStatusPrimary,
            NkDetailedStatuses = detailedStatuses.ToArray(),
            CategoryName = NormalizeCategoryName(ReadCategoryName(feedEntry)),
            NkCategoryId = ReadNkCategoryId(feedEntry),
            NkUpdatedAt = ParseNkUpdatedAt(feedEntry),
            IsSigned = isSigned,
            CanOrderCodes = ComputeCanOrderCodes(status, isSigned, productGroup: null, cardType),
            SyncedAt = syncedAt,
        };
    }

    public static string? ReadProductGroupFromInfoResponse(string json, string gtin)
    {
        using var doc = JsonDocument.Parse(json);
        return ReadProductGroupFromInfoElement(doc.RootElement, gtin);
    }

    public static string? ReadProductGroupFromInfoElement(JsonElement root, string gtin)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in root.EnumerateArray())
        {
            var itemGtin = ReadString(item, "gtin");
            if (!string.Equals(itemGtin, gtin, StringComparison.Ordinal))
                continue;

            return ReadString(item, "productGroup")
                ?? ReadString(item, "product_group");
        }

        return null;
    }

    public static bool ComputeCanOrderCodes(string? nkStatus, bool isSigned, string? productGroup, NkCardType cardType)
    {
        if (cardType != NkCardType.TradeUnit)
            return false;

        var published = string.Equals(nkStatus, "published", StringComparison.OrdinalIgnoreCase);
        var groupKnown = CrptProductGroup.IsKnown(productGroup);
        return published && isSigned && groupKnown;
    }

    /// <summary>
    /// Keeps category from a previous catalog entry when a re-sync returns no category from NK.
    /// </summary>
    public static CrptProductCatalogItem PreservePreviousCatalogFields(
        CrptProductCatalogItem current,
        CrptProductCatalogItem? previous)
    {
        if (previous is null)
            return current;

        if (!string.IsNullOrWhiteSpace(current.CategoryName))
            return current;

        if (string.IsNullOrWhiteSpace(previous.CategoryName))
            return current;

        return CopyCatalogItem(
            current,
            categoryName: NormalizeCategoryName(previous.CategoryName),
            nkCategoryId: previous.NkCategoryId);
    }

    public static NkProductState MapProductState(JsonElement element)
    {
        if (IsPublishedInNkLk(element))
            return NkProductState.Published;

        var status = ReadString(element, "good_status");
        var detailed = ReadStringArray(element, "good_detailed_status");
        return MapProductStateFromTokens(status, detailed);
    }

    public static NkCardType MapCardType(JsonElement element)
    {
        if (ReadBoolFlexible(element, "is_set") == true)
            return NkCardType.Set;

        if (ReadBoolFlexible(element, "is_kit") == true)
            return NkCardType.Kit;

        if (HasTradeUnitLevel(element))
            return NkCardType.TradeUnit;

        if (ReadBoolFlexible(element, "is_set") == false && ReadBoolFlexible(element, "is_kit") == false)
            return NkCardType.TradeUnit;

        return NkCardType.Unknown;
    }

    public static DateTimeOffset? ParseNkUpdatedAt(JsonElement element)
    {
        foreach (var field in new[] { "updated_date", "update_date", "to_date" })
        {
            var text = ReadString(element, field);
            if (TryParseNkDateTime(text, out var parsed))
                return parsed;
        }

        return null;
    }

    public static string? ReadCategoryName(JsonElement element) =>
        NormalizeCategoryName(ReadCategoryInfo(element).Name);

    public static int? ReadNkCategoryId(JsonElement element) =>
        ReadCategoryInfo(element).Id;

    public static (string? Name, int? Id) ReadCategoryInfo(JsonElement element)
    {
        var direct = ReadString(element, "category");
        if (!string.IsNullOrWhiteSpace(direct))
            return (direct, null);

        if (element.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array)
        {
            foreach (var category in categories.EnumerateArray())
            {
                var info = ReadCategoryFromCategoriesElement(category);
                if (!string.IsNullOrWhiteSpace(info.Name))
                    return info;
            }
        }

        var fromAttrs = ReadCategoryFromAttrs(element);
        return string.IsNullOrWhiteSpace(fromAttrs) ? (null, null) : (fromAttrs, null);
    }

    public static string MapCardStatusPrimary(IReadOnlyList<string> detailedStatuses)
    {
        foreach (var candidate in CardStatusPrimaryPriority)
        {
            if (detailedStatuses.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                return candidate;
        }

        return detailedStatuses.FirstOrDefault() ?? "";
    }

    /// <summary>
    /// Fills missing product group / template from settings defaults and recomputes <see cref="CrptProductCatalogItem.CanOrderCodes"/>.
    /// </summary>
    public static CrptProductCatalogItem ApplyCatalogDefaults(
        CrptProductCatalogItem item,
        string? defaultProductGroup,
        int? defaultTemplateId)
    {
        var productGroup = !string.IsNullOrWhiteSpace(item.ProductGroup)
            ? item.ProductGroup
            : defaultProductGroup;
        var templateId = item.TemplateId ?? defaultTemplateId;
        var canOrder = ComputeCanOrderCodes(item.NkStatus, item.IsSigned, productGroup, item.NkCardType);

        if (string.Equals(productGroup, item.ProductGroup, StringComparison.OrdinalIgnoreCase)
            && templateId == item.TemplateId
            && canOrder == item.CanOrderCodes)
            return item;

        return CopyCatalogItem(item, productGroup: productGroup, templateId: templateId, canOrderCodes: canOrder);
    }

    private static CrptProductCatalogItem CopyCatalogItem(
        CrptProductCatalogItem item,
        string? productGroup = null,
        int? templateId = null,
        bool? canOrderCodes = null,
        string? categoryName = null,
        int? nkCategoryId = null,
        string? nkEtag = null) =>
        new()
        {
            Gtin = item.Gtin,
            GoodId = item.GoodId,
            Name = item.Name,
            TnvedCode = item.TnvedCode,
            TnvedGroup = item.TnvedGroup,
            ProductGroup = productGroup ?? item.ProductGroup,
            TemplateId = templateId ?? item.TemplateId,
            NkStatus = item.NkStatus,
            NkStatusRaw = item.NkStatusRaw,
            NkProductState = item.NkProductState,
            NkCardType = item.NkCardType,
            NkCardStatusPrimary = item.NkCardStatusPrimary,
            NkDetailedStatuses = item.NkDetailedStatuses,
            CategoryName = categoryName ?? item.CategoryName,
            NkCategoryId = nkCategoryId ?? item.NkCategoryId,
            NkUpdatedAt = item.NkUpdatedAt,
            IsSigned = item.IsSigned,
            CanOrderCodes = canOrderCodes ?? item.CanOrderCodes,
            CertificateDocType = item.CertificateDocType,
            CertificateDocNumber = item.CertificateDocNumber,
            CertificateDocDate = item.CertificateDocDate,
            SyncedAt = item.SyncedAt,
            SyncError = item.SyncError,
            NkEtag = nkEtag ?? item.NkEtag,
        };

    public static CrptNkEtagsListDiff.EtagsListPage ParseEtagsListResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var container = root.TryGetProperty("result", out var resultEl) ? resultEl : root;

        var goodsCount = ReadInt(container, "goods_count") ?? 0;
        var offset = ReadInt(container, "offset") ?? 0;
        var lastProductNumber = ReadInt(container, "last_product_number");

        var entries = new List<CrptNkEtagsListDiff.EtagsListEntry>();
        if (container.TryGetProperty("goods", out var goodsEl) && goodsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var good in goodsEl.EnumerateArray())
            {
                var goodId = ReadInt(good, "good_id");
                var etag = ReadString(good, "etag");
                if (goodId is null or <= 0 || string.IsNullOrWhiteSpace(etag))
                    continue;

                entries.Add(new CrptNkEtagsListDiff.EtagsListEntry(goodId.Value, etag));
            }
        }

        return new CrptNkEtagsListDiff.EtagsListPage(entries, goodsCount, offset, lastProductNumber);
    }

    public static CrptProductCatalogItem ApplyRemoteEtag(
        CrptProductCatalogItem item,
        IReadOnlyDictionary<int, string> remoteEtagsByGoodId)
    {
        if (item.GoodId is not int goodId || !remoteEtagsByGoodId.TryGetValue(goodId, out var etag))
            return item;

        if (string.Equals(item.NkEtag, etag, StringComparison.Ordinal))
            return item;

        return CopyCatalogItem(item, nkEtag: etag);
    }

    /// <summary>
    /// Reads GTIN from product-list (<c>gtin</c>) or feed-product (<c>identified_by</c>).
    /// </summary>
    public static string? ReadGtin(JsonElement element)
    {
        var gtin = ReadString(element, "gtin");
        if (!string.IsNullOrWhiteSpace(gtin))
            return gtin;

        if (!element.TryGetProperty("identified_by", out var identifiers) ||
            identifiers.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var identifier in identifiers.EnumerateArray())
        {
            var type = ReadString(identifier, "type");
            if (!string.Equals(type, "gtin", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = ReadString(identifier, "value");
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    /// <summary>
    /// NK LK «Опубликован» / «Готов к вводу в оборот»: <c>good_status=published</c>,
    /// <c>published</c> in <c>good_detailed_status</c>, or <c>good_turn_flag</c>.
    /// </summary>
    public static bool IsPublishedInNkLk(JsonElement element)
    {
        var status = ReadString(element, "good_status");
        if (string.Equals(status, "published", StringComparison.OrdinalIgnoreCase))
            return true;

        var detailed = ReadStringArray(element, "good_detailed_status");
        if (detailed.Contains("published", StringComparer.OrdinalIgnoreCase))
            return true;

        return ReadBoolFlexible(element, "good_turn_flag") == true;
    }

    /// <summary>
    /// NK API v4 product-list exposes <c>good_status</c> and <c>good_detailed_status</c>;
    /// <c>good_signed</c> is returned by feed-product only.
    /// </summary>
    public static string ResolveNkStatus(JsonElement element, string? fallback = null)
    {
        if (IsPublishedInNkLk(element))
            return "published";

        var status = ReadString(element, "good_status");
        if (!string.IsNullOrWhiteSpace(status))
            return status;

        var detailed = ReadStringArray(element, "good_detailed_status");
        foreach (var candidate in new[] { "notsigned", "draft", "moderation", "errors", "archived" })
        {
            if (detailed.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                return candidate;
        }

        return fallback ?? "";
    }

    /// <summary>
    /// Maps LK «подписана» from <c>good_signed</c> (feed-product) or <c>good_detailed_status</c>
    /// (product-list): «notsigned» / «draft» → false; «published» without «notsigned» → true.
    /// </summary>
    public static bool ResolveIsSigned(JsonElement element, bool? fallback = null)
    {
        var explicitSigned = ReadBoolFlexible(element, "good_signed");
        if (explicitSigned.HasValue)
            return explicitSigned.Value;

        var detailed = ReadStringArray(element, "good_detailed_status");
        if (detailed.Contains("notsigned", StringComparer.OrdinalIgnoreCase))
            return false;

        var status = ResolveNkStatus(element);
        if (string.Equals(status, "published", StringComparison.OrdinalIgnoreCase))
            return true;

        if (detailed.Contains("published", StringComparer.OrdinalIgnoreCase))
            return true;

        if (detailed.Contains("draft", StringComparer.OrdinalIgnoreCase))
            return false;

        return fallback ?? false;
    }

    public static (IReadOnlyList<JsonElement> Goods, int Total) ParseProductListResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("result", out var resultEl))
            return ParseProductListContainer(resultEl);

        return ParseProductListContainer(root);
    }

    public static IReadOnlyList<JsonElement> ParseFeedProductEntries(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("result", out var resultEl))
            return DetachElements(ReadGoodsArray(resultEl));

        if (root.ValueKind == JsonValueKind.Array)
            return DetachElements(root.EnumerateArray().ToList());

        return DetachElements(ReadGoodsArray(root));
    }

    private static List<JsonElement> DetachElements(IReadOnlyList<JsonElement> elements)
    {
        var detached = new List<JsonElement>(elements.Count);
        foreach (var element in elements)
            detached.Add(JsonSerializer.SerializeToElement(element));
        return detached;
    }

    private static (IReadOnlyList<JsonElement>, int Total) ParseProductListContainer(JsonElement container)
    {
        var goods = ReadGoodsArray(container);
        var total = ReadInt(container, "total") ?? goods.Count;
        return (DetachElements(goods), total);
    }

    private static List<JsonElement> ReadGoodsArray(JsonElement container)
    {
        if (container.ValueKind == JsonValueKind.Array)
            return FlattenGoods(container);

        if (container.TryGetProperty("goods", out var goodsEl) && goodsEl.ValueKind == JsonValueKind.Array)
            return FlattenGoods(goodsEl);

        return [];
    }

    private static List<JsonElement> FlattenGoods(JsonElement array)
    {
        var goods = new List<JsonElement>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Array)
                goods.AddRange(FlattenGoods(item));
            else if (item.ValueKind == JsonValueKind.Object)
                goods.Add(item);
        }

        return goods;
    }

    private static string? ReadTnvedCode(JsonElement feedEntry)
    {
        if (!feedEntry.TryGetProperty("good_attrs", out var attrs) || attrs.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var attr in attrs.EnumerateArray())
        {
            var attrId = ReadInt(attr, "attr_id");
            if (attrId != TnvedAttributeId)
                continue;

            return ReadString(attr, "attr_value")
                ?? ReadString(attr, "value");
        }

        return null;
    }

    private static CertificateFields? ReadCertificate(JsonElement feedEntry)
    {
        if (!feedEntry.TryGetProperty("certificates", out var certs) || certs.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var cert in certs.EnumerateArray())
        {
            var docNumber = ReadString(cert, "number")
                ?? ReadString(cert, "doc_number");
            if (string.IsNullOrWhiteSpace(docNumber))
                continue;

            return new CertificateFields(
                ReadString(cert, "type") ?? ReadString(cert, "doc_type"),
                docNumber,
                ReadString(cert, "date") ?? ReadString(cert, "doc_date"));
        }

        return null;
    }

    private static NkProductState MapProductStateFromTokens(string? status, IReadOnlyList<string> detailed)
    {
        foreach (var token in EnumerateStateTokens(status, detailed))
        {
            var mapped = MapSingleProductStateToken(token);
            if (mapped != NkProductState.Unknown)
                return mapped;
        }

        return NkProductState.Unknown;
    }

    private static IEnumerable<string> EnumerateStateTokens(string? status, IReadOnlyList<string> detailed)
    {
        if (!string.IsNullOrWhiteSpace(status))
            yield return status;

        foreach (var item in detailed)
        {
            if (!string.IsNullOrWhiteSpace(item))
                yield return item;
        }
    }

    private static NkProductState MapSingleProductStateToken(string token) =>
        token.ToLowerInvariant() switch
        {
            "published" => NkProductState.Published,
            "draft" => NkProductState.Draft,
            "moderation" => NkProductState.Moderation,
            "errors" => NkProductState.Errors,
            "archived" => NkProductState.Archived,
            _ => NkProductState.Unknown,
        };

    private static bool HasTradeUnitLevel(JsonElement element)
    {
        if (!element.TryGetProperty("identified_by", out var identifiers) ||
            identifiers.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var identifier in identifiers.EnumerateArray())
        {
            var level = ReadString(identifier, "level");
            if (string.Equals(level, "trade-unit", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static (string? Name, int? Id) ReadCategoryFromCategoriesElement(JsonElement category)
    {
        if (category.ValueKind == JsonValueKind.String)
        {
            var text = category.GetString();
            return string.IsNullOrWhiteSpace(text) ? (null, null) : (text, null);
        }

        if (category.ValueKind != JsonValueKind.Object)
            return (null, null);

        var name = ReadString(category, "cat_name")
            ?? ReadString(category, "name")
            ?? ReadString(category, "category")
            ?? ReadString(category, "title");
        var id = ReadInt(category, "cat_id");
        return (name, id);
    }

    private static string? ReadCategoryFromAttrs(JsonElement element)
    {
        if (!element.TryGetProperty("good_attrs", out var attrs) || attrs.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var attr in attrs.EnumerateArray())
        {
            var attrName = ReadString(attr, "attr_name")
                ?? ReadString(attr, "name");
            if (!string.Equals(attrName, "Категория", StringComparison.OrdinalIgnoreCase))
                continue;

            return ReadString(attr, "attr_value")
                ?? ReadString(attr, "value");
        }

        return null;
    }

    private static bool TryParseNkDateTime(string? text, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (DateTime.TryParseExact(
                text,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var local))
        {
            result = new DateTimeOffset(local);
            return true;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool? ReadBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static bool? ReadBoolFlexible(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) ? parsed : null,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            _ => null,
        };
    }

    private static List<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    items.Add(text);
            }
        }

        return items;
    }

    private static string? NormalizeCategoryName(string? name) =>
        CrptNkCategoryDiscovery.NormalizeCategoryName(name);

    private sealed record CertificateFields(string? DocType, string DocNumber, string? DocDate);
}
