using System.Net;
using System.Net.Http.Headers;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptSuzClientTests : IDisposable
{
    private const string TestOmsId = "00000000-0000-4000-8000-000000000001";
    private const string TestGtin = "00000000000000";
    private const string TestOrderId = "00000000-0000-4000-8000-000000000099";
    private const char Gs = (char)0x1D;

    private readonly CrptSuzTestHandler _handler;
    private readonly CrptSuzClient _client;

    public CrptSuzClientTests()
    {
        _handler = new CrptSuzTestHandler();
        var http = new HttpClient(_handler) { BaseAddress = new Uri("https://suz.test/") };
        _client = new CrptSuzClient(
            new CrptConnectionSettings { OmsId = TestOmsId, SuzBaseUrl = "https://suz.test/" },
            http,
            disposeHttpClient: false);
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public void BuildOrderBody_MatchesRequestBuilderShape()
    {
        var body = CrptSuzClient.BuildOrderBody(new CreateSuzOrderRequest
        {
            ProductGroup = "chemistry",
            ContactPerson = "Test Contact",
            Products =
            [
                new CreateSuzOrderProduct
                {
                    Gtin = TestGtin,
                    Quantity = 10,
                    TemplateId = 46,
                },
            ],
        });

        body["productGroup"].Should().Be("chemistry");
        var products = body["products"].Should().BeAssignableTo<object[]>().Subject;
        var product = products[0].Should().BeOfType<Dictionary<string, object>>().Subject;
        product["gtin"].Should().Be(TestGtin);
        product["templateId"].Should().Be(46);
    }

    [Fact]
    public async Task GetOrderStatusAsync_MapsReadyStatus()
    {
        _handler.Responses["order/status"] = (HttpStatusCode.OK, """{"orderStatus":"READY","bufferStatus":"ACTIVE"}""");

        var status = await _client.GetOrderStatusAsync("suz-token", TestOrderId, TestGtin, CancellationToken.None);

        status.Status.Should().Be(SuzOrderRemoteStatus.Ready);
        status.IsReadyForDownload.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrderStatusAsync_MapsRejectedAsError()
    {
        _handler.Responses["order/status"] = (HttpStatusCode.OK, """{"orderStatus":"REJECTED","errorMessage":"invalid gtin"}""");

        var status = await _client.GetOrderStatusAsync("suz-token", TestOrderId, TestGtin, CancellationToken.None);

        status.Status.Should().Be(SuzOrderRemoteStatus.Error);
        status.IsTerminalFailure.Should().BeTrue();
        status.ErrorMessage.Should().Be("invalid gtin");
    }

    [Fact]
    public async Task GetCodesBlockAsync_ParsesCodesWithoutLoggingPayload()
    {
        var code = SyntheticMarkingCode(1);
        _handler.Responses["codes|first"] = (HttpStatusCode.OK, $$"""{"blockId":"block-1","isLast":true,"codes":["{{SyntheticMarkingCodeJson(1)}}"]}""");

        var block = await _client.GetCodesBlockAsync("suz-token", TestOrderId, TestGtin, 1, lastBlockId: null, CancellationToken.None);

        block.Codes.Should().ContainSingle().Which.Should().Be(code);
        block.IsLast.Should().BeTrue();
        _handler.LastCapturedPath.Should().Contain("quantity=1");
    }

    [Fact]
    public async Task GetCodesBlockAsync_FollowsBlockIdPagination()
    {
        var firstCode = SyntheticMarkingCode(1);
        var secondCode = SyntheticMarkingCode(2);

        _handler.Responses["codes|first"] = (HttpStatusCode.OK, $$"""{"blockId":"block-1","isLast":false,"codes":["{{SyntheticMarkingCodeJson(1)}}"]}""");
        _handler.Responses["codes|next"] = (HttpStatusCode.OK, $$"""{"blockId":"block-2","isLast":true,"codes":["{{SyntheticMarkingCodeJson(2)}}"]}""");

        var first = await _client.GetCodesBlockAsync("suz-token", TestOrderId, TestGtin, 2, null, CancellationToken.None);
        var second = await _client.GetCodesBlockAsync("suz-token", TestOrderId, TestGtin, 2, first.BlockId, CancellationToken.None);

        first.IsLast.Should().BeFalse();
        second.IsLast.Should().BeTrue();
        _handler.LastCapturedPath.Should().Contain("blockId=block-1");
    }

    [Fact]
    public void ValidateAndParseCodes_RejectsNonGs1Payload()
    {
        var block = new CrptSuzCodesBlock(["not-a-marking-code"], "block-1", true);

        var act = () => CrptSuzClient.ValidateAndParseCodes(block);

        act.Should().Throw<CrptSuzException>();
    }

    [Fact]
    public async Task CloseOrderAsync_PostsToCloseEndpoint()
    {
        _handler.Responses["order/close"] = (HttpStatusCode.OK, """{"status":"CLOSED"}""");

        await _client.CloseOrderAsync("suz-token", TestOrderId, CancellationToken.None);

        _handler.LastMethod.Should().Be(HttpMethod.Post);
        _handler.LastCapturedPath.Should().Contain("order/close");
        _handler.LastRequestBody.Should().Contain(TestOrderId);
    }

    [Fact]
    public async Task CreateSuzRequest_SendsClientTokenAndOmsHeaders()
    {
        _handler.Responses["order/status"] = (HttpStatusCode.OK, """{"orderStatus":"PENDING"}""");

        await _client.GetOrderStatusAsync("synthetic-suz-token", TestOrderId, TestGtin, CancellationToken.None);

        _handler.LastClientToken.Should().Be("synthetic-suz-token");
        _handler.LastOmsHeader.Should().Be(TestOmsId);
        _handler.LastAuthorization.Should().BeNull("SUZ calls must not send Authorization with clientToken");
    }

    private static string SyntheticMarkingCode(int index) =>
        $"010000000000000021SYN{index:D3}{Gs}91EE12{Gs}92SYNTHETICPAYLOAD{index:D3}=";

    private static string SyntheticMarkingCodeJson(int index) =>
        $"010000000000000021SYN{index:D3}\\u001d91EE12\\u001d92SYNTHETICPAYLOAD{index:D3}=";

    private sealed class CrptSuzTestHandler : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode StatusCode, string Body)> Responses { get; } = new(StringComparer.Ordinal);

        public string? LastCapturedPath { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastRequestBody { get; private set; }
        public string? LastClientToken { get; private set; }
        public string? LastOmsHeader { get; private set; }
        public AuthenticationHeaderValue? LastAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastCapturedPath = request.RequestUri?.PathAndQuery;
            LastClientToken = request.Headers.TryGetValues("clientToken", out var tokens)
                ? tokens.FirstOrDefault()
                : null;
            LastOmsHeader = request.Headers.TryGetValues("X-OMS-Id", out var omsValues)
                ? omsValues.FirstOrDefault()
                : null;
            LastAuthorization = request.Headers.Authorization;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var key = ResolveKey(request);
            if (!Responses.TryGetValue(key, out var response))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body),
            };
        }

        private static string ResolveKey(HttpRequestMessage request)
        {
            var path = request.RequestUri?.AbsolutePath.Trim('/') ?? "";
            var query = request.RequestUri?.Query ?? "";

            if (path.Contains("order/status", StringComparison.Ordinal))
                return "order/status";
            if (path.Contains("order/close", StringComparison.Ordinal))
                return "order/close";
            if (path.Contains("codes", StringComparison.Ordinal))
                return query.Contains("blockId=", StringComparison.Ordinal) ? "codes|next" : "codes|first";

            return path;
        }
    }
}
