using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

/// <summary>
/// §14.1 — SUZ status JSON → <see cref="CrptSuzOrder.RemoteStatus"/> mapping chain.
/// </summary>
public class CrptSuzOrderStatusMappingTests
{
    private static readonly DateTimeOffset CreatedAt = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("""{"orderStatus":"PENDING"}""", SuzOrderRemoteStatus.Pending)]
    [InlineData("""{"orderStatus":"CREATED"}""", SuzOrderRemoteStatus.Pending)]
    [InlineData("""{"bufferStatus":"ACTIVE"}""", SuzOrderRemoteStatus.Ready)]
    [InlineData("""{"orderStatus":"READY","bufferStatus":"ACTIVE"}""", SuzOrderRemoteStatus.Ready)]
    [InlineData("""{"orderStatus":"CLOSED"}""", SuzOrderRemoteStatus.Closed)]
    [InlineData("""{"orderStatus":"REJECTED"}""", SuzOrderRemoteStatus.Error)]
    [InlineData("""{"orderStatus":"FAILED","errorMessage":"invalid gtin"}""", SuzOrderRemoteStatus.Error)]
    public void ParseOrderStatus_MapsToCrptSuzOrderRemoteStatus(string json, SuzOrderRemoteStatus expected)
    {
        var parsed = CrptSuzResponseParser.ParseOrderStatus(json);

        parsed.Status.Should().Be(expected);
    }

    [Fact]
    public void ApplyRemoteStatus_UpdatesCrptSuzOrderWithoutMutatingOtherFields()
    {
        var order = CreateSampleOrder(SuzOrderRemoteStatus.Pending);
        const string json = """{"orderStatus":"READY","bufferStatus":"ACTIVE"}""";

        var parsed = CrptSuzResponseParser.ParseOrderStatus(json);
        var updated = order with { RemoteStatus = parsed.Status };

        updated.RemoteStatus.Should().Be(SuzOrderRemoteStatus.Ready);
        updated.LocalId.Should().Be(order.LocalId);
        updated.Gtin.Should().Be(order.Gtin);
        updated.RequestedQuantity.Should().Be(order.RequestedQuantity);
        updated.ProductGroup.Should().Be(order.ProductGroup);
    }

    [Fact]
    public void ParseOrderStatus_ReadyStatus_IsReadyForDownloadOnCrptSuzOrder()
    {
        const string json = """{"orderStatus":"READY","bufferStatus":"ACTIVE"}""";

        var parsed = CrptSuzResponseParser.ParseOrderStatus(json);
        var order = CreateSampleOrder(SuzOrderRemoteStatus.Pending) with { RemoteStatus = parsed.Status };

        parsed.IsReadyForDownload.Should().BeTrue();
        SuzOrderRemoteStatusMapper.IsReadyForDownload(order.RemoteStatus).Should().BeTrue();
    }

    [Fact]
    public void ParseOrderStatus_ErrorStatus_CarriesMessageForCrptSuzOrder()
    {
        const string json = """{"orderStatus":"REJECTED","errorMessage":"gtin not in catalog"}""";

        var parsed = CrptSuzResponseParser.ParseOrderStatus(json);
        var failed = CreateSampleOrder(SuzOrderRemoteStatus.Pending) with
        {
            RemoteStatus = parsed.Status,
            ErrorMessage = parsed.ErrorMessage,
        };

        failed.RemoteStatus.Should().Be(SuzOrderRemoteStatus.Error);
        failed.ErrorMessage.Should().Be("gtin not in catalog");
        parsed.IsTerminalFailure.Should().BeTrue();
    }

    [Fact]
    public void ParseOrderStatus_ClosedStatus_MapsToTerminalSuccess()
    {
        const string json = """{"orderStatus":"CLOSED"}""";

        var parsed = CrptSuzResponseParser.ParseOrderStatus(json);
        var closed = CreateSampleOrder(SuzOrderRemoteStatus.Ready) with
        {
            RemoteStatus = parsed.Status,
            CompletedAt = DateTimeOffset.UtcNow,
        };

        closed.RemoteStatus.Should().Be(SuzOrderRemoteStatus.Closed);
        SuzOrderRemoteStatusMapper.IsTerminalSuccess(closed.RemoteStatus).Should().BeTrue();
    }

    private static CrptSuzOrder CreateSampleOrder(SuzOrderRemoteStatus status) => new(
        LocalId: "local-order-001",
        RemoteOrderId: "00000000-0000-4000-8000-000000000099",
        Gtin: "00000000000000",
        RequestedQuantity: 10,
        ReceivedQuantity: 0,
        ProductGroup: "chemistry",
        RemoteStatus: status,
        CreatedAt: CreatedAt,
        CompletedAt: null,
        ErrorMessage: null);
}
