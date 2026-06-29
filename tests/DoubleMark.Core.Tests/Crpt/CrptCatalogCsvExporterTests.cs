using DoubleMark.Core.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.ViewModels.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptCatalogCsvExporterTests
{
    [Fact]
    public void FormatCsv_EscapesSemicolonsAndIncludesHeaders()
    {
        var rows = new[]
        {
            new CrptCatalogRowViewModel(new CrptProductCatalogItem
            {
                Gtin = "00000000000001",
                Name = "Test; product",
                TnvedCode = "3304990000",
                NkProductState = NkProductState.Published,
                NkCardStatusPrimary = "published",
                NkCardType = NkCardType.TradeUnit,
                CategoryName = "Категория",
                ProductGroup = "chemistry",
                CanOrderCodes = true,
                NkUpdatedAt = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero),
                SyncedAt = DateTimeOffset.UtcNow,
            }),
        };

        var csv = CrptCatalogCsvExporter.FormatCsv(rows);

        csv.Should().StartWith("GTIN;Название;ТН ВЭД;");
        csv.Should().Contain("\"Test; product\"");
        csv.Should().Contain("00000000000001");
        csv.Should().Contain("Да");
        csv.Should().NotContain("91");
        csv.Should().NotContain("92");
    }

    [Fact]
    public void FormatCsv_UsesFilteredRowViewModels()
    {
        var rows = new[]
        {
            new CrptCatalogRowViewModel(new CrptProductCatalogItem
            {
                Gtin = "00000000000001",
                Name = "Visible",
                SyncedAt = DateTimeOffset.UtcNow,
            }),
        };

        var csv = CrptCatalogCsvExporter.FormatCsv(rows);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(2);
    }
}
