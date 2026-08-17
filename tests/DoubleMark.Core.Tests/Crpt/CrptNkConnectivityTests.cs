using DoubleMark.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptNkConnectivityTests
{
    [Fact]
    public void HealthCheckTimeouts_AreFastFailValues()
    {
        CrptNkConnectivity.HealthCheckTimeoutSeconds.Should().Be(20);
        CrptNkConnectivity.HealthCheckConnectTimeoutSeconds.Should().Be(15);
    }

    [Fact]
    public async Task CheckReachableAsync_WhenUnreachable_FailsFast()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var act = () => CrptNkConnectivity.CheckReachableAsync("https://127.0.0.1:1/");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(25));
        ex.Which.Message.Should().Contain("Националь");
    }

    [Fact]
    public async Task TryCheckReachableAsync_WhenUnreachable_ReturnsFailure()
    {
        var (success, error) = await CrptNkConnectivity.TryCheckReachableAsync("https://127.0.0.1:1/");

        success.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CreateClient_WithExplicitConnectTimeout_UsesProvidedValue()
    {
        using var client = CrptNkHttpFactory.CreateClient(CrptUrl.SandboxNkBaseUrl, 60, 18);
        client.Timeout.Should().Be(TimeSpan.FromSeconds(60));
    }
}
