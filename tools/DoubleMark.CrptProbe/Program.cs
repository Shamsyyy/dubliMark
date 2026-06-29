using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoubleMark.Crpt;

namespace DoubleMark.CrptProbe;

internal static class Program
{
    private static readonly Regex SensitivePayload = new(@"(?<=""codes""\s*:\s*\[)[^\]]+", RegexOptions.Singleline);

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var (configPath, commandArgs) = ParseArgs(args);
            if (!File.Exists(configPath))
            {
                var devPath = FindDevConfigPath();
                if (devPath is not null)
                    configPath = devPath;
            }

            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine("Config not found. Copy crpt-probe.local.example.json to crpt-probe.local.json");
                return 1;
            }

            var config = CrptProbeConfig.Load(configPath);
            var settings = config.ToConnectionSettings();
            Console.WriteLine($"Config: {configPath}");

            var certificate = CrptCertificateProvider.FindCertificate(settings);
            Console.WriteLine($"Certificate thumbprint: {certificate.Thumbprint}");

            if (commandArgs.Contains("--product-list-diag", StringComparer.OrdinalIgnoreCase))
            {
                await ProductListDiagnostic.RunAsync(config, commandArgs.ToArray());
                return 0;
            }

            if (commandArgs.Contains("--feed-product-diag", StringComparer.OrdinalIgnoreCase))
            {
                await FeedProductDiagnostic.RunAsync(config);
                return 0;
            }

            if (commandArgs.Contains("--sync-catalog", StringComparer.OrdinalIgnoreCase))
                return await RunSyncCatalogStubAsync(config, settings, certificate, commandArgs);

            if (commandArgs.Contains("--introduce", StringComparer.OrdinalIgnoreCase))
                return await RunIntroduceFlowAsync(config, settings, certificate, configPath, commandArgs);

            if (commandArgs.Contains("--doc-status", StringComparer.OrdinalIgnoreCase))
                return await RunDocStatusAsync(settings, certificate, commandArgs);

            if (commandArgs.Contains("--order", StringComparer.OrdinalIgnoreCase) ||
                !commandArgs.Any(a => a.StartsWith("--", StringComparison.Ordinal)))
                return await RunOrderFlowAsync(config, settings, certificate, configPath);

            Console.Error.WriteLine("Unknown command. Use --order, --sync-catalog, or --introduce [--codes-file path.json]");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAILED:");
            Console.Error.WriteLine(ex.Message);
            if (ex.InnerException is not null)
                Console.Error.WriteLine(ex.InnerException.Message);
            return 2;
        }
    }

    private static async Task<int> RunSyncCatalogStubAsync(
        CrptProbeConfig config,
        CrptConnectionSettings settings,
        X509Certificate2 certificate,
        IReadOnlyList<string> commandArgs)
    {
        _ = certificate;
        var gtinFilter = ReadOptionalArg(commandArgs, "--gtin");

        Console.WriteLine("Sync catalog via NK product-list + feed-product");
        Console.WriteLine($"NK base URL: {config.NkBaseUrl ?? settings.NkBaseUrl}");
        if (!string.IsNullOrWhiteSpace(gtinFilter))
            Console.WriteLine($"GTIN filter: {gtinFilter}");

        using var auth = new CrptAuthClient(settings);
        var jwtToken = await auth.AuthenticateJwtAsync(certificate, CancellationToken.None);
        Console.WriteLine("JWT token OK");

        settings.NkBaseUrl = config.NkBaseUrl ?? settings.NkBaseUrl;
        using var nk = new CrptNkClient(settings, jwtToken.Value);
        var fromDate = CrptNkProductMapper.DefaultProductListFromDate;
        var toDate = CrptNkProductMapper.DefaultProductListToDate();
        var productList = await nk.GetProductListAsync(
            limit: 1000,
            offset: 0,
            fromDate: fromDate,
            toDate: toDate,
            ct: CancellationToken.None);
        var (goods, total) = CrptNkProductMapper.ParseProductListResponse(productList);
        Console.WriteLine($"product-list total={total}, page goods={goods.Count}");

        var signedCount = 0;
        foreach (var good in goods)
        {
            try
            {
                if (CrptNkProductMapper.MapProductListEntry(good, DateTimeOffset.UtcNow).IsSigned)
                    signedCount++;
            }
            catch
            {
                // skip malformed entries
            }
        }

        Console.WriteLine($"product-list signed (inferred): {signedCount}/{goods.Count}");

        if (!string.IsNullOrWhiteSpace(gtinFilter))
        {
            var feed = await nk.GetFeedProductAsync([gtinFilter], CancellationToken.None);
            Console.WriteLine($"feed-product response length: {feed.Length} chars");
        }

        Console.WriteLine("Sync catalog completed.");
        return 0;
    }

    private static string? ReadOptionalArg(IReadOnlyList<string> args, string flag)
    {
        var idx = IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Count ? args[idx + 1] : null;
    }

    private static async Task<int> RunOrderFlowAsync(
        CrptProbeConfig config,
        CrptConnectionSettings settings,
        X509Certificate2 certificate,
        string configPath)
    {
        Console.WriteLine($"Order: GTIN={config.Gtin}, qty={config.Quantity}, PG={config.ProductGroup}, template={config.TemplateId}");

        using var auth = new CrptAuthClient(settings);
        var suzToken = await auth.AuthenticateForSuzAsync(certificate, CancellationToken.None);
        Console.WriteLine("SUZ token OK");

        using var suz = new CrptSuzClient(settings);
        Console.WriteLine(await suz.PingAsync(suzToken.Value, CancellationToken.None));

        var result = await suz.CreateAndDownloadAsync(
            suzToken.Value, certificate, config.Gtin, config.Quantity, CancellationToken.None);

        var outputDir = Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "output");
        Directory.CreateDirectory(outputDir);
        var orderFile = Path.Combine(outputDir, $"order-{result.OrderId}.json");
        await File.WriteAllTextAsync(orderFile, result.CodesResponse);

        var safePreview = SensitivePayload.Replace(result.CodesResponse, "[REDACTED]");
        Console.WriteLine($"OrderId: {result.OrderId}");
        Console.WriteLine($"Saved: {orderFile}");
        Console.WriteLine(safePreview.Length > 1500 ? safePreview[..1500] + "..." : safePreview);
        return 0;
    }

    private static async Task<int> RunDocStatusAsync(
        CrptConnectionSettings settings,
        X509Certificate2 certificate,
        IReadOnlyList<string> commandArgs)
    {
        var idx = IndexOf(commandArgs, "--doc-status");
        if (idx < 0 || idx + 1 >= commandArgs.Count)
            throw new InvalidOperationException("Pass --doc-status <documentId>");

        var documentId = commandArgs[idx + 1];
        using var auth = new CrptAuthClient(settings);
        var jwtToken = await auth.AuthenticateJwtAsync(certificate, CancellationToken.None);
        using var gis = new CrptGisMtClient(settings);
        var info = await gis.GetDocumentInfoAsync(jwtToken.Value, documentId, CancellationToken.None);
        Console.WriteLine(info);
        return 0;
    }

    private static async Task<int> RunIntroduceFlowAsync(
        CrptProbeConfig config,
        CrptConnectionSettings settings,
        X509Certificate2 certificate,
        string configPath,
        IReadOnlyList<string> commandArgs)
    {
        var codesFile = config.CodesFile;
        var codesFileArgIdx = IndexOf(commandArgs, "--codes-file");
        if (codesFileArgIdx >= 0 && codesFileArgIdx + 1 < commandArgs.Count)
            codesFile = commandArgs[codesFileArgIdx + 1];

        codesFile ??= FindLatestOrderFile(configPath);
        if (codesFile is null || !File.Exists(codesFile))
            throw new InvalidOperationException("Codes file not found. Pass --codes-file or run --order first.");

        var orderJson = await File.ReadAllTextAsync(codesFile);
        var codes = CrptSuzClient.ParseCodesFromOrderFile(orderJson);
        Console.WriteLine($"Codes file: {codesFile}, count={codes.Count}");

        using var auth = new CrptAuthClient(settings);
        var suzToken = await auth.AuthenticateForSuzAsync(certificate, CancellationToken.None);
        var jwtToken = await auth.AuthenticateJwtAsync(certificate, CancellationToken.None);
        Console.WriteLine("SUZ + JWT tokens OK");

        using var suz = new CrptSuzClient(settings);
        Console.WriteLine("Sending utilisation report (нанесение)...");
        try
        {
            var utilisation = await suz.SendUtilisationAsync(suzToken.Value, certificate, codes, CancellationToken.None);
            Console.WriteLine($"Utilisation submitted: {utilisation.DocumentId}");
            Console.WriteLine(utilisation.RawResponse);
            await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Utilisation skipped or failed: {ex.Message}");
        }

        var documentJson = CrptIntroduceGoodsBuilder.BuildDocumentJson(settings, codes);
        var outputDir = Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "output");
        Directory.CreateDirectory(outputDir);
        var docPath = Path.Combine(outputDir, "introduce-document.json");
        await File.WriteAllTextAsync(docPath, documentJson);
        Console.WriteLine($"Introduce document saved: {docPath}");

        using var gis = new CrptGisMtClient(settings);
        Console.WriteLine("Submitting LP_INTRODUCE_GOODS with detached UKEP signature...");
        var introduce = await gis.IntroduceGoodsAsync(jwtToken.Value, certificate, documentJson, CancellationToken.None);
        Console.WriteLine($"Introduce document id: {introduce.DocumentId}");
        Console.WriteLine(introduce.RawResponse);

        await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
        try
        {
            var info = await gis.GetDocumentInfoAsync(jwtToken.Value, introduce.DocumentId, CancellationToken.None);
            Console.WriteLine("Document status:");
            Console.WriteLine(RedactMarkingCodes(info));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Document info not available yet: {ex.Message}");
        }

        return 0;
    }

    private static (string configPath, List<string> commandArgs) ParseArgs(string[] args)
    {
        var list = args.ToList();
        var configPath = list.FirstOrDefault(x => x.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            ?? Path.Combine(AppContext.BaseDirectory, "crpt-probe.local.json");
        list.RemoveAll(x => x.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        return (configPath, list);
    }

    private static int IndexOf(IReadOnlyList<string> args, string value)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static string? FindLatestOrderFile(string configPath)
    {
        var dir = Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "output");
        if (!Directory.Exists(dir))
            return null;

        return Directory.GetFiles(dir, "order-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? FindDevConfigPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "DoubleMark.CrptProbe", "crpt-probe.local.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static string RedactMarkingCodes(string json) =>
        SensitivePayload.Replace(json, "[REDACTED]");
}
