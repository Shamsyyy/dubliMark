using DoubleMark.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

/// <summary>
/// §16 — CRPT documentation link constants.
/// </summary>
public class CrptDocumentationLinksTests
{
    public static IEnumerable<object[]> Section16Urls =>
        CrptDocumentationLinks.AllDocumentedUrls.Select(url => new object[] { url });

    [Theory]
    [MemberData(nameof(Section16Urls))]
    public void Section16_EachUrl_IsHttpsAndValidUri(string url)
    {
        Uri.TryCreate(url, UriKind.Absolute, out var uri).Should().BeTrue(because: url);
        uri!.Scheme.Should().Be(Uri.UriSchemeHttps);
    }

    [Fact]
    public void Section16_AllSpecUrls_ArePresent()
    {
        CrptDocumentationLinks.AllDocumentedUrls.Should().HaveCount(4);
        CrptDocumentationLinks.AllDocumentedUrls.Should().Contain(CrptDocumentationLinks.TrueApiDocs);
        CrptDocumentationLinks.AllDocumentedUrls.Should().Contain(CrptDocumentationLinks.NationalCatalogApiDocs);
        CrptDocumentationLinks.AllDocumentedUrls.Should().Contain(CrptDocumentationLinks.MarkirovkaKnowledgeBase);
        CrptDocumentationLinks.AllDocumentedUrls.Should().Contain(CrptDocumentationLinks.BecomeTechnologyPartner);
    }

    [Fact]
    public void Section16_TrueApiDocs_MatchesSpec()
    {
        CrptDocumentationLinks.TrueApiDocs.Should().Be("https://docs.crpt.ru/gismt/True_API/");
    }

    [Fact]
    public void Section16_NationalCatalogApiDocs_MatchesSpec()
    {
        CrptDocumentationLinks.NationalCatalogApiDocs.Should().Be("https://docs.crpt.ru/gismt/API_НК/");
    }

    [Fact]
    public void Section16_MarkirovkaKnowledgeBase_MatchesSpec()
    {
        CrptDocumentationLinks.MarkirovkaKnowledgeBase.Should().Be("https://markirovka.ru");
    }

    [Fact]
    public void Section16_BecomeTechnologyPartner_MatchesSpec()
    {
        CrptDocumentationLinks.BecomeTechnologyPartner.Should().Be(
            "https://markirovka.ru/knowledge/developers/become-technology-partner/kak-stat-tekhnologicheskim-partnerom-tsrpt-instruktsiya");
    }

    [Fact]
    public void Section16_SuzApi30_HasLabelWithoutPublicUrl()
    {
        CrptDocumentationLinks.SuzApi30Label.Should().Be("API СУЗ 3.0");
        CrptDocumentationLinks.SuzApi30DownloadNote.Should().Contain("ЛК Честного ЗНАКа");
        CrptDocumentationLinks.AllDocumentedUrls.Should().NotContain(CrptDocumentationLinks.SuzApi30Label);
    }
}
