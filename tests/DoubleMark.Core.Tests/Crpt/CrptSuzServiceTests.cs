using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.Settings;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptSuzServiceTests : IDisposable
{
    private const string TestGtin = "00000000000000";
    private const char Gs = (char)0x1D;

    private readonly string _tempDirectory;
    private readonly CrptSettingsStore _settingsStore;
    private readonly CrptOrderRepository _orderRepository;
    private readonly X509Certificate2 _certificate;
    private readonly FakeSuzClient _fakeClient = new();
    private int _statusPollCount;

    public CrptSuzServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DoubleMark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _settingsStore = new CrptSettingsStore(_tempDirectory);
        _orderRepository = new CrptOrderRepository();
        _certificate = CreateTestCertificate();

        _settingsStore.Save(new CrptSettings
        {
            Inn = "0000000000",
            ContactPerson = "Test Contact",
            ProductGroups = ["chemistry"],
            ProductGroupTemplateDefaults = { ["chemistry"] = 46 },
        }, new CrptSecrets
        {
            OmsId = "00000000-0000-4000-8000-000000000001",
            ConnectionId = "00000000-0000-4000-8000-000000000002",
        });
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
    public async Task PollUntilReadyAsync_ReturnsReadyAfterNPolls()
    {
        _statusPollCount = 0;
        _fakeClient.StatusFactory = () =>
        {
            _statusPollCount++;
            return _statusPollCount < 3
                ? new CrptSuzOrderStatus(SuzOrderRemoteStatus.Pending, null, """{"orderStatus":"PENDING"}""")
                : new CrptSuzOrderStatus(SuzOrderRemoteStatus.Ready, null, """{"orderStatus":"READY"}""");
        };

        var service = CreateService();
        var status = await service.PollUntilReadyAsync(
            "remote-order-id",
            TestGtin,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(10),
            cancellationToken: CancellationToken.None);

        status.Should().Be(SuzOrderRemoteStatus.Ready);
        _statusPollCount.Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task DownloadCodesAsync_StoresValidatedCodesInRepository()
    {
        var code = SyntheticMarkingCode(1);
        _fakeClient.Blocks =
        [
            new CrptSuzCodesBlock([code], "block-1", true),
        ];

        var service = CreateService();
        var items = await service.DownloadCodesAsync(
            localOrderId: "local-order-1",
            remoteOrderId: "remote-order-1",
            gtin: TestGtin,
            quantity: 1,
            cancellationToken: CancellationToken.None);

        items.Should().ContainSingle();
        items[0].RawPayload.Should().Be(code);
        items[0].Status.Should().Be(CrptCodeLifecycleStatus.Received);
    }

    [Fact]
    public async Task CreateAndDownloadOrderAsync_PersistsClosedOrderWithCodes()
    {
        var code = SyntheticMarkingCode(1);
        _fakeClient.CreateOrderResult = "remote-order-complete";
        _fakeClient.StatusFactory = () => new CrptSuzOrderStatus(
            SuzOrderRemoteStatus.Ready,
            null,
            """{"orderStatus":"READY"}""");
        _fakeClient.Blocks =
        [
            new CrptSuzCodesBlock([code], "block-1", true),
        ];

        var service = CreateService();
        var order = await service.CreateAndDownloadOrderAsync(
            TestGtin,
            quantity: 1,
            productGroup: "chemistry",
            cancellationToken: CancellationToken.None);

        order.RemoteOrderId.Should().Be("remote-order-complete");
        order.ReceivedQuantity.Should().Be(1);
        order.RemoteStatus.Should().Be(SuzOrderRemoteStatus.Closed);
        _fakeClient.CloseOrderCalls.Should().Be(1);

        var codes = await _orderRepository.ListCodesByOrderAsync(order.LocalId);
        codes.Should().ContainSingle();
    }

    private CrptSuzService CreateService()
    {
        return new CrptSuzService(
            _settingsStore,
            new TestCertificateProvider(_certificate),
            _orderRepository,
            _ => _fakeClient,
            (_, _, _) => Task.FromResult("synthetic-suz-token"));
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

    private sealed class TestCertificateProvider(X509Certificate2 certificate) : ICrptCertificateProvider
    {
        public X509Certificate2 FindCertificate(CrptConnectionSettings settings) => certificate;

        public IReadOnlyList<CrptCertificateDescriptor> ListEligibleCertificates(string? innFilter = null) =>
            [new CrptCertificateDescriptor(certificate.Subject, certificate.Thumbprint, certificate.NotAfter)];
    }

    private sealed class FakeSuzClient : ICrptSuzClient
    {
        public string CreateOrderResult { get; set; } = "remote-order-id";
        public Func<CrptSuzOrderStatus>? StatusFactory { get; set; }
        public IReadOnlyList<CrptSuzCodesBlock> Blocks { get; set; } = [];
        public int CloseOrderCalls { get; private set; }

        public Task<string> CreateOrderAsync(
            string suzClientToken,
            X509Certificate2 certificate,
            CreateSuzOrderRequest request,
            CancellationToken ct = default)
        {
            _ = suzClientToken;
            _ = certificate;
            _ = ct;
            request.ProductGroup.Should().NotBeNullOrWhiteSpace();
            request.Products[0].Gtin.Should().Be(TestGtin);
            return Task.FromResult(CreateOrderResult);
        }

        public Task<CrptSuzOrderStatus> GetOrderStatusAsync(
            string suzClientToken,
            string remoteOrderId,
            string gtin,
            CancellationToken ct = default) =>
            Task.FromResult(StatusFactory?.Invoke()
                ?? new CrptSuzOrderStatus(SuzOrderRemoteStatus.Ready, null, """{"orderStatus":"READY"}"""));

        public Task<CrptSuzCodesBlock> GetCodesBlockAsync(
            string suzClientToken,
            string remoteOrderId,
            string gtin,
            int quantity,
            string? lastBlockId,
            CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(lastBlockId) && Blocks.Count > 1)
                return Task.FromResult(Blocks[1]);

            return Task.FromResult(Blocks[0]);
        }

        public Task CloseOrderAsync(string suzClientToken, string remoteOrderId, CancellationToken ct = default)
        {
            CloseOrderCalls++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
