using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DoubleMark.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptGisMtClientTests : IDisposable
{
    private const string TestOmsId = "00000000-0000-4000-8000-000000000001";
    private const char Gs = (char)0x1D;

    private readonly CrptGisMtTestHandler _handler;
    private readonly CrptGisMtClient _client;
    private readonly X509Certificate2 _certificate;

    public CrptGisMtClientTests()
    {
        _handler = new CrptGisMtTestHandler();
        var suzHttp = new HttpClient(_handler) { BaseAddress = new Uri("https://suz.test/") };
        var trueApiHttp = new HttpClient { BaseAddress = new Uri("https://trueapi.test/") };
        _client = new CrptGisMtClient(
            new CrptConnectionSettings
            {
                OmsId = TestOmsId,
                SuzBaseUrl = "https://suz.test/",
                TrueApiBaseUrl = "https://trueapi.test/",
                ProductGroup = "chemistry",
            },
            trueApiHttp,
            suzHttp,
            (_, _) => "synthetic-signature-base64");
        _certificate = CreateTestCertificate();
    }

    public void Dispose()
    {
        _client.Dispose();
        _certificate.Dispose();
    }

    [Fact]
    public async Task SendUtilisationAsync_PostsSignedBodyWithBearerJwt()
    {
        _handler.UtilisationResponse = (HttpStatusCode.OK, """{"reportId":"util-report-001"}""");

        var request = new UtilisationReportRequest
        {
            ProductGroup = "chemistry",
            RawPayloads = [SyntheticMarkingCode(1)],
            ProductionDate = "2026-03-01",
            ExpirationDate = "2029-03-01",
        };

        var result = await _client.SendUtilisationAsync(
            "synthetic-jwt-token",
            _certificate,
            request,
            CancellationToken.None);

        result.DocumentId.Should().Be("util-report-001");
        _handler.LastMethod.Should().Be(HttpMethod.Post);
        _handler.LastCapturedPath.Should().Contain("utilisation");
        _handler.LastCapturedPath.Should().Contain($"omsId={TestOmsId}");
        _handler.LastAuthorization.Should().NotBeNull();
        _handler.LastAuthorization!.Scheme.Should().Be("Bearer");
        _handler.LastAuthorization.Parameter.Should().Be("synthetic-jwt-token");
        _handler.LastSignatureHeader.Should().NotBeNullOrWhiteSpace();
        _handler.LastOmsHeader.Should().Be(TestOmsId);
        _handler.LastRequestBody.Should().Contain("productionDate");
        _handler.LastRequestBody.Should().Contain("2026-03-01");
    }

    [Fact]
    public async Task SendUtilisationAsync_DoesNotEchoRawPayloadInThrownError()
    {
        var code = SyntheticMarkingCode(1);
        _handler.UtilisationResponse = (HttpStatusCode.BadRequest, code + " invalid payload");

        var request = new UtilisationReportRequest
        {
            ProductGroup = "chemistry",
            RawPayloads = [code],
            ProductionDate = "2026-03-01",
            ExpirationDate = "2029-03-01",
        };

        var act = () => _client.SendUtilisationAsync(
            "synthetic-jwt-token",
            _certificate,
            request,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<CrptGisMtException>();
        ex.Which.Message.Should().NotContain("SYNTHETICPAYLOAD");
        ex.Which.Message.Should().NotContain(Gs.ToString());
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

    private sealed class CrptGisMtTestHandler : HttpMessageHandler
    {
        public (HttpStatusCode StatusCode, string Body) UtilisationResponse { get; set; } =
            (HttpStatusCode.OK, """{"reportId":"default"}""");

        public string? LastCapturedPath { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastRequestBody { get; private set; }
        public string? LastSignatureHeader { get; private set; }
        public string? LastOmsHeader { get; private set; }
        public AuthenticationHeaderValue? LastAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastMethod = request.Method;
            LastCapturedPath = request.RequestUri?.PathAndQuery;
            LastAuthorization = request.Headers.Authorization;
            LastSignatureHeader = request.Headers.TryGetValues("X-Signature", out var sig)
                ? sig.FirstOrDefault()
                : null;
            LastOmsHeader = request.Headers.TryGetValues("X-OMS-Id", out var oms)
                ? oms.FirstOrDefault()
                : null;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(UtilisationResponse.StatusCode)
            {
                Content = new StringContent(UtilisationResponse.Body),
            };
        }
    }
}
