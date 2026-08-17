using System.Text.Json;
using DoubleMark.Core.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptProductCatalogItemTests
{
    [Fact]
    public void Serialization_RoundtripPreservesAllFields()
    {
        var item = new CrptProductCatalogItem
        {
            Gtin = "00000000000000",
            GoodId = 12345,
            Name = "Synthetic Test Product",
            TnvedCode = "0000000000",
            TnvedGroup = "0000",
            ProductGroup = "chemistry",
            TemplateId = 46,
            NkStatus = "published",
            NkStatusRaw = "published",
            NkProductState = NkProductState.Published,
            NkCardType = NkCardType.TradeUnit,
            NkCardStatusPrimary = "published",
            NkDetailedStatuses = ["published"],
            CategoryName = "Synthetic Category",
            NkCategoryId = 123,
            NkUpdatedAt = new DateTimeOffset(2020, 8, 18, 10, 57, 18, TimeSpan.Zero),
            IsSigned = true,
            CanOrderCodes = true,
            CertificateDocType = "CONFORMITY_CERTIFICATE",
            CertificateDocNumber = "DOC-0001",
            CertificateDocDate = "2024-01-01",
            SyncedAt = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero),
            SyncError = null,
        };

        var json = JsonSerializer.Serialize(item, CrptProductCatalogStore.JsonOptions);
        var restored = JsonSerializer.Deserialize<CrptProductCatalogItem>(json, CrptProductCatalogStore.JsonOptions);

        restored.Should().BeEquivalentTo(item);
    }

    [Fact]
    public void Deserialization_BackwardCompatibleWithoutNewFields()
    {
        const string legacyJson = """
            {
              "items": [
                {
                  "gtin": "00000000000000",
                  "name": "Legacy Product",
                  "nkStatus": "published",
                  "isSigned": true,
                  "canOrderCodes": false,
                  "syncedAt": "2024-06-01T12:00:00+00:00"
                }
              ]
            }
            """;

        using var doc = JsonDocument.Parse(legacyJson);
        var item = doc.RootElement.GetProperty("items")[0];
        var restored = JsonSerializer.Deserialize<CrptProductCatalogItem>(
            item.GetRawText(),
            CrptProductCatalogStore.JsonOptions);

        restored.Should().NotBeNull();
        restored!.Gtin.Should().Be("00000000000000");
        restored.NkProductState.Should().Be(NkProductState.Unknown);
        restored.NkCardType.Should().Be(NkCardType.Unknown);
        restored.NkDetailedStatuses.Should().BeEmpty();
        restored.CategoryName.Should().BeNull();
        restored.NkUpdatedAt.Should().BeNull();
    }

    [Fact]
    public void CatalogSyncProgress_RecordEquality()
    {
        var progress = new CrptCatalogSyncProgress("feed-product", 5, 25, "00000000000000");
        progress.Stage.Should().Be("feed-product");
        progress.Processed.Should().Be(5);
        progress.Total.Should().Be(25);
        progress.CurrentGtin.Should().Be("00000000000000");
    }
}
