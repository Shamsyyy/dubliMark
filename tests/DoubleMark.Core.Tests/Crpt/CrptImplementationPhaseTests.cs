using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptImplementationPhaseTests
{
    public static IEnumerable<object[]> MvpPhaseDoDItems =>
        CrptPhaseDoDValidator.EvaluateMvpPhases()
            .Select(item => new object[] { item });

    public static IEnumerable<object[]> PhaseEBacklogItems =>
        CrptPhaseDoDValidator.EvaluatePhaseEBacklog()
            .Select(item => new object[] { item });

    [Theory]
    [MemberData(nameof(MvpPhaseDoDItems))]
    public void MvpPhaseDoD_IsComplete(CrptPhaseDoDValidator.PhaseDoDResult item)
    {
        item.IsComplete.Should().BeTrue(
            because: $"Phase {item.Phase} {item.Id} ({item.Task}) must satisfy DoD. Gap: {item.GapNote}");
    }

    [Theory]
    [MemberData(nameof(MvpPhaseDoDItems))]
    public void MvpPhaseDoD_PartialItemsAreDocumented(CrptPhaseDoDValidator.PhaseDoDResult item)
    {
        if (!item.IsPartial)
            return;

        item.GapNote.Should().NotBeNullOrWhiteSpace(
            because: $"Partial DoD {item.Phase} {item.Id} must document remaining scope");
    }

    [Theory]
    [MemberData(nameof(PhaseEBacklogItems))]
    public void PhaseE_BacklogItemsAreNotComplete(CrptPhaseDoDValidator.PhaseDoDResult item)
    {
        item.IsComplete.Should().BeFalse(
            because: $"Phase E item {item.Id} must remain backlog until Phase 2");
    }

    [Fact]
    public void PhaseE_IntroduceGoodsStub_IsGuarded()
    {
        var guarded = CrptPhaseDoDValidator.EvaluateAll()
            .Single(r => r.Id == "E5");
        guarded.IsComplete.Should().BeTrue("IntroduceGoodsAsync NotImplemented guard is required for Phase 2 boundary");
    }

    [Fact]
    public void DiRegistration_IncludesPrintServiceAndTokenRefresh()
    {
        CrptPhaseDoDValidator.DiRegistersPhaseComponents().Should().BeTrue();
    }

    [Fact]
    public void PhaseSummary_AllMvpItemsPassExceptDocumentedPartial()
    {
        var mvp = CrptPhaseDoDValidator.EvaluateMvpPhases();
        var incomplete = mvp.Where(i => !i.IsComplete).ToList();
        incomplete.Should().BeEmpty(
            because: "all MVP phase DoD rows A–D should pass automated checks");
    }
}
