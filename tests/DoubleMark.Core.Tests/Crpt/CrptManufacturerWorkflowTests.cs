using DoubleMark.Core.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptManufacturerWorkflowTests
{
    [Fact]
    public void MvpStages_ExcludeIntroduceGoodsPhase2()
    {
        CrptManufacturerWorkflow.MvpStages.Should().NotContain(CrptBusinessProcessStage.IntroduceGoodsPhase2);
        CrptManufacturerWorkflow.Phase2Stages.Should().ContainSingle()
            .Which.Should().Be(CrptBusinessProcessStage.IntroduceGoodsPhase2);
    }

    [Fact]
    public void FullStages_MatchSection3SequenceOrder()
    {
        CrptManufacturerWorkflow.FullStages.Should().Equal(
        [
            CrptBusinessProcessStage.SettingsCheck,
            CrptBusinessProcessStage.Auth,
            CrptBusinessProcessStage.CatalogSync,
            CrptBusinessProcessStage.OrderCreate,
            CrptBusinessProcessStage.OrderPoll,
            CrptBusinessProcessStage.CodesDownload,
            CrptBusinessProcessStage.Print,
            CrptBusinessProcessStage.Utilisation,
            CrptBusinessProcessStage.IntroduceGoodsPhase2,
        ]);
    }

    [Fact]
    public void ValidateStageOrder_AcceptsSpecSequence()
    {
        CrptManufacturerWorkflow.ValidateStageOrder(CrptManufacturerWorkflow.MvpStages).Should().BeTrue();
        CrptManufacturerWorkflow.ValidateStageOrder(CrptManufacturerWorkflow.FullStages).Should().BeTrue();
    }

    [Fact]
    public void ValidateStageOrder_RejectsAuthBeforeSettingsCheck()
    {
        var invalid =
            new[]
            {
                CrptBusinessProcessStage.Auth,
                CrptBusinessProcessStage.SettingsCheck,
            };

        CrptManufacturerWorkflow.ValidateStageOrder(invalid).Should().BeFalse();
    }

    [Fact]
    public void ValidateStageOrder_RejectsCatalogBeforeAuth()
    {
        var invalid =
            new[]
            {
                CrptBusinessProcessStage.SettingsCheck,
                CrptBusinessProcessStage.CatalogSync,
                CrptBusinessProcessStage.Auth,
            };

        CrptManufacturerWorkflow.ValidateStageOrder(invalid).Should().BeFalse();
    }

    [Fact]
    public void ValidateStageOrder_RejectsUtilisationBeforePrint()
    {
        var invalid =
            new[]
            {
                CrptBusinessProcessStage.SettingsCheck,
                CrptBusinessProcessStage.Auth,
                CrptBusinessProcessStage.CatalogSync,
                CrptBusinessProcessStage.OrderCreate,
                CrptBusinessProcessStage.OrderPoll,
                CrptBusinessProcessStage.CodesDownload,
                CrptBusinessProcessStage.Utilisation,
                CrptBusinessProcessStage.Print,
            };

        CrptManufacturerWorkflow.ValidateStageOrder(invalid).Should().BeFalse();
    }

    [Fact]
    public void WorkflowDescriptor_EnforcesStageDependencies()
    {
        CrptManufacturerWorkflowDescriptor.CanReachStage(
                CrptBusinessProcessStage.Auth,
                [CrptBusinessProcessStage.SettingsCheck])
            .Should().BeTrue();

        CrptManufacturerWorkflowDescriptor.CanReachStage(
                CrptBusinessProcessStage.CatalogSync,
                [CrptBusinessProcessStage.SettingsCheck])
            .Should().BeFalse();

        CrptManufacturerWorkflowDescriptor.CanReachStage(
                CrptBusinessProcessStage.OrderCreate,
                [CrptBusinessProcessStage.SettingsCheck, CrptBusinessProcessStage.Auth, CrptBusinessProcessStage.CatalogSync])
            .Should().BeTrue();

        CrptManufacturerWorkflowDescriptor.CanReachStage(
                CrptBusinessProcessStage.Print,
                [CrptBusinessProcessStage.SettingsCheck, CrptBusinessProcessStage.Auth, CrptBusinessProcessStage.CatalogSync])
            .Should().BeFalse();
    }

    [Fact]
    public void IntroduceGoodsPhase2_RequiresUtilisationCompleted()
    {
        CrptManufacturerWorkflow.IsPhase2Stage(CrptBusinessProcessStage.IntroduceGoodsPhase2).Should().BeTrue();

        CrptManufacturerWorkflowDescriptor.GetPrerequisites(CrptBusinessProcessStage.IntroduceGoodsPhase2)
            .Should().Contain(CrptBusinessProcessStage.Utilisation);
    }
}
