using System.Net;
using System.Net.Http.Headers;
using DoubleMark.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptNkClientTests
{
    [Fact]
    public async Task GetProductListAsync_JwtMode_SendsBearerWithoutApiKeyOrSignature()
    {
        var handler = new NkCaptureHandler("""{"result":{"total":0,"goods":[]}}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://nk.test/") };
        using var client = new CrptNkClient(
            new CrptConnectionSettings { NkBaseUrl = "https://nk.test/" },
            bearerToken: "synthetic-jwt-token",
            httpClient: http);

        await client.GetProductListAsync(limit: 10, offset: 0, goodStatus: "published");

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().EndWith("/v4/product-list");
        handler.LastRequest.RequestUri.Query.Should().Contain("limit=10");
        handler.LastRequest.RequestUri.Query.Should().Contain("offset=0");
        handler.LastRequest.RequestUri.Query.Should().Contain("good_status=published");
        handler.LastRequest.RequestUri.Query.Should().NotContain("apikey=");
        handler.LastRequest.Headers.Authorization.Should().BeEquivalentTo(
            new AuthenticationHeaderValue("Bearer", "synthetic-jwt-token"));
        handler.LastRequest.Headers.Contains("X-Signature").Should().BeFalse();
    }

    [Fact]
    public async Task GetProductListAsync_IncludesFromAndToDateQueryParams()
    {
        var handler = new NkCaptureHandler("""{"result":{"total":0,"goods":[]}}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://nk.test/") };
        using var client = new CrptNkClient(
            new CrptConnectionSettings { NkBaseUrl = "https://nk.test/" },
            bearerToken: "synthetic-jwt-token",
            httpClient: http);

        await client.GetProductListAsync(
            limit: 100,
            offset: 0,
            goodStatus: "published",
            fromDate: "2020-01-01 00:00:00",
            toDate: "2026-06-25 12:00:00");

        handler.LastRequest!.RequestUri!.Query.Should().Contain("from_date=");
        handler.LastRequest.RequestUri.Query.Should().Contain("to_date=");
        handler.LastRequest.RequestUri.Query.Should().Contain("2020-01-01");
        handler.LastRequest.RequestUri.Query.Should().Contain("2026-06-25");
    }

    [Fact]
    public async Task GetFeedProductAsync_ApiKeyMode_SendsApiKeyQueryWithoutBearer()
    {
        var handler = new NkCaptureHandler("""{"result":[]}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://nk.test/") };
        using var client = new CrptNkClient(
            new CrptConnectionSettings { NkBaseUrl = "https://nk.test/" },
            bearerToken: null,
            apiKey: "nk-api-key-value",
            httpClient: http);

        await client.GetFeedProductAsync(["00000000000000"]);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().EndWith("/v3/feed-product");
        handler.LastRequest.RequestUri.Query.Should().Contain("apikey=nk-api-key-value");
        handler.LastRequest.Headers.Authorization.Should().BeNull();
        handler.LastRequest.Headers.Contains("X-Signature").Should().BeFalse();
    }

    [Fact]
    public async Task GetEtagsListAsync_SendsOffsetQuery()
    {
        var handler = new NkCaptureHandler("""{"result":{"goods_count":0,"offset":100,"goods":[]}}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://nk.test/") };
        using var client = new CrptNkClient(
            new CrptConnectionSettings { NkBaseUrl = "https://nk.test/" },
            bearerToken: "synthetic-jwt-token",
            httpClient: http);

        await client.GetEtagsListAsync(offset: 100);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().EndWith("/v3/etagslist");
        handler.LastRequest.RequestUri.Query.Should().Contain("offset=100");
        handler.LastRequest.Headers.Authorization.Should().BeEquivalentTo(
            new AuthenticationHeaderValue("Bearer", "synthetic-jwt-token"));
    }

    [Fact]
    public async Task GetFeedProductByGoodIdAsync_SendsGoodIdQuery()
    {
        var handler = new NkCaptureHandler("""{"result":[]}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://nk.test/") };
        using var client = new CrptNkClient(
            new CrptConnectionSettings { NkBaseUrl = "https://nk.test/" },
            bearerToken: "synthetic-jwt-token",
            httpClient: http);

        await client.GetFeedProductByGoodIdAsync(4242);

        handler.LastRequest!.RequestUri!.AbsolutePath.Should().EndWith("/v3/feed-product");
        handler.LastRequest.RequestUri.Query.Should().Contain("good_id=4242");
    }

    private sealed class NkCaptureHandler(string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            });
        }
    }
}
