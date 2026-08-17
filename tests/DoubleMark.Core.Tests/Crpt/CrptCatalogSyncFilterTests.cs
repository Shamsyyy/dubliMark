using System.Text.Json;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Settings;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

/// <summary>
/// Catalog sync stores all NK cards regardless of published/signed status (Phase C1).
/// UI filters apply at display time only.
/// </summary>
public class CrptCatalogSyncFilterTests
{
    private static readonly DateTimeOffset SyncedAt = new(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StoreAllPipeline_KeepsDraftAndPublished()
    {
        var merged = BuildMergedCatalog(
            totalGoods: 2,
            explicitPublishedStatus: 1,
            signed: true);

        merged.Should().HaveCount(2);
        merged.Values.Should().Contain(item => item.NkStatus == "published");
        merged.Values.Should().Contain(item => item.NkStatus == "draft");
    }

    [Fact]
    public void StoreAllPipeline_KeepsSignedAndUnsigned()
    {
        var signed = BuildSingleItem("00000000000001", "published", signed: true);
        var unsigned = BuildSingleItem("00000000000002", "published", signed: false);

        var merged = new Dictionary<string, CrptProductCatalogItem>(StringComparer.Ordinal)
        {
            [signed.Gtin] = signed,
            [unsigned.Gtin] = unsigned,
        };

        merged.Should().HaveCount(2);
        merged.Values.Should().Contain(item => item.IsSigned);
        merged.Values.Should().Contain(item => !item.IsSigned);
    }

    [Fact]
    public void StoreAllPipeline_FiftyOneMixedStatuses_AllRemainInCatalog()
    {
        const int totalGoods = 51;
        const int explicitPublishedStatus = 3;
        var settings = new CrptSettings
        {
            ProductGroups = [CrptProductGroup.Chemistry],
        };

        var merged = BuildMergedCatalog(totalGoods, explicitPublishedStatus, signed: false);
        ApplyDefaults(settings, merged);

        merged.Should().HaveCount(totalGoods);
        merged.Values.Count(item => item.NkStatus == "published").Should().BeGreaterThan(0);
        merged.Values.Count(item => item.NkStatus == "draft").Should().BeGreaterThan(0);
        merged.Values.Should().OnlyContain(item => !item.CanOrderCodes, "unsigned cards must not be orderable");
    }

    [Fact]
    public void DefaultSettings_DoNotFilterOnSync()
    {
        var settings = new CrptSettings();

        settings.NkSyncOnlyPublished.Should().BeFalse();
        settings.NkSyncOnlySigned.Should().BeFalse();
    }

    private static CrptProductCatalogItem BuildSingleItem(string gtin, string listStatus, bool signed)
    {
        var json = $$"""
                     {
                       "good_id": 1000,
                       "gtin": "{{gtin}}",
                       "good_name": "Synthetic",
                       "good_status": "{{listStatus}}",
                       "good_detailed_status": ["{{listStatus}}"]
                     }
                     """;
        using var doc = JsonDocument.Parse(json);
        var baseline = CrptNkProductMapper.MapProductListEntry(doc.RootElement, SyncedAt);

        var feedJson = signed
            ? $$"""
              {
                "identified_by": [{ "value": "{{gtin}}", "type": "gtin" }],
                "good_status": "published",
                "good_signed": true,
                "good_detailed_status": ["published"]
              }
              """
            : $$"""
              {
                "identified_by": [{ "value": "{{gtin}}", "type": "gtin" }],
                "good_status": "published",
                "good_signed": false,
                "good_detailed_status": ["published", "notsigned"]
              }
              """;
        using var feedDoc = JsonDocument.Parse(feedJson);
        return CrptNkProductMapper.MergeFeedProduct(
            baseline,
            feedDoc.RootElement,
            productGroup: null,
            templateId: null,
            SyncedAt);
    }

    private static Dictionary<string, CrptProductCatalogItem> BuildMergedCatalog(
        int totalGoods,
        int explicitPublishedStatus,
        bool signed)
    {
        var merged = new Dictionary<string, CrptProductCatalogItem>(StringComparer.Ordinal);
        for (var index = 0; index < totalGoods; index++)
        {
            var gtin = index.ToString("D14");
            var useExplicitPublished = index < explicitPublishedStatus;
            var json = useExplicitPublished
                ? $$"""
                  {
                    "good_id": {{1000 + index}},
                    "gtin": "{{gtin}}",
                    "good_name": "Synthetic {{index}}",
                    "good_status": "published",
                    "good_detailed_status": ["published"]
                  }
                  """
                : $$"""
                  {
                    "good_id": {{1000 + index}},
                    "gtin": "{{gtin}}",
                    "good_name": "Synthetic {{index}}",
                    "good_status": "draft",
                    "good_detailed_status": ["draft"]
                  }
                  """;

            using var doc = JsonDocument.Parse(json);
            var baseline = CrptNkProductMapper.MapProductListEntry(doc.RootElement, SyncedAt);

            var feedJson = useExplicitPublished
                ? (signed
                    ? $$"""
                      {
                        "identified_by": [{ "value": "{{gtin}}", "type": "gtin" }],
                        "good_status": "published",
                        "good_signed": true,
                        "good_detailed_status": ["published"]
                      }
                      """
                    : $$"""
                      {
                        "identified_by": [{ "value": "{{gtin}}", "type": "gtin" }],
                        "good_status": "published",
                        "good_signed": false,
                        "good_detailed_status": ["published", "notsigned"]
                      }
                      """)
                : $$"""
                  {
                    "identified_by": [{ "value": "{{gtin}}", "type": "gtin" }],
                    "good_status": "draft",
                    "good_signed": false,
                    "good_detailed_status": ["draft"]
                  }
                  """;
            using var feedDoc = JsonDocument.Parse(feedJson);
            merged[gtin] = CrptNkProductMapper.MergeFeedProduct(
                baseline,
                feedDoc.RootElement,
                productGroup: null,
                templateId: null,
                SyncedAt);
        }

        return merged;
    }

    private static void ApplyDefaults(CrptSettings settings, Dictionary<string, CrptProductCatalogItem> merged)
    {
        var defaultGroup = settings.PrimaryProductGroup;
        var defaultTemplateId = settings.ResolveTemplateId(defaultGroup);
        foreach (var gtin in merged.Keys.ToList())
        {
            merged[gtin] = CrptNkProductMapper.ApplyCatalogDefaults(
                merged[gtin],
                defaultGroup,
                defaultTemplateId);
        }
    }
}
