using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptUtilisationBuilderTests
{
    private const char Gs = (char)0x1D;
    private const string TestGtin = "00000000000000";

    [Fact]
    public void BuildBody_IncludesProductionAndExpirationDates()
    {
        var request = new UtilisationReportRequest
        {
            ProductGroup = "chemistry",
            RawPayloads = [SyntheticMarkingCode(1)],
            ProductionDate = "2026-01-15",
            ExpirationDate = "2029-01-15",
        };

        var body = CrptUtilisationBuilder.BuildBody(request);

        body["productGroup"].Should().Be("chemistry");
        var sntins = body["sntins"].Should().BeAssignableTo<IReadOnlyList<string>>().Subject;
        sntins.Should().ContainSingle();

        var attributes = body["attributes"].Should().BeOfType<Dictionary<string, object>>().Subject;
        attributes["productionDate"].Should().Be("2026-01-15");
        attributes["expirationDate"].Should().Be("2029-01-15");
    }

    [Fact]
    public void BuildBody_IncludesCertificateRefsWhenPresent()
    {
        var request = new UtilisationReportRequest
        {
            ProductGroup = "chemistry",
            RawPayloads = [SyntheticMarkingCode(1)],
            ProductionDate = "2026-01-15",
            ExpirationDate = "2029-01-15",
            CertificateDocType = "CONFORMITY_CERTIFICATE",
            CertificateDocNumber = "RU-SYN-0001",
            CertificateDocDate = "2025-12-01T00:00:00.000Z",
        };

        var body = CrptUtilisationBuilder.BuildBody(request);
        var attributes = body["attributes"].Should().BeOfType<Dictionary<string, object>>().Subject;

        attributes["certificateDocument"].Should().Be("CONFORMITY_CERTIFICATE");
        attributes["certificateDocumentNumber"].Should().Be("RU-SYN-0001");
        attributes["certificateDocumentDate"].Should().Be("2025-12-01T00:00:00.000Z");
    }

    [Fact]
    public void BuildBody_OmitsCertificateRefsWhenNumberMissing()
    {
        var request = new UtilisationReportRequest
        {
            ProductGroup = "chemistry",
            RawPayloads = [SyntheticMarkingCode(1)],
            ProductionDate = "2026-01-15",
            ExpirationDate = "2029-01-15",
            CertificateDocType = "CONFORMITY_CERTIFICATE",
        };

        var body = CrptUtilisationBuilder.BuildBody(request);
        var attributes = body["attributes"].Should().BeOfType<Dictionary<string, object>>().Subject;

        attributes.Should().NotContainKey("certificateDocument");
        attributes.Should().NotContainKey("certificateDocumentNumber");
    }

    [Fact]
    public void BuildRequest_MapsCatalogItemAndSettings()
    {
        var catalogItem = new CrptProductCatalogItem
        {
            Gtin = TestGtin,
            ProductGroup = "chemistry",
            CertificateDocType = "CONFORMITY_DECLARATION",
            CertificateDocNumber = "RU-SYN-0002",
            CertificateDocDate = "2025-11-01T00:00:00.000Z",
            SyncedAt = DateTimeOffset.UtcNow,
        };

        var settings = new CrptConnectionSettings
        {
            UtilisationProductionDate = "2026-02-01",
            UtilisationExpirationDate = "2029-02-01",
        };

        var request = CrptUtilisationBuilder.BuildRequest(
            catalogItem,
            "chemistry",
            [SyntheticMarkingCode(1)],
            settings);

        request.ProductGroup.Should().Be("chemistry");
        request.ProductionDate.Should().Be("2026-02-01");
        request.ExpirationDate.Should().Be("2029-02-01");
        request.CertificateDocNumber.Should().Be("RU-SYN-0002");
    }

    private static string SyntheticMarkingCode(int index) =>
        $"010000000000000021SYN{index:D3}{Gs}91EE12{Gs}92SYNTHETICPAYLOAD{index:D3}=";
}
