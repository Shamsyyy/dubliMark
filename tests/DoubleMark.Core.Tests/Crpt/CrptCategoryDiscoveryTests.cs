using DoubleMark.Core.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptCategoryDiscoveryTests
{
    [Fact]
    public void MergeKnownCategories_AddsDiscoveredNamesSortedCaseInsensitive()
    {
        var merged = CrptNkCategoryDiscovery.MergeKnownCategories(
            [],
            ["Synthetic Beta", "Synthetic Alpha", "synthetic beta"]);

        merged.Should().Equal("Synthetic Alpha", "Synthetic Beta");
    }

    [Fact]
    public void MergeKnownCategories_PreservesExistingKnownCasing()
    {
        var merged = CrptNkCategoryDiscovery.MergeKnownCategories(
            ["Synthetic Alpha"],
            ["synthetic alpha", "Synthetic Beta"]);

        merged.Should().Equal("Synthetic Alpha", "Synthetic Beta");
    }

    [Fact]
    public void MergeKnownCategories_IsIdempotent()
    {
        var first = CrptNkCategoryDiscovery.MergeKnownCategories(
            ["Synthetic Alpha"],
            ["Synthetic Beta", "synthetic beta"]);

        var second = CrptNkCategoryDiscovery.MergeKnownCategories(
            first,
            ["Synthetic Beta", "Synthetic Alpha", "synthetic alpha"]);

        second.Should().Equal(first);
    }

    [Fact]
    public void MergeKnownCategories_IgnoresBlankNames()
    {
        var merged = CrptNkCategoryDiscovery.MergeKnownCategories(
            ["", "   "],
            [null, "", "Synthetic Category"]);

        merged.Should().Equal("Synthetic Category");
    }

    [Fact]
    public void FilterByVisibleCategories_WhenEmpty_ReturnsAllItems()
    {
        var items = CreateItems(
            ("00000000000001", "Synthetic Alpha"),
            ("00000000000002", "Synthetic Beta"),
            ("00000000000003", null));

        var filtered = CrptNkCategoryDiscovery.FilterByVisibleCategories(items, []).ToList();

        filtered.Should().HaveCount(3);
    }

    [Fact]
    public void FilterByVisibleCategories_WhenSubset_ReturnsMatchingCategoriesOnly()
    {
        var items = CreateItems(
            ("00000000000001", "Synthetic Alpha"),
            ("00000000000002", "Synthetic Beta"),
            ("00000000000003", null));

        var filtered = CrptNkCategoryDiscovery.FilterByVisibleCategories(
            items,
            ["synthetic alpha"]).ToList();

        filtered.Should().ContainSingle(item => item.Gtin == "00000000000001");
    }

    [Fact]
    public void CollectCategoryNames_ReadsCategoryNameFromItems()
    {
        var items = CreateItems(
            ("00000000000001", "Synthetic Alpha"),
            ("00000000000002", null));

        CrptNkCategoryDiscovery.CollectCategoryNames(items)
            .Should().Equal("Synthetic Alpha", null);
    }

    [Theory]
    [InlineData("3307490000 Прочие средства для ароматизации", "Прочие средства для ароматизации")]
    [InlineData("3307490000", "3307490000")]
    [InlineData("Товары для ароматизации", "Товары для ароматизации")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void NormalizeCategoryName_StripsOptionalTnvedPrefix(string? input, string? expected) =>
        CrptNkCategoryDiscovery.NormalizeCategoryName(input).Should().Be(expected);

    [Fact]
    public void MergeKnownCategories_NormalizesTnvedPrefixedNames()
    {
        var merged = CrptNkCategoryDiscovery.MergeKnownCategories(
            ["3307490000 Прочие средства для ароматизации"],
            ["Прочие средства для ароматизации"]);

        merged.Should().Equal("Прочие средства для ароматизации");
    }

    [Fact]
    public void FilterByVisibleCategories_MatchesNormalizedCategoryNames()
    {
        var items = CreateItems(
            ("00000000000001", "3307490000 Прочие средства для ароматизации"),
            ("00000000000002", "Synthetic Beta"));

        var filtered = CrptNkCategoryDiscovery.FilterByVisibleCategories(
            items,
            ["Прочие средства для ароматизации"]).ToList();

        filtered.Should().ContainSingle(item => item.Gtin == "00000000000001");
    }

    private static List<CrptProductCatalogItem> CreateItems(params (string Gtin, string? Category)[] rows) =>
        rows.Select(row => new CrptProductCatalogItem
        {
            Gtin = row.Gtin,
            Name = "Synthetic",
            CategoryName = row.Category,
        }).ToList();
}
