using System.Text.Json;
using System.Text.RegularExpressions;
using DoubleMark.Crpt;

namespace DoubleMark.CrptProbe;

internal static class ProductListDiagnostic
{
    private static readonly Regex GtinPattern = new(@"""gtin""\s*:\s*""\d+""", RegexOptions.Compiled);

    public static async Task RunAsync(CrptProbeConfig config, string[] args)
    {
        var settings = config.ToConnectionSettings();
        using var auth = new CrptAuthClient(settings);
        var certificate = CrptCertificateProvider.FindCertificate(settings);
        var jwt = await auth.AuthenticateJwtAsync(certificate, CancellationToken.None);
        Console.WriteLine("JWT OK");

        settings.NkBaseUrl = config.NkBaseUrl ?? settings.NkBaseUrl;
        using var nk = new CrptNkClient(settings, jwt.Value);

        var scenarios = new (string Label, int Limit, int Offset, string? GoodStatus, string? FromDate, string? ToDate)[]
        {
            ("published (default probe)", 10, 0, "published", null, null),
            ("no good_status filter", 10, 0, null, null, null),
            ("from_date=2020-01-01", 10, 0, null, "2020-01-01 00:00:00", null),
            ("from_date=2020-01-01 + published", 10, 0, "published", "2020-01-01 00:00:00", null),
            ("from_date=2024-01-01", 10, 0, null, "2024-01-01 00:00:00", null),
            ("from_date=2025-01-01", 10, 0, null, "2025-01-01 00:00:00", null),
            ("from+to wide range", 10, 0, null, "2020-01-01 00:00:00", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
        };

        foreach (var (label, limit, offset, goodStatus, fromDate, toDate) in scenarios)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {label} ===");
            try
            {
                var json = await nk.GetProductListAsync(limit, offset, goodStatus, fromDate, toDate, CancellationToken.None);
                DumpSummary(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }

        var apiKey = ReadOptionalArg(args, "--nk-apikey");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine();
            Console.WriteLine("=== apikey only (no JWT) + from_date=2020-01-01 ===");
            using var nkKey = new CrptNkClient(settings, bearerToken: null, apiKey: apiKey);
            var json = await nkKey.GetProductListAsync(10, 0, goodStatus: null, fromDate: "2020-01-01 00:00:00", ct: CancellationToken.None);
            DumpSummary(json);
        }
    }

    private static void DumpSummary(string json)
    {
        var redacted = GtinPattern.Replace(json, "\"gtin\":\"[REDACTED]\"");
        Console.WriteLine($"raw length: {json.Length} chars");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Console.WriteLine($"root keys: {string.Join(", ", root.EnumerateObject().Select(p => p.Name))}");

        if (!root.TryGetProperty("result", out var result))
        {
            Console.WriteLine("no result property");
            return;
        }

        if (result.ValueKind == JsonValueKind.Array)
        {
            Console.WriteLine($"result is ARRAY, count={result.GetArrayLength()}");
            return;
        }

        Console.WriteLine($"result keys: {string.Join(", ", result.EnumerateObject().Select(p => p.Name))}");
        var total = result.TryGetProperty("total", out var totalEl) && totalEl.TryGetInt32(out var t) ? t : -1;
        var goodsCount = 0;
        if (result.TryGetProperty("goods", out var goods) && goods.ValueKind == JsonValueKind.Array)
            goodsCount = goods.GetArrayLength();

        Console.WriteLine($"total={total}, goods.length={goodsCount}");
        var (parsedGoods, parsedTotal) = CrptNkProductMapper.ParseProductListResponse(json);
        Console.WriteLine($"parser: total={parsedTotal}, goods={parsedGoods.Count}");

        if (redacted.Length <= 500)
            Console.WriteLine($"body: {redacted}");
    }

    private static string? ReadOptionalArg(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
