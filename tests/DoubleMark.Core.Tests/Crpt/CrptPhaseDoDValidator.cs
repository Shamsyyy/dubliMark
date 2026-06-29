using System.Reflection;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.Settings;
using DoubleMark.Desktop.ViewModels.Crpt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DoubleMark.Core.Tests.Crpt;

/// <summary>
/// Automated Definition-of-Done checks for CRPT integration phases (spec §13).
/// </summary>
public static class CrptPhaseDoDValidator
{
    public sealed record PhaseDoDResult(
        string Phase,
        string Id,
        string Task,
        bool IsComplete,
        bool IsPartial = false,
        string? GapNote = null);

    public static IReadOnlyList<PhaseDoDResult> EvaluateAll()
    {
        var repoRoot = FindRepoRoot();
        var results = new List<PhaseDoDResult>();
        results.AddRange(EvaluatePhaseA(repoRoot));
        results.AddRange(EvaluatePhaseB0(repoRoot));
        results.AddRange(EvaluatePhaseB(repoRoot));
        results.AddRange(EvaluatePhaseC(repoRoot));
        results.AddRange(EvaluatePhaseD(repoRoot));
        results.AddRange(EvaluatePhaseE());
        return results;
    }

    public static IReadOnlyList<PhaseDoDResult> EvaluateMvpPhases() =>
        EvaluateAll().Where(r => r.Phase is not "E").ToList();

    public static IReadOnlyList<PhaseDoDResult> EvaluatePhaseEBacklog() =>
        EvaluateAll().Where(r => r.Phase == "E" && !r.IsComplete).ToList();

    private static IEnumerable<PhaseDoDResult> EvaluatePhaseA(string repoRoot)
    {
        var desktopAssembly = typeof(CrptAuthService).Assembly;
        var settingsStore = desktopAssembly.GetType("DoubleMark.Desktop.Settings.CrptSettingsStore", throwOnError: false);
        var dpapiProtector = desktopAssembly.GetType("DoubleMark.Desktop.Settings.DpapiCrptSecretsProtector", throwOnError: false);
        var secretsFileName = settingsStore?
            .GetField("SecretsFileName", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null) as string;

        yield return Item("A", "A1", "CrptSettings + CrptSettingsStore (DPAPI)",
            typeof(CrptSettings).IsClass &&
            settingsStore is not null &&
            dpapiProtector is not null &&
            secretsFileName == "crpt-secrets.dat",
            gap: settingsStore is null ? "CrptSettingsStore missing" : null);

        yield return Item("A", "A2", "CrptCertificateProvider — выбор УКЭП, подпись",
            typeof(ICrptCertificateProvider).IsInterface &&
            typeof(StoreCrptCertificateProvider).IsClass &&
            typeof(ICrptCertificateProvider).GetMethod(nameof(ICrptCertificateProvider.FindCertificate)) is not null &&
            typeof(ICrptCertificateProvider).GetMethod(nameof(ICrptCertificateProvider.ListEligibleCertificates)) is not null);

        yield return Item("A", "A3", "CrptAuthService — key + simpleSignIn",
            typeof(ICrptAuthService).IsInterface &&
            typeof(CrptAuthService).IsClass &&
            typeof(CrptSettingsViewModel).GetProperty(nameof(CrptSettingsViewModel.TestConnectionCommand)) is not null);

        var refreshSource = ReadRepoFile(repoRoot,
            "src", "DoubleMark.Desktop", "Services", "Crpt", "CrptTokenRefreshHostedService.cs");
        yield return Item("A", "A4", "CrptTokenRefreshService — авто refresh каждые 8 ч",
            typeof(CrptTokenRefreshHostedService).IsClass &&
            typeof(IHostedService).IsAssignableFrom(typeof(CrptTokenRefreshHostedService)) &&
            refreshSource.Contains("FromHours(8)", StringComparison.Ordinal));

        var settingsViewText = ReadRepoFile(repoRoot, "src", "DoubleMark.Desktop", "Views", "CrptSettingsView.xaml");
        yield return Item("A", "A5", "CrptSettingsView + «Проверить подключение»",
            settingsViewText.Contains("Проверить подключение", StringComparison.Ordinal) &&
            settingsViewText.Contains("TestConnectionCommand", StringComparison.Ordinal));
    }

    private static IEnumerable<PhaseDoDResult> EvaluatePhaseB0(string repoRoot)
    {
        var libAssembly = typeof(CrptNkClient).Assembly;

        yield return Item("B0", "B0.1", "CrptNkClient — product-list, feed-product",
            TypeHasMethods(libAssembly, nameof(CrptNkClient), "GetProductListAsync", "GetFeedProductAsync"));

        yield return Item("B0", "B0.2", "CrptTrueApiProductClient — product/info",
            libAssembly.GetType("DoubleMark.Crpt.CrptTrueApiProductClient", throwOnError: false) is not null);

        yield return Item("B0", "B0.3", "CrptNkProductMapper → CrptProductCatalogItem",
            typeof(CrptNkProductMapper).IsClass);

        yield return Item("B0", "B0.4", "CrptProductCatalogStore — load/save JSON",
            typeof(CrptProductCatalogStore).IsClass &&
            typeof(ICrptProductCatalogStore).IsInterface);

        yield return Item("B0", "B0.5", "CrptCatalogSyncService + progress",
            typeof(CrptCatalogSyncService).IsClass &&
            typeof(ICrptCatalogSyncService).IsInterface);

        var catalogViewText = ReadRepoFile(repoRoot, "src", "DoubleMark.Desktop", "Views", "CrptCatalogView.xaml");
        yield return Item("B0", "B0.6", "CrptCatalogView + «Обновить каталог»",
            catalogViewText.Contains("Обновить из НК", StringComparison.Ordinal) &&
            catalogViewText.Contains("CanOrderCodes", StringComparison.Ordinal) &&
            typeof(CrptCatalogFilter).IsEnum);

        var probeText = ReadRepoFile(repoRoot, "tools", "DoubleMark.CrptProbe", "Program.cs");
        yield return Item("B0", "B0.7", "Probe --sync-catalog",
            probeText.Contains("--sync-catalog", StringComparison.Ordinal) &&
            probeText.Contains("GetProductListAsync", StringComparison.Ordinal));

        var settings = new CrptSettings
        {
            ProductGroupTemplateDefaults = { [CrptProductGroup.Chemistry] = 46 },
        };
        yield return Item("B0", "B0.8", "Defaults templateId по productGroup (chemistry=46)",
            settings.ResolveTemplateId(CrptProductGroup.Chemistry) == 46);
    }

    private static IEnumerable<PhaseDoDResult> EvaluatePhaseB(string repoRoot)
    {
        yield return Item("B", "B1", "CrptSuzClient.CreateOrder",
            MethodExists(typeof(CrptSuzClient), nameof(CrptSuzClient.CreateOrderAsync)) &&
            MethodExists(typeof(CrptSuzService), nameof(CrptSuzService.CreateOrderAsync)));

        yield return Item("B", "B2", "Polling status",
            MethodExists(typeof(CrptSuzService), nameof(CrptSuzService.PollUntilReadyAsync)));

        yield return Item("B", "B3", "Download codes blocks",
            MethodExists(typeof(CrptSuzService), nameof(CrptSuzService.DownloadCodesAsync)));

        var ordersViewText = ReadRepoFile(repoRoot, "src", "DoubleMark.Desktop", "Views", "CrptOrdersView.xaml");
        yield return Item("B", "B4", "CrptOrdersView — заказ из каталога",
            ordersViewText.Contains("OrderableItems", StringComparison.Ordinal) &&
            ordersViewText.Contains("SelectedGtin", StringComparison.Ordinal));

        yield return Item("B", "B5", "Close order",
            MethodExists(typeof(CrptSuzClient), nameof(CrptSuzClient.CloseOrderAsync)) &&
            MethodExists(typeof(CrptSuzService), nameof(CrptSuzService.CloseOrderAsync)));
    }

    private static IEnumerable<PhaseDoDResult> EvaluatePhaseC(string repoRoot)
    {
        yield return Item("C", "C1", "Очередь печати + MarkRenderService",
            typeof(ICrptPrintService).IsInterface &&
            typeof(CrptPrintService).IsClass &&
            File.Exists(Path.Combine(repoRoot, "src", "DoubleMark.Desktop", "Views", "CrptPrintQueueView.xaml")));

        yield return Item("C", "C2", "Статус Printed без потери GS",
            MethodExists(typeof(CrptPrintQueueViewModel), nameof(CrptPrintQueueViewModel.MarkCodePrinted)) &&
            typeof(CrptPrintService).GetMethod(nameof(CrptPrintService.RenderLabel)) is not null);

        yield return Item("C", "C3", "Пакетная печать N кодов",
            MethodExists(typeof(ICrptPrintService), nameof(ICrptPrintService.RenderBatch)) &&
            typeof(CrptPrintQueueViewModel).GetProperty(nameof(CrptPrintQueueViewModel.RenderBatchCommand)) is not null,
            isPartial: true,
            gap: "Render-only batch via MarkRenderService; physical printer dispatch deferred to shared MarkPrintService wiring.");
    }

    private static IEnumerable<PhaseDoDResult> EvaluatePhaseD(string repoRoot)
    {
        yield return Item("D", "D1", "CrptGisMtClient.SendUtilisation",
            MethodExists(typeof(CrptGisMtClient), nameof(CrptGisMtClient.SendUtilisationAsync)) &&
            MethodExists(typeof(CrptGisMtService), nameof(CrptGisMtService.SendUtilisationForOrderAsync)));

        var printQueueText = ReadRepoFile(repoRoot, "src", "DoubleMark.Desktop", "Views", "CrptPrintQueueView.xaml");
        yield return Item("D", "D2", "UI «Отправить отчёт о нанесении»",
            printQueueText.Contains("Отправить отчёт о нанесении", StringComparison.Ordinal) &&
            printQueueText.Contains("SendUtilisationCommand", StringComparison.Ordinal) &&
            MethodExists(typeof(CrptPrintQueueViewModel), nameof(CrptPrintQueueViewModel.SelectUtilisationCandidates)));
    }

    private static IEnumerable<PhaseDoDResult> EvaluatePhaseE()
    {
        yield return Item("E", "E1", "Introduce goods UI (LP_INTRODUCE_GOODS)",
            isComplete: false,
            gap: "Phase 2 backlog — probe verified, Desktop UI not implemented.");

        yield return Item("E", "E2", "Инкрементальный sync НК (etagslist)",
            typeof(CrptNkClient).GetMethod(nameof(CrptNkClient.GetEtagsListAsync)) is not null &&
            typeof(CrptCatalogSyncService).GetMethod(nameof(CrptCatalogSyncService.ShouldUseIncrementalSync)) is not null);

        yield return Item("E", "E3", "GS1 aggregation",
            isComplete: false,
            gap: "Phase 2 backlog.");

        yield return Item("E", "E4", "Diadoc",
            isComplete: false,
            gap: "Phase 2 backlog.");

        yield return Item("E", "E5", "IntroduceGoodsAsync throws NotImplemented",
            MethodExists(typeof(CrptGisMtService), nameof(CrptGisMtService.IntroduceGoodsAsync)) &&
            CrptMvpScope.Phase2Features.Contains(CrptIntegrationFeature.IntroduceToCirculation),
            gap: "Service stub throws NotImplementedException — expected for Phase 2.");
    }

    private static PhaseDoDResult Item(
        string phase,
        string id,
        string task,
        bool isComplete,
        bool isPartial = false,
        string? gap = null) =>
        new(phase, id, task, isComplete, isPartial, gap);

    private static bool TypeHasMethods(Assembly assembly, string typeName, params string[] methodNames)
    {
        var type = assembly.GetType($"DoubleMark.Crpt.{typeName}", throwOnError: false);
        if (type is null)
            return false;

        return methodNames.All(name => type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Any(m => m.Name == name));
    }

    private static bool MethodExists(Type type, string methodName) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Any(m => m.Name == methodName);

    public static bool DiRegistersPhaseComponents()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "DoubleMark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var provider = new ServiceCollection()
                .AddCrptServices(tempDirectory)
                .BuildServiceProvider();

            return provider.GetService<ICrptPrintService>() is CrptPrintService &&
                   provider.GetService<IHostedService>() is CrptTokenRefreshHostedService;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    public static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DoubleMark.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string ReadRepoFile(string repoRoot, params string[] segments)
    {
        var path = Path.Combine(new[] { repoRoot }.Concat(segments).ToArray());
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }
}
