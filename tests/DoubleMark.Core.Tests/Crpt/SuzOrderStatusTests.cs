using DoubleMark.Core.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class SuzOrderStatusTests
{
    [Fact]
    public void ParseFromJson_ReadsBufferActiveAsReady()
    {
        const string json = """{ "bufferStatus": "ACTIVE" }""";

        SuzOrderStatus.ParseFromJson(json).Should().Be(SuzOrderRemoteStatus.Ready);
    }

    [Fact]
    public void ParseFromJson_ReadsOrderStatus()
    {
        const string json = """{ "orderStatus": "READY", "bufferStatus": "ACTIVE" }""";

        SuzOrderStatus.ParseFromJson(json).Should().Be(SuzOrderRemoteStatus.Ready);
    }

    [Fact]
    public void ParseFromJson_ReadsRejectedAsError()
    {
        const string json = """{ "orderStatus": "REJECTED" }""";

        SuzOrderStatus.ParseFromJson(json).Should().Be(SuzOrderRemoteStatus.Error);
    }
}
