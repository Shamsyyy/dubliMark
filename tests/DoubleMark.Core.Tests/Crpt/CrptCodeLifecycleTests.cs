using DoubleMark.Core.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptCodeLifecycleTests
{
    [Theory]
    [InlineData(CrptCodeLifecycleStatus.Received, CrptCodeLifecycleStatus.QueuedForPrint, true)]
    [InlineData(CrptCodeLifecycleStatus.QueuedForPrint, CrptCodeLifecycleStatus.Printed, true)]
    [InlineData(CrptCodeLifecycleStatus.Printed, CrptCodeLifecycleStatus.UtilisationSent, true)]
    [InlineData(CrptCodeLifecycleStatus.UtilisationSent, CrptCodeLifecycleStatus.InCirculation, true)]
    [InlineData(CrptCodeLifecycleStatus.Received, CrptCodeLifecycleStatus.Printed, false)]
    [InlineData(CrptCodeLifecycleStatus.Printed, CrptCodeLifecycleStatus.InCirculation, false)]
    [InlineData(CrptCodeLifecycleStatus.InCirculation, CrptCodeLifecycleStatus.Printed, false)]
    public void LifecycleTransitions_FollowManufacturerSequence(
        CrptCodeLifecycleStatus from,
        CrptCodeLifecycleStatus to,
        bool expected)
    {
        CrptCodeLifecycleTransitions.CanTransition(from, to).Should().Be(expected);
    }

    [Theory]
    [InlineData(CrptCodeLifecycleStatus.Received, CrptCodeLifecycleStatus.Error, true)]
    [InlineData(CrptCodeLifecycleStatus.Printed, CrptCodeLifecycleStatus.Error, true)]
    [InlineData(CrptCodeLifecycleStatus.Error, CrptCodeLifecycleStatus.Printed, false)]
    public void LifecycleTransitions_AllowErrorFromActiveStates(
        CrptCodeLifecycleStatus from,
        CrptCodeLifecycleStatus to,
        bool expected)
    {
        CrptCodeLifecycleTransitions.CanTransition(from, to).Should().Be(expected);
    }

    [Theory]
    [InlineData("PENDING", SuzOrderRemoteStatus.Pending)]
    [InlineData("READY", SuzOrderRemoteStatus.Ready)]
    [InlineData("CLOSED", SuzOrderRemoteStatus.Closed)]
    [InlineData("ERROR", SuzOrderRemoteStatus.Error)]
    [InlineData("failed", SuzOrderRemoteStatus.Error)]
    [InlineData("", SuzOrderRemoteStatus.Unknown)]
    public void SuzOrderRemoteStatusMapper_MapsKnownValues(string remote, SuzOrderRemoteStatus expected)
    {
        SuzOrderRemoteStatusMapper.FromRemoteStatus(remote).Should().Be(expected);
    }

    [Fact]
    public void SuzOrderRemoteStatusMapper_ReadyForDownloadOnlyWhenReady()
    {
        SuzOrderRemoteStatusMapper.IsReadyForDownload(SuzOrderRemoteStatus.Ready).Should().BeTrue();
        SuzOrderRemoteStatusMapper.IsReadyForDownload(SuzOrderRemoteStatus.Pending).Should().BeFalse();
    }
}
