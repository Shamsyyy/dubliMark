using System.Text.Json;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;

namespace DoubleMark.CrptProbe;

internal static class FeedProductDiagnostic
{
    public static async Task RunAsync(CrptProbeConfig config)
    {
        var settings = config.ToConnectionSettings();
        using var auth = new CrptAuthClient(settings);
        var certificate = CrptCertificateProvider.FindCertificate(settings);
        var jwt = await auth.AuthenticateJwtAsync(certificate, CancellationToken.None);
        Console.WriteLine("JWT OK");

        settings.NkBaseUrl = config.NkBaseUrl ?? settings.NkBaseUrl;
        using var nk = new CrptNkClient(settings, jwt.Value);

        var fromDate = CrptNkProductMapper.DefaultProductListFromDate;
        var toDate = CrptNkProductMapper.DefaultProductListToDate();
        var listJson = await nk.GetProductListAsync(
            limit: 50,
            offset: 0,
            fromDate: fromDate,
            toDate: toDate,
            ct: CancellationToken.None);

        var (goods, total) = CrptNkProductMapper.ParseProductListResponse(listJson);
        Console.WriteLine($"product-list total={total}, sample={goods.Count}");

        var published = goods
            .Select(g => (Entry: g, Item: SafeMap(g)))
            .Where(x => x.Item is not null)
            .FirstOrDefault(x =>
                string.Equals(x.Item!.NkStatus, "published", StringComparison.OrdinalIgnoreCase));

        if (published.Item is null)
        {
            Console.WriteLine("No published GTIN in first page; using first entry.");
            if (goods.Count == 0)
            {
                Console.WriteLine("product-list returned 0 goods.");
                return;
            }

            published = (goods[0], SafeMap(goods[0]));
        }

        var gtin = published.Item!.Gtin;
        Console.WriteLine($"Probing feed-product for GTIN ending …{gtin[^4..]}");
        Console.WriteLine($"product-list fields: {string.Join(", ", published.Entry.EnumerateObject().Select(p => p.Name))}");
        DumpCatalogFields(published.Entry, "product-list");
        Console.WriteLine(
            $"product-list inferred: status={published.Item.NkStatus}, signed={published.Item.IsSigned}");
        if (published.Entry.TryGetProperty("good_detailed_status", out var detailedRaw))
            Console.WriteLine($"product-list good_detailed_status raw: {detailedRaw.GetRawText()}");

        string feedJson;
        try
        {
            feedJson = await nk.GetFeedProductAsync([gtin], CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"feed-product request failed: {ex.Message}");
            throw;
        }

        Console.WriteLine($"feed-product response length: {feedJson.Length} chars");
        IReadOnlyList<JsonElement> entries;
        try
        {
            entries = CrptNkProductMapper.ParseFeedProductEntries(feedJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"feed-product parse failed: {ex.Message}");
            using var debugDoc = JsonDocument.Parse(feedJson);
            Console.WriteLine($"feed root kind: {debugDoc.RootElement.ValueKind}");
            if (debugDoc.RootElement.TryGetProperty("result", out var resultEl))
                Console.WriteLine($"feed result kind: {resultEl.ValueKind}");
            throw;
        }
        if (entries.Count == 0)
        {
            Console.WriteLine("feed-product returned 0 entries.");
            return;
        }

        var feedEntry = entries[0];
        Console.WriteLine($"feed-product fields: {string.Join(", ", feedEntry.EnumerateObject().Select(p => p.Name))}");

        DumpCatalogFields(feedEntry, "feed-product");
        DumpStatusFields(feedEntry, "feed-product");

        var merged = CrptNkProductMapper.MergeFeedProduct(
            published.Item,
            feedEntry,
            productGroup: null,
            templateId: null,
            DateTimeOffset.UtcNow);

        Console.WriteLine($"merged: status={merged.NkStatus}, signed={merged.IsSigned}, canOrder={merged.CanOrderCodes}");

        var signedFromList = goods.Count(g =>
        {
            var item = SafeMap(g);
            return item?.IsSigned == true;
        });
        Console.WriteLine($"product-list page signed count (inferred): {signedFromList}/{goods.Count}");
    }

    private static CrptProductCatalogItem? SafeMap(JsonElement good)
    {
        try
        {
            return CrptNkProductMapper.MapProductListEntry(good, DateTimeOffset.UtcNow);
        }
        catch
        {
            return null;
        }
    }

    private static void DumpCatalogFields(JsonElement element, string label)
    {
        foreach (var name in new[] { "to_date", "updated_date", "update_date", "category", "categories" })
        {
            if (!element.TryGetProperty(name, out var value))
            {
                Console.WriteLine($"{label}: {name}=<absent>");
                continue;
            }

            Console.WriteLine($"{label}: {name}={FormatJsonValue(value)}");
        }
    }

    private static void DumpStatusFields(JsonElement element, string label)
    {
        foreach (var name in new[] { "good_status", "good_detailed_status", "good_signed", "good_mark_flag", "good_turn_flag" })
        {
            if (!element.TryGetProperty(name, out var value))
            {
                Console.WriteLine($"{label}: {name}=<absent>");
                continue;
            }

            Console.WriteLine($"{label}: {name}={FormatJsonValue(value)}");
        }
    }

    private static string FormatJsonValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(FormatJsonValue)) + "]",
            _ => value.GetRawText(),
        };
}
