using DoubleMark.Crpt;
using DoubleMark.Desktop.Settings;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptUrlTests
{
    [Fact]
    public void NormalizeBaseUrl_OfficialCyrillicNkHost_ConvertsToPunycode()
    {
        CrptUrl.NormalizeBaseUrl(CrptUrl.ProductionNkCyrillicBaseUrl).Should().Be(CrptUrl.ProductionNkBaseUrl);
    }

    [Fact]
    public void NormalizeBaseUrl_LegacyLatinApiCyrillicHost_RemapsToOfficialPunycode()
    {
        const string legacyLatin = "https://api.национальный-каталог.рф/";
        CrptUrl.NormalizeBaseUrl(legacyLatin).Should().Be(CrptUrl.ProductionNkBaseUrl);
    }

    [Fact]
    public void NormalizeBaseUrl_LegacyLatinApiPunycodeHost_RemapsToOfficialPunycode()
    {
        var legacy = $"https://{CrptUrl.LegacyLatinApiNkPunycodeHost}/";
        CrptUrl.NormalizeBaseUrl(legacy).Should().Be(CrptUrl.ProductionNkBaseUrl);
    }

    [Fact]
    public void NormalizeBaseUrl_PunycodeNkHost_StaysPunycode()
    {
        CrptUrl.NormalizeBaseUrl(CrptUrl.ProductionNkBaseUrl).Should().Be(CrptUrl.ProductionNkBaseUrl);
    }

    [Fact]
    public void NormalizeBaseUrl_LegacyWrongNkPunycodeHost_RemapsToWorkingProductionHost()
    {
        var legacy = $"https://{CrptUrl.LegacyWrongNkPunycodeHost}/";
        CrptUrl.NormalizeBaseUrl(legacy).Should().Be(CrptUrl.ProductionNkBaseUrl);
    }

    [Fact]
    public void ProductionNkPunycodeHost_IsOfficialDocsHost()
    {
        CrptUrl.ProductionNkPunycodeHost.Should().Be("xn--80aqu.xn----7sbabas4ajkhfocclk9d3cvfsa.xn--p1ai");
        CrptUrl.ProductionNkBaseUrl.Should().Be($"https://{CrptUrl.ProductionNkPunycodeHost}/");
        CrptUrl.LegacyLatinApiNkPunycodeHost.Should().Be("api.xn----7sbabas4ajkhfocclk9d3cvfsa.xn--p1ai");
    }

    [Fact]
    public void NormalizeBaseUrl_AddsTrailingSlash()
    {
        CrptUrl.NormalizeBaseUrl("https://api.nk.sandbox.crptech.ru")
            .Should().Be("https://api.nk.sandbox.crptech.ru/");
    }

    [Fact]
    public void DefaultNkBaseUrl_IsPunycodeNotCyrillic()
    {
        CrptSettings.DefaultNkBaseUrl.Should().Be(CrptUrl.ProductionNkBaseUrl);
        CrptSettings.DefaultNkBaseUrl.Should().NotContain("национальный");
    }
}
