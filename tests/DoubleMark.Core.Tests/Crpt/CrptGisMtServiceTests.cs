using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.Settings;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptGisMtServiceTests : IDisposable
{
    private const string TestGtin = "00000000000000";
    private const char Gs = (char)0x1D;

    private readonly string _tempDirectory;
    private readonly string _catalogPath;
    private readonly CrptSettingsStore _settingsStore;
    private readonly CrptOrderRepository _orderRepository;
    private readonly CrptProductCatalogStore _catalogStore;
    private readonly CrptGisMtServiceTestHandler _handler = new();
    private readonly X509Certificate2 _certificate;

    public CrptGisMtServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DoubleMark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _catalogPath = Path.Combine(_tempDirectory, "crpt-catalog.json");
        _settingsStore = new CrptSettingsStore(_tempDirectory);
        _orderRepository = new CrptOrderRepository();
        _catalogStore = new CrptProductCatalogStore(_catalogPath);
        _certificate = CreateTestCertificate();

        _settingsStore.Save(new CrptSettings
        {
            Inn = "0000000000",
            ContactPerson = "Test Contact",
            ProductGroups = ["chemistry"],
        }, new CrptSecrets
        {
            OmsId = "00000000-0000-4000-8000-000000000001",
            ConnectionId = "00000000-0000-4000-8000-000000000002",
        });

        _catalogStore.Save(
        [
            new CrptProductCatalogItem
            {
                Gtin = TestGtin,
                Name = "Synthetic chemistry item",
                ProductGroup = "chemistry",
                CanOrderCodes = true,
                CertificateDocType = "CONFORMITY_DECLARATION",
                CertificateDocNumber = "RU-SYN-0001",
                CertificateDocDate = "2025-12-01T00:00:00.000Z",
                SyncedAt = DateTimeOffset.UtcNow,
            },
        ]);
    }

    public void Dispose()
    {
        _certificate.Dispose();
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task SendUtilisationForOrderAsync_UpdatesPrintedCodesToUtilisationSent()
    {
        const string orderId = "order-local-1";
        await SeedOrderAsync(orderId);
        var printed = await SeedPrintedCodeAsync(orderId, index: 1);

        var service = CreateService();
        var result = await service.SendUtilisationForOrderAsync(orderId, CancellationToken.None);

        result.DocumentId.Should().Be("util-doc-001");
        result.CodesSubmitted.Should().Be(1);
        _handler.SubmitCalls.Should().Be(1);
        _handler.LastAuthorization!.Parameter.Should().Be("synthetic-united-jwt");
        _handler.LastRequestBody.Should().Contain("certificateDocumentNumber");
        _handler.LastRequestBody.Should().Contain("RU-SYN-0001");

        var updated = await _orderRepository.GetCodesByIdsAsync([printed.Id]);
        updated.Should().ContainSingle();
        updated[0].Status.Should().Be(CrptCodeLifecycleStatus.UtilisationSent);
    }

    [Fact]
    public async Task SendUtilisationForCodesAsync_RejectsNonPrintedCodes()
    {
        const string orderId = "order-local-2";
        await SeedOrderAsync(orderId);
        await _orderRepository.SaveCodesAsync(orderId, [SyntheticMarkingCode(2)], CancellationToken.None);
        var codes = await _orderRepository.ListCodesByOrderAsync(orderId);
        var receivedCode = codes[0];

        var service = CreateService();
        var act = () => service.SendUtilisationForCodesAsync([receivedCode.Id], CancellationToken.None);

        var ex = await act.Should().ThrowAsync<CrptGisMtException>();
        ex.Which.Message.Should().NotContain("SYNTHETICPAYLOAD");
        ex.Which.Message.Should().Contain(receivedCode.Id.ToString());
        _handler.SubmitCalls.Should().Be(0);
    }

    [Fact]
    public async Task SendUtilisationForOrderAsync_SkipsNonPrintedCodes()
    {
        const string orderId = "order-local-3";
        await SeedOrderAsync(orderId);
        await _orderRepository.SaveCodesAsync(orderId, [SyntheticMarkingCode(3)], CancellationToken.None);
        await SeedPrintedCodeAsync(orderId, index: 4);

        var service = CreateService();
        var result = await service.SendUtilisationForOrderAsync(orderId, CancellationToken.None);

        result.CodesSubmitted.Should().Be(1);
        _handler.LastRequestBody.Should().Contain("SYN004");
        _handler.LastRequestBody.Should().NotContain("SYN003");
    }

    [Fact]
    public async Task IntroduceGoodsAsync_ThrowsNotImplementedForPhase2()
    {
        var service = CreateService();

        var act = () => service.IntroduceGoodsAsync([SyntheticMarkingCode(1)], CancellationToken.None);

        await act.Should().ThrowAsync<NotImplementedException>()
            .WithMessage("*LP_INTRODUCE_GOODS*");
    }

    private CrptGisMtService CreateService()
    {
        return new CrptGisMtService(
            new FakeAuthService(),
            _settingsStore,
            new TestCertificateProvider(_certificate),
            _catalogStore,
            _orderRepository,
            connection =>
            {
                var suzHttp = new HttpClient(_handler) { BaseAddress = new Uri("https://suz.test/") };
                return new CrptGisMtClient(connection, suzHttpClient: suzHttp, signOrderBody: (_, _) => "synthetic-signature-base64");
            });
    }

    private async Task SeedOrderAsync(string orderId)
    {
        await _orderRepository.SaveAsync(
            new CrptSuzOrder(
                LocalId: orderId,
                RemoteOrderId: "remote-order",
                Gtin: TestGtin,
                RequestedQuantity: 1,
                ReceivedQuantity: 1,
                ProductGroup: "chemistry",
                RemoteStatus: SuzOrderRemoteStatus.Closed,
                CreatedAt: DateTimeOffset.UtcNow,
                CompletedAt: DateTimeOffset.UtcNow,
                ErrorMessage: null),
            CancellationToken.None);
    }

    private async Task<CrptMarkingCodeItem> SeedPrintedCodeAsync(string orderId, int index)
    {
        await _orderRepository.SaveCodesAsync(orderId, [SyntheticMarkingCode(index)], CancellationToken.None);
        var code = (await _orderRepository.ListCodesByOrderAsync(orderId)).Single(c =>
            c.RawPayload == SyntheticMarkingCode(index));
        var printed = code with
        {
            Status = CrptCodeLifecycleStatus.Printed,
            PrintedAt = DateTimeOffset.UtcNow,
        };
        await _orderRepository.UpdateCodeAsync(printed, CancellationToken.None);
        return printed;
    }

    private static string SyntheticMarkingCode(int index) =>
        $"010000000000000021SYN{index:D3}{Gs}91EE12{Gs}92SYNTHETICPAYLOAD{index:D3}=";

    private static X509Certificate2 CreateTestCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=DoubleMark Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private sealed class FakeAuthService : ICrptAuthService
    {
        public DateTimeOffset? TokenExpiresAt => DateTimeOffset.UtcNow.AddHours(1);

        public Task<string> GetValidTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("synthetic-united-jwt");

        public Task RefreshTokenAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestCertificateProvider(X509Certificate2 certificate) : ICrptCertificateProvider
    {
        public X509Certificate2 FindCertificate(CrptConnectionSettings settings) => certificate;

        public IReadOnlyList<CrptCertificateDescriptor> ListEligibleCertificates(string? innFilter = null) =>
            [new CrptCertificateDescriptor(certificate.Subject, certificate.Thumbprint, certificate.NotAfter)];
    }

    private sealed class CrptGisMtServiceTestHandler : HttpMessageHandler
    {
        public int SubmitCalls { get; private set; }
        public string? LastRequestBody { get; private set; }
        public AuthenticationHeaderValue? LastAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            SubmitCalls++;
            LastAuthorization = request.Headers.Authorization;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"reportId":"util-doc-001"}"""),
            };
        }
    }
}
