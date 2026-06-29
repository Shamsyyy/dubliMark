using System.Net;
using System.Net.Sockets;
using DoubleMark.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptNkHttpTests
{
    [Fact]
    public void CreateClient_UsesConfiguredTimeout()
    {
        using var client = CrptNkHttpFactory.CreateClient(CrptUrl.ProductionNkBaseUrl, 240);
        client.Timeout.Should().Be(TimeSpan.FromSeconds(240));
        client.BaseAddress!.Host.Should().Be(CrptUrl.ProductionNkPunycodeHost);
    }

    [Fact]
    public void NkHttpTimeout_DefaultIs180Seconds()
    {
        CrptRiskMitigations.NkHttpTimeoutSeconds.Should().Be(180);
    }

    [Fact]
    public void NkConnectionRetry_HasThreeAttempts()
    {
        CrptRiskMitigations.NkConnectionRetryAttempts.Should().Be(3);
    }

    [Fact]
    public void IsProductionNkHost_DetectsPunycodeProductionHost()
    {
        CrptUrl.IsProductionNkHost(CrptUrl.ProductionNkPunycodeHost).Should().BeTrue();
        CrptUrl.IsProductionNkHost("api.nk.sandbox.crptech.ru").Should().BeFalse();
    }

    [Fact]
    public async Task GetProductListAsync_RetriesTransientConnectionFailures()
    {
        var handler = new FlakyConnectionHandler(
            failuresBeforeSuccess: 2,
            responseBody: """{"result":{"total":0,"goods":[]}}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://nk.test/") };
        using var client = new CrptNkClient(
            new CrptConnectionSettings { NkBaseUrl = "https://nk.test/" },
            bearerToken: "jwt",
            httpClient: http);

        await client.GetProductListAsync(limit: 1, offset: 0);

        handler.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task GetProductListAsync_ProductionTimeoutMessage_SuggestsSandbox()
    {
        var handler = new AlwaysFailingConnectionHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri(CrptUrl.ProductionNkBaseUrl) };
        using var client = new CrptNkClient(
            new CrptConnectionSettings { NkBaseUrl = CrptUrl.ProductionNkBaseUrl, NkHttpTimeoutSeconds = 30 },
            bearerToken: "jwt",
            httpClient: http);

        var act = () => client.GetProductListAsync(limit: 1, offset: 0);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("Порт 443");
        ex.Which.Message.Should().Contain(CrptUrl.SandboxNkBaseUrl);
    }

    private sealed class FlakyConnectionHandler(int failuresBeforeSuccess, string responseBody) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts <= failuresBeforeSuccess)
            {
                throw new HttpRequestException(
                    "connect failed",
                    new SocketException((int)SocketError.TimedOut));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            });
        }
    }

    private sealed class AlwaysFailingConnectionHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException(
                "connect failed",
                new SocketException((int)SocketError.TimedOut));
    }
}
