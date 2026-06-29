using DoubleMark.Core.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptEnumTests
{
    [Fact]
    public void CrptOrganizationRole_HasAllSpecValues()
    {
        Enum.GetNames<CrptOrganizationRole>().Should().BeEquivalentTo(
        [
            nameof(CrptOrganizationRole.Manufacturer),
            nameof(CrptOrganizationRole.Importer),
            nameof(CrptOrganizationRole.Wholesaler),
            nameof(CrptOrganizationRole.Retailer),
            nameof(CrptOrganizationRole.Seller),
            nameof(CrptOrganizationRole.Exporter),
            nameof(CrptOrganizationRole.Government),
            nameof(CrptOrganizationRole.HoReCa),
        ]);
    }

    [Fact]
    public void CrptEnvironment_HasSandboxAndProduction()
    {
        Enum.GetNames<CrptEnvironment>().Should().BeEquivalentTo(
        [
            nameof(CrptEnvironment.Sandbox),
            nameof(CrptEnvironment.Production),
        ]);
    }

    [Fact]
    public void EveryOrganizationRole_HasDefinedScope()
    {
        foreach (var role in Enum.GetValues<CrptOrganizationRole>())
        {
            var scope = CrptMvpScope.GetRoleScope(role);
            scope.Should().BeOneOf(CrptRoleScope.Mvp, CrptRoleScope.Phase2, CrptRoleScope.OutOfScope);
        }
    }

    [Fact]
    public void EveryIntegrationFeature_HasExactlyOneScopeBucket()
    {
        foreach (var feature in Enum.GetValues<CrptIntegrationFeature>())
        {
            var inMvp = CrptMvpScope.IsInMvp(feature);
            var inPhase2 = CrptMvpScope.IsPhase2(feature);
            var outOfScope = CrptMvpScope.IsOutOfScope(feature);

            var bucketCount = (inMvp ? 1 : 0) + (inPhase2 ? 1 : 0) + (outOfScope ? 1 : 0);
            bucketCount.Should().Be(1, because: $"feature {feature} must belong to exactly one scope bucket");
        }
    }
}
