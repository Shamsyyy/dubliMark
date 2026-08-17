using DoubleMark.Core.Crpt;
using DoubleMark.Core.Parsing;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DoubleMark.Core.Tests.Crpt;

/// <summary>
/// §14.2 — Structural readiness for manual sandbox integration checklist (no live API).
/// </summary>
public class CrptIntegrationReadinessTests
{
    private const char Gs = (char)0x1D;

    private static readonly string[] Checklist14_2 =
    [
        "Token flow wired (auth + expireDate + refresh)",
        "Catalog sync service (product-list + feed-product)",
        "SUZ order flow (create → poll → download → close)",
        "GS1 validation on downloaded codes (01 + 21 + GS)",
        "Print path preserves GS in marking payloads",
        "Utilisation report wired (GIS MT + builder)",
    ];

    [Fact]
    public void Section14_2_ChecklistItemsAreDocumented()
    {
        Checklist14_2.Should().HaveCount(6);
    }

    [Fact]
    public void Checklist1_TokenFlow_IsWired()
    {
        typeof(ICrptAuthService).GetMethod(nameof(ICrptAuthService.GetValidTokenAsync))
            .Should().NotBeNull();
        typeof(CrptAuthService).GetProperty(nameof(CrptAuthService.TokenExpiresAt))
            .Should().NotBeNull();

        var expireDateParam = typeof(CrptAuthResponseParser)
            .GetMethod(nameof(CrptAuthResponseParser.ParseJwtToken))
            ?.GetParameters();
        expireDateParam.Should().NotBeNull();

        typeof(CrptTokenRefreshHostedService).Should().Implement<IHostedService>();

        var tempDirectory = CreateTempDirectory();
        try
        {
            var provider = new ServiceCollection()
                .AddCrptServices(tempDirectory)
                .BuildServiceProvider();

            provider.GetRequiredService<ICrptAuthService>().Should().BeOfType<CrptAuthService>();
            provider.GetRequiredService<IHostedService>().Should().BeOfType<CrptTokenRefreshHostedService>();
        }
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Checklist2_CatalogSync_IsWired()
    {
        typeof(ICrptCatalogSyncService).GetMethod(nameof(ICrptCatalogSyncService.SyncAsync))
            .Should().NotBeNull();
        typeof(CrptCatalogSyncService).Should().Implement<ICrptCatalogSyncService>();
        typeof(CrptNkProductMapper).GetMethod(nameof(CrptNkProductMapper.MergeFeedProduct))
            .Should().NotBeNull();
        typeof(CrptNkProductMapper).GetMethod(nameof(CrptNkProductMapper.MapProductListEntry))
            .Should().NotBeNull();
        typeof(ICrptProductCatalogStore).GetMethod(nameof(ICrptProductCatalogStore.Save))
            .Should().NotBeNull();
    }

    [Fact]
    public void Checklist3_OrderFlow_IsWired()
    {
        var suzService = typeof(ICrptSuzService);
        suzService.GetMethod(nameof(ICrptSuzService.CreateOrderAsync)).Should().NotBeNull();
        suzService.GetMethod(nameof(ICrptSuzService.PollUntilReadyAsync)).Should().NotBeNull();
        suzService.GetMethod(nameof(ICrptSuzService.DownloadCodesAsync)).Should().NotBeNull();
        suzService.GetMethod(nameof(ICrptSuzService.CloseOrderAsync)).Should().NotBeNull();
        suzService.GetMethod(nameof(ICrptSuzService.CreateAndDownloadOrderAsync)).Should().NotBeNull();

        typeof(CrptSuzRequestBuilder).GetMethod(nameof(CrptSuzRequestBuilder.BuildOrderBody))
            .Should().NotBeNull();
        typeof(CrptOrderRepository).GetMethod(nameof(CrptOrderRepository.SaveAsync))
            .Should().NotBeNull();
    }

    [Fact]
    public void Checklist4_GsValidation_AcceptsSyntheticFullCode()
    {
        var code = $"010000000000000021SYN001{Gs}91EE12{Gs}92SYNTHETICPAYLOAD001=";

        Gs1BarcodeEncoding.LooksLikeGs1Cz(code).Should().BeTrue();
        code.Should().Contain("01");
        code.Should().Contain("21");
        Gs1BarcodeEncoding.CountGs(code).Should().BeGreaterThan(0);

        var block = new CrptSuzCodesBlock([code], "block-1", true);
        var validated = CrptSuzClient.ValidateAndParseCodes(block);

        validated.Should().ContainSingle().Which.Should().Be(code);
    }

    [Fact]
    public void Checklist4_GsValidation_RejectsInvalidPayload()
    {
        var block = new CrptSuzCodesBlock(["not-a-marking-code"], "block-1", true);

        var act = () => CrptSuzClient.ValidateAndParseCodes(block);

        act.Should().Throw<CrptSuzException>();
    }

    [Fact]
    public void Checklist5_PrintSafety_PreservesGsInRawPayload()
    {
        var service = new CrptPrintService();
        var raw = $"010000000000000021SYNTH{Gs}91EE12{Gs}92SYNTHETICPAYLOAD=";
        var code = new CrptMarkingCodeItem(
            Id: 1,
            OrderLocalId: "order-1",
            RawPayload: raw,
            Status: CrptCodeLifecycleStatus.Received,
            PrintedAt: null,
            LastError: null);

        var render = service.RenderLabel(code, CrptPrintDefaults.MinimalTemplate);

        render.GsCount.Should().Be(2);
        render.RawPayload.Should().Be(raw);
    }

    [Fact]
    public void Checklist6_Utilisation_IsWired()
    {
        typeof(ICrptGisMtService).GetMethod(nameof(ICrptGisMtService.SendUtilisationForOrderAsync))
            .Should().NotBeNull();
        typeof(ICrptGisMtService).GetMethod(nameof(ICrptGisMtService.SendUtilisationForCodesAsync))
            .Should().NotBeNull();
        typeof(CrptUtilisationBuilder).GetMethod(nameof(CrptUtilisationBuilder.BuildBody))
            .Should().NotBeNull();
        typeof(CrptGisMtClient).GetMethod("SendUtilisationAsync").Should().NotBeNull();
    }

    [Fact]
    public void Checklist6_UtilisationBuilder_ProducesReportBody()
    {
        var request = new UtilisationReportRequest
        {
            ProductGroup = "chemistry",
            RawPayloads = [$"010000000000000021SYN001{Gs}91EE12{Gs}92SYNTHETICPAYLOAD001="],
            ProductionDate = "2026-01-15",
            ExpirationDate = "2029-01-15",
        };

        var body = CrptUtilisationBuilder.BuildBody(request);

        body.Should().ContainKey("productGroup");
        body.Should().ContainKey("sntins");
        body.Should().ContainKey("attributes");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "DoubleMark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
