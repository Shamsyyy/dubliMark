using DoubleMark.Core.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptMvpScopeTests
{
    [Fact]
    public void Manufacturer_IsMvpSupported()
    {
        CrptMvpScope.IsMvpSupported(CrptOrganizationRole.Manufacturer).Should().BeTrue();
        CrptMvpScope.GetRoleScope(CrptOrganizationRole.Manufacturer).Should().Be(CrptRoleScope.Mvp);
    }

    [Theory]
    [InlineData(CrptOrganizationRole.Importer)]
    public void Importer_IsPhase2Role(CrptOrganizationRole role)
    {
        CrptMvpScope.IsMvpSupported(role).Should().BeFalse();
        CrptMvpScope.IsPhase2Role(role).Should().BeTrue();
        CrptMvpScope.GetRoleScope(role).Should().Be(CrptRoleScope.Phase2);
    }

    [Theory]
    [InlineData(CrptOrganizationRole.Wholesaler)]
    [InlineData(CrptOrganizationRole.Retailer)]
    [InlineData(CrptOrganizationRole.Seller)]
    public void WholesaleRetailSeller_AreOutOfScope(CrptOrganizationRole role)
    {
        CrptMvpScope.IsMvpSupported(role).Should().BeFalse();
        CrptMvpScope.IsOutOfScopeRole(role).Should().BeTrue();
        CrptMvpScope.GetRoleScope(role).Should().Be(CrptRoleScope.OutOfScope);
    }

    [Theory]
    [InlineData(CrptOrganizationRole.Exporter)]
    [InlineData(CrptOrganizationRole.Government)]
    [InlineData(CrptOrganizationRole.HoReCa)]
    public void OtherRoles_AreNotMvpSupported(CrptOrganizationRole role)
    {
        CrptMvpScope.IsMvpSupported(role).Should().BeFalse();
        CrptMvpScope.GetRoleScope(role).Should().Be(CrptRoleScope.OutOfScope);
    }

    [Fact]
    public void MvpRoles_ContainsOnlyManufacturer()
    {
        CrptMvpScope.MvpRoles.Should().ContainSingle(r => r == CrptOrganizationRole.Manufacturer);
    }

    [Theory]
    [InlineData(CrptIntegrationFeature.ConnectionSettings)]
    [InlineData(CrptIntegrationFeature.TrueApiTokenRefresh)]
    [InlineData(CrptIntegrationFeature.NkCatalogFullSync)]
    [InlineData(CrptIntegrationFeature.SuzOrderCreate)]
    [InlineData(CrptIntegrationFeature.SuzCodeDownload)]
    [InlineData(CrptIntegrationFeature.LabelPrint)]
    [InlineData(CrptIntegrationFeature.UtilisationReport)]
    public void ManufacturerWorkflowFeatures_AreInMvp(CrptIntegrationFeature feature)
    {
        CrptMvpScope.IsInMvp(feature).Should().BeTrue();
        CrptMvpScope.IsPhase2(feature).Should().BeFalse();
        CrptMvpScope.IsOutOfScope(feature).Should().BeFalse();
    }

    [Theory]
    [InlineData(CrptIntegrationFeature.IntroduceToCirculation)]
    [InlineData(CrptIntegrationFeature.NkIncrementalSync)]
    [InlineData(CrptIntegrationFeature.NkCardCreateEdit)]
    [InlineData(CrptIntegrationFeature.Gs1Aggregation)]
    [InlineData(CrptIntegrationFeature.DiadocUpd)]
    public void Phase2Features_AreNotInMvp(CrptIntegrationFeature feature)
    {
        CrptMvpScope.IsInMvp(feature).Should().BeFalse();
        CrptMvpScope.IsPhase2(feature).Should().BeTrue();
    }

    [Fact]
    public void Marketplaces_AreOutOfScope()
    {
        CrptMvpScope.IsOutOfScope(CrptIntegrationFeature.Marketplaces).Should().BeTrue();
        CrptMvpScope.IsInMvp(CrptIntegrationFeature.Marketplaces).Should().BeFalse();
    }

    [Fact]
    public void Phase2Features_MatchSpecSection13()
    {
        CrptMvpScope.Phase2Features.Should().BeEquivalentTo(
        [
            CrptIntegrationFeature.IntroduceToCirculation,
            CrptIntegrationFeature.NkIncrementalSync,
            CrptIntegrationFeature.NkCardCreateEdit,
            CrptIntegrationFeature.Gs1Aggregation,
            CrptIntegrationFeature.DiadocUpd,
        ]);
    }
}
