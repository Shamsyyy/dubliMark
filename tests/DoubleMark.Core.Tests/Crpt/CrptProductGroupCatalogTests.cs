using DoubleMark.Core.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public sealed class CrptProductGroupCatalogTests
{
    [Theory]
    [InlineData("chemistry", "Косметика, бытовая химия и товары личной гигиены")]
    [InlineData("CHEMISTRY", "Косметика, бытовая химия и товары личной гигиены")]
    [InlineData("milk", "Молочная продукция")]
    [InlineData("lp", "Лёгкая промышленность")]
    [InlineData("petfood", "Корма для животных")]
    [InlineData("seafood", "Морепродукты")]
    [InlineData("softdrinks", "Соковая продукция и безалкогольные напитки")]
    public void GetDisplayName_ReturnsRussianNameForKnownCode(string code, string expected)
    {
        CrptProductGroupCatalog.GetDisplayName(code).Should().Be(expected);
    }

    [Fact]
    public void GetDisplayName_EmptyOrNull_ReturnsDash()
    {
        CrptProductGroupCatalog.GetDisplayName(null).Should().Be("—");
        CrptProductGroupCatalog.GetDisplayName("").Should().Be("—");
        CrptProductGroupCatalog.GetDisplayName("   ").Should().Be("—");
    }

    [Fact]
    public void GetDisplayName_UnknownCode_ReturnsNormalizedCode()
    {
        CrptProductGroupCatalog.GetDisplayName("futuregroup").Should().Be("futuregroup");
        CrptProductGroupCatalog.GetDisplayName(" FutureGroup ").Should().Be("futuregroup");
    }

    [Fact]
    public void All_ContainsEveryDocumentedProductGroup()
    {
        var expectedCodes = new[]
        {
            "lp", "shoes", "tobacco", "perfumery", "tires", "electronics", "pharma", "milk",
            "bicycle", "wheelchairs", "alcohol", "otp", "water", "furs", "beer", "ncp", "bio",
            "antiseptic", "petfood", "seafood", "nabeer", "softdrinks", "meat", "vetpharma",
            "toys", "radio", "titan", "conserve", "vegetableoil", "opticfiber", "chemistry",
            "books", "grocery", "pharmaraw", "construction", "fire", "heater", "cableraw",
            "autofluids", "polymer", "sweets", "carparts", "furslp", "nicotindev", "gadgets",
            "frozen", "fertilizers", "homeware", "pyrotechnics",
        };

        CrptProductGroupCatalog.All.Keys.Should().BeEquivalentTo(expectedCodes);
        foreach (var code in expectedCodes)
        {
            CrptProductGroupCatalog.All[code].DisplayNameRu.Should().NotBeNullOrWhiteSpace();
            CrptProductGroupCatalog.IsKnown(code).Should().BeTrue();
            CrptProductGroup.IsKnown(code).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("chemistry", 35)]
    [InlineData("milk", 8)]
    [InlineData("lp", 1)]
    public void TryGetEntry_ReturnsDbIdWhenKnown(string code, int dbId)
    {
        var entry = CrptProductGroupCatalog.TryGetEntry(code);
        entry.Should().NotBeNull();
        entry!.DbId.Should().Be(dbId);
    }

    [Fact]
    public void Chemistry_IsDistinctFromNkCategoryConcept()
    {
        const string nkCategoryExample = "Товары для ароматизации";
        var productGroupDisplay = CrptProductGroupCatalog.GetDisplayName("chemistry");

        productGroupDisplay.Should().NotBe(nkCategoryExample);
        productGroupDisplay.Should().Contain("Косметика");
    }
}
