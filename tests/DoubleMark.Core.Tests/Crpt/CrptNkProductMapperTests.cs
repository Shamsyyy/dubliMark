using System.Text.Json;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptNkProductMapperTests
{
    private static readonly DateTimeOffset SyncedAt = new(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParseProductListResponse_ReadsApiV4ResultEnvelope()
    {
        const string json = """
            {
              "apiversion": 4,
              "result": {
                "limit": 2,
                "offset": 0,
                "total": 3,
                "goods": [
                  {
                    "good_id": 720679,
                    "gtin": "0000000000001",
                    "good_name": "Synthetic Product A",
                    "tnved": "3303",
                    "brand_name": "Synthetic Brand",
                    "good_status": "published",
                    "good_detailed_status": ["published"],
                    "to_date": "2020-08-18 10:57:18"
                  },
                  {
                    "good_id": 720680,
                    "gtin": "0000000000002",
                    "good_name": "Synthetic Product B",
                    "tnved": "3304",
                    "brand_name": "Synthetic Brand",
                    "good_status": "published",
                    "good_detailed_status": ["published"],
                    "to_date": "2020-08-19 11:00:00"
                  }
                ]
              }
            }
            """;

        var (goods, total) = CrptNkProductMapper.ParseProductListResponse(json);

        total.Should().Be(3);
        goods.Should().HaveCount(2);
        goods[0].GetProperty("gtin").GetString().Should().Be("0000000000001");
        goods[1].GetProperty("good_status").GetString().Should().Be("published");
    }

    [Fact]
    public void MapProductListEntry_InfersSignedFromDetailedStatusWhenGoodSignedAbsent()
    {
        const string json = """
            {
              "good_id": 1002,
              "gtin": "00000000000001",
              "good_name": "Synthetic Published Product",
              "tnved": "3303",
              "good_status": "published",
              "good_detailed_status": ["published"]
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var item = CrptNkProductMapper.MapProductListEntry(doc.RootElement, SyncedAt);

        item.IsSigned.Should().BeTrue("product-list omits good_signed; published in good_detailed_status means signed in LK");
        item.NkStatus.Should().Be("published");
    }

    [Fact]
    public void MapProductListEntry_NotSignedWhenDetailedStatusContainsNotsigned()
    {
        const string json = """
            {
              "good_id": 1003,
              "gtin": "00000000000002",
              "good_name": "Synthetic Awaiting Signature",
              "good_status": "draft",
              "good_detailed_status": ["notsigned"]
            }
            """;
        using var doc = JsonDocument.Parse(json);

        CrptNkProductMapper.MapProductListEntry(doc.RootElement, SyncedAt)
            .IsSigned.Should().BeFalse();
    }

    [Fact]
    public void MapProductListEntry_DraftIsNotSigned()
    {
        const string json = """
            {
              "good_id": 1004,
              "gtin": "00000000000003",
              "good_name": "Synthetic Draft",
              "good_status": "draft",
              "good_detailed_status": ["draft"]
            }
            """;
        using var doc = JsonDocument.Parse(json);

        CrptNkProductMapper.MapProductListEntry(doc.RootElement, SyncedAt)
            .IsSigned.Should().BeFalse();
    }

    [Fact]
    public void ReadGtin_ReadsFromFeedProductIdentifiedBy()
    {
        const string json = """
            {
              "identified_by": [
                { "value": "00000000000004", "type": "gtin", "multiplier": 1, "level": "trade-unit" }
              ],
              "good_status": "published",
              "good_signed": true
            }
            """;
        using var doc = JsonDocument.Parse(json);

        CrptNkProductMapper.ReadGtin(doc.RootElement).Should().Be("00000000000004");
    }

    [Fact]
    public void MergeFeedProduct_PrefersExplicitGoodSignedOverBaseline()
    {
        var baseline = new CrptProductCatalogItem
        {
            Gtin = "00000000000005",
            Name = "Baseline",
            NkStatus = "published",
            IsSigned = true,
            SyncedAt = SyncedAt,
        };

        const string feedJson = """
            {
              "identified_by": [
                { "value": "00000000000005", "type": "gtin" }
              ],
              "good_status": "published",
              "good_signed": false,
              "good_detailed_status": ["published", "notsigned"]
            }
            """;
        using var doc = JsonDocument.Parse(feedJson);

        CrptNkProductMapper.MergeFeedProduct(baseline, doc.RootElement, null, null, SyncedAt)
            .IsSigned.Should().BeFalse("feed-product good_signed overrides product-list inference");
    }

    [Fact]
    public void ParseProductListResponse_PublishedEntriesInferSigned()
    {
        const string json = """
            {
              "apiversion": 4,
              "result": {
                "total": 1,
                "goods": [
                  {
                    "good_id": 720679,
                    "gtin": "00000000000006",
                    "good_name": "Synthetic Product",
                    "good_status": "published",
                    "good_detailed_status": ["published"]
                  }
                ]
              }
            }
            """;

        var (goods, _) = CrptNkProductMapper.ParseProductListResponse(json);
        var item = CrptNkProductMapper.MapProductListEntry(goods[0], SyncedAt);

        item.IsSigned.Should().BeTrue();
    }

    [Fact]
    public void ResolveIsSigned_PublishedStatusOverridesDraftInDetailed()
    {
        const string json = """
            {
              "good_status": "published",
              "good_detailed_status": ["draft"]
            }
            """;
        using var doc = JsonDocument.Parse(json);

        CrptNkProductMapper.ResolveIsSigned(doc.RootElement).Should().BeTrue();
    }

    [Fact]
    public void ResolveIsSigned_PublishedWinsWhenDraftAlsoPresent()
    {
        const string json = """
            {
              "good_status": "published",
              "good_detailed_status": ["published", "draft"]
            }
            """;
        using var doc = JsonDocument.Parse(json);

        CrptNkProductMapper.ResolveIsSigned(doc.RootElement).Should().BeTrue();
    }

    [Fact]
    public void ParseFeedProductEntries_ReadsResultArrayEnvelope()
    {
        const string json = """
            {
              "apiversion": 3,
              "result": [
                {
                  "identified_by": [
                    { "value": "00000000000007", "type": "gtin" }
                  ],
                  "good_status": "published",
                  "good_signed": true,
                  "good_detailed_status": ["published"]
                }
              ]
            }
            """;

        var entries = CrptNkProductMapper.ParseFeedProductEntries(json);

        entries.Should().HaveCount(1);
        CrptNkProductMapper.ReadGtin(entries[0]).Should().Be("00000000000007");
        CrptNkProductMapper.ResolveIsSigned(entries[0]).Should().BeTrue();
    }

    [Fact]
    public void MapProductListEntry_MapsSummaryFields()
    {
        const string json = """
            {
              "good_id": 1001,
              "gtin": "00000000000000",
              "good_name": "Synthetic Test Product",
              "tnved": "0000",
              "good_status": "published",
              "good_signed": true
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var item = CrptNkProductMapper.MapProductListEntry(doc.RootElement, SyncedAt);

        item.Gtin.Should().Be("00000000000000");
        item.GoodId.Should().Be(1001);
        item.Name.Should().Be("Synthetic Test Product");
        item.TnvedGroup.Should().Be("0000");
        item.NkStatus.Should().Be("published");
        item.IsSigned.Should().BeTrue();
        item.CanOrderCodes.Should().BeFalse("productGroup is unknown until True API step");
    }

    [Fact]
    public void MergeFeedProduct_MapsTnvedAttributeAndCertificate()
    {
        var baseline = new CrptProductCatalogItem
        {
            Gtin = "00000000000000",
            Name = "Baseline",
            NkStatus = "published",
            IsSigned = true,
            SyncedAt = SyncedAt,
        };

        const string feedJson = """
            {
              "identified_by": [
                { "value": "00000000000000", "type": "gtin", "level": "trade-unit" }
              ],
              "good_name": "Synthetic Feed Product",
              "good_status": "published",
              "good_signed": true,
              "is_set": false,
              "is_kit": false,
              "good_attrs": [
                { "attr_id": 13933, "attr_value": "0000000000" }
              ],
              "certificates": [
                { "type": "CONFORMITY_CERTIFICATE", "number": "DOC-0001", "date": "2024-01-01" }
              ]
            }
            """;
        using var doc = JsonDocument.Parse(feedJson);

        var merged = CrptNkProductMapper.MergeFeedProduct(
            baseline,
            doc.RootElement,
            CrptProductGroup.Chemistry,
            templateId: 46,
            SyncedAt);

        merged.TnvedCode.Should().Be("0000000000");
        merged.ProductGroup.Should().Be(CrptProductGroup.Chemistry);
        merged.TemplateId.Should().Be(46);
        merged.CertificateDocType.Should().Be("CONFORMITY_CERTIFICATE");
        merged.CertificateDocNumber.Should().Be("DOC-0001");
        merged.CertificateDocDate.Should().Be("2024-01-01");
        merged.CanOrderCodes.Should().BeTrue();
    }

    [Fact]
    public void ResolveNkStatus_TreatsDetailedPublishedAsPublishedWhenGoodStatusIsDraft()
    {
        const string json = """
            {
              "good_status": "draft",
              "good_detailed_status": ["published"]
            }
            """;
        using var doc = JsonDocument.Parse(json);

        CrptNkProductMapper.ResolveNkStatus(doc.RootElement).Should().Be("published");
        CrptNkProductMapper.IsPublishedInNkLk(doc.RootElement).Should().BeTrue();
    }

    [Fact]
    public void ResolveNkStatus_TreatsGoodTurnFlagAsPublished()
    {
        const string json = """
            {
              "good_status": "moderation",
              "good_turn_flag": true
            }
            """;
        using var doc = JsonDocument.Parse(json);

        CrptNkProductMapper.ResolveNkStatus(doc.RootElement).Should().Be("published");
    }

    [Fact]
    public void MapProductListEntry_DraftStatusWithDetailedPublished_IsPublishedAndSigned()
    {
        const string json = """
            {
              "good_id": 2001,
              "gtin": "00000000000099",
              "good_name": "Synthetic LK Published",
              "good_status": "draft",
              "good_detailed_status": ["published"]
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var item = CrptNkProductMapper.MapProductListEntry(doc.RootElement, SyncedAt);

        item.NkStatus.Should().Be("published");
        item.IsSigned.Should().BeTrue();
    }

    [Fact]
    public void ApplyCatalogDefaults_UsesPrimaryProductGroupForCanOrderCodes()
    {
        var item = new CrptProductCatalogItem
        {
            Gtin = "00000000000000",
            Name = "Synthetic",
            NkStatus = "published",
            IsSigned = true,
            NkCardType = NkCardType.TradeUnit,
            SyncedAt = SyncedAt,
        };

        var updated = CrptNkProductMapper.ApplyCatalogDefaults(
            item,
            CrptProductGroup.Chemistry,
            defaultTemplateId: 46);

        updated.ProductGroup.Should().Be(CrptProductGroup.Chemistry);
        updated.TemplateId.Should().Be(46);
        updated.CanOrderCodes.Should().BeTrue();
    }

    [Fact]
    public void ReadProductGroupFromInfoResponse_MatchesGtin()
    {
        const string json = """
            [
              { "gtin": "00000000000001", "productGroup": "milk" },
              { "gtin": "00000000000000", "productGroup": "chemistry" }
            ]
            """;

        CrptNkProductMapper.ReadProductGroupFromInfoResponse(json, "00000000000000")
            .Should().Be("chemistry");
    }

    [Theory]
    [InlineData("published", true, "chemistry", NkCardType.TradeUnit, true)]
    [InlineData("draft", true, "chemistry", NkCardType.TradeUnit, false)]
    [InlineData("published", false, "chemistry", NkCardType.TradeUnit, false)]
    [InlineData("published", true, "unknown-group", NkCardType.TradeUnit, false)]
    [InlineData("published", true, "chemistry", NkCardType.Set, false)]
    [InlineData("published", true, "chemistry", NkCardType.Kit, false)]
    public void ComputeCanOrderCodes_RequiresPublishedSignedKnownGroupAndTradeUnit(
        string status,
        bool signed,
        string group,
        NkCardType cardType,
        bool expected)
    {
        CrptNkProductMapper.ComputeCanOrderCodes(status, signed, group, cardType).Should().Be(expected);
    }

    [Theory]
    [InlineData("draft", "[\"draft\"]", NkProductState.Draft)]
    [InlineData("moderation", "[\"moderation\"]", NkProductState.Moderation)]
    [InlineData("errors", "[\"errors\"]", NkProductState.Errors)]
    [InlineData("archived", "[\"archived\"]", NkProductState.Archived)]
    [InlineData("published", "[\"published\"]", NkProductState.Published)]
    public void MapProductState_MapsGoodStatusAndDetailed(string goodStatus, string detailedJson, NkProductState expected)
    {
        var json = $$"""
                     {
                       "good_status": "{{goodStatus}}",
                       "good_detailed_status": {{detailedJson}}
                     }
                     """;
        using var doc = JsonDocument.Parse(json);

        CrptNkProductMapper.MapProductState(doc.RootElement).Should().Be(expected);
    }

    [Theory]
    [InlineData(false, false, "trade-unit", NkCardType.TradeUnit)]
    [InlineData(true, false, "trade-unit", NkCardType.Set)]
    [InlineData(false, true, "trade-unit", NkCardType.Kit)]
    public void MapCardType_PrefersSetAndKitFlags(bool isSet, bool isKit, string level, NkCardType expected)
    {
        var json = $$"""
                     {
                       "is_set": {{isSet.ToString().ToLowerInvariant()}},
                       "is_kit": {{isKit.ToString().ToLowerInvariant()}},
                       "identified_by": [
                         { "value": "00000000000010", "type": "gtin", "level": "{{level}}" }
                       ]
                     }
                     """;
        using var doc = JsonDocument.Parse(json);

        CrptNkProductMapper.MapCardType(doc.RootElement).Should().Be(expected);
    }

    [Fact]
    public void ParseNkUpdatedAt_PrefersUpdatedDateOverToDate()
    {
        const string json = """
            {
              "to_date": "2020-08-18 10:57:18",
              "updated_date": "2021-01-15 12:30:00"
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var parsed = CrptNkProductMapper.ParseNkUpdatedAt(doc.RootElement);
        parsed.Should().NotBeNull();
        parsed!.Value.DateTime.Should().Be(new DateTime(2021, 1, 15, 12, 30, 0));
    }

    [Fact]
    public void MapProductListEntry_ReadsCategoryAndUpdatedAt()
    {
        const string json = """
            {
              "good_id": 1005,
              "gtin": "00000000000011",
              "good_name": "Synthetic Category Product",
              "good_status": "published",
              "good_detailed_status": ["published"],
              "category": "Synthetic Category",
              "to_date": "2020-08-18 10:57:18"
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var item = CrptNkProductMapper.MapProductListEntry(doc.RootElement, SyncedAt);

        item.CategoryName.Should().Be("Synthetic Category");
        item.NkUpdatedAt.Should().NotBeNull();
        item.NkProductState.Should().Be(NkProductState.Published);
        item.NkCardStatusPrimary.Should().Be("published");
    }

    [Fact]
    public void ReadCategoryName_ReadsOfficialCategoriesArrayWithCatNameAndCatId()
    {
        const string officialJson = """
            {
              "categories": [
                { "cat_id": 123, "cat_name": "Товары для ароматизации" }
              ]
            }
            """;
        using var officialDoc = JsonDocument.Parse(officialJson);

        var info = CrptNkProductMapper.ReadCategoryInfo(officialDoc.RootElement);
        info.Name.Should().Be("Товары для ароматизации");
        info.Id.Should().Be(123);
        CrptNkProductMapper.ReadCategoryName(officialDoc.RootElement).Should().Be("Товары для ароматизации");
        CrptNkProductMapper.ReadNkCategoryId(officialDoc.RootElement).Should().Be(123);
    }

    [Fact]
    public void ReadCategoryName_ReadsCategoriesArrayAndAttrFallback()
    {
        const string categoriesJson = """
            {
              "categories": [
                { "name": "Synthetic Array Category" }
              ]
            }
            """;
        using var categoriesDoc = JsonDocument.Parse(categoriesJson);
        CrptNkProductMapper.ReadCategoryName(categoriesDoc.RootElement).Should().Be("Synthetic Array Category");

        const string attrJson = """
            {
              "good_attrs": [
                { "attr_name": "Категория", "attr_value": "Synthetic Attr Category" }
              ]
            }
            """;
        using var attrDoc = JsonDocument.Parse(attrJson);
        CrptNkProductMapper.ReadCategoryName(attrDoc.RootElement).Should().Be("Synthetic Attr Category");
    }

    [Fact]
    public void MapProductListEntry_ReadsOfficialCategoriesArray()
    {
        const string json = """
            {
              "good_id": 1006,
              "gtin": "00000000000015",
              "good_name": "Synthetic Official Category Product",
              "good_status": "published",
              "good_detailed_status": ["published"],
              "categories": [
                { "cat_id": 123, "cat_name": "Товары для ароматизации" }
              ],
              "to_date": "2020-08-18 10:57:18"
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var item = CrptNkProductMapper.MapProductListEntry(doc.RootElement, SyncedAt);

        item.CategoryName.Should().Be("Товары для ароматизации");
        item.NkCategoryId.Should().Be(123);
    }

    [Fact]
    public void MergeFeedProduct_ReadsCategoryFromFeedProductCategoriesArray()
    {
        var baseline = new CrptProductCatalogItem
        {
            Gtin = "00000000000016",
            Name = "Baseline",
            NkStatus = "published",
            IsSigned = true,
            SyncedAt = SyncedAt,
        };

        const string feedJson = """
            {
              "identified_by": [{ "value": "00000000000016", "type": "gtin", "level": "trade-unit" }],
              "good_status": "published",
              "good_signed": true,
              "categories": [
                { "cat_id": 456, "cat_name": "Synthetic Feed Category" }
              ]
            }
            """;
        using var doc = JsonDocument.Parse(feedJson);

        var merged = CrptNkProductMapper.MergeFeedProduct(baseline, doc.RootElement, null, null, SyncedAt);

        merged.CategoryName.Should().Be("Synthetic Feed Category");
        merged.NkCategoryId.Should().Be(456);
    }

    [Fact]
    public void MapCardStatusPrimary_UsesPriorityOrder()
    {
        CrptNkProductMapper.MapCardStatusPrimary(["published", "notsigned"]).Should().Be("notsigned");
        CrptNkProductMapper.MapCardStatusPrimary(["draft", "errors"]).Should().Be("errors");
    }

    [Fact]
    public void MergeFeedProduct_KeepsBaselineCategoryWhenFeedEmpty()
    {
        var baseline = new CrptProductCatalogItem
        {
            Gtin = "00000000000012",
            Name = "Baseline",
            CategoryName = "Synthetic Kept Category",
            NkStatus = "published",
            IsSigned = true,
            SyncedAt = SyncedAt,
        };

        const string feedJson = """
            {
              "identified_by": [{ "value": "00000000000012", "type": "gtin", "level": "trade-unit" }],
              "good_status": "published",
              "good_signed": true
            }
            """;
        using var doc = JsonDocument.Parse(feedJson);

        CrptNkProductMapper.MergeFeedProduct(baseline, doc.RootElement, null, null, SyncedAt)
            .CategoryName.Should().Be("Synthetic Kept Category");
    }

    [Fact]
    public void PreservePreviousCatalogFields_KeepsCategoryOnResync()
    {
        var current = new CrptProductCatalogItem
        {
            Gtin = "00000000000013",
            Name = "Current",
            SyncedAt = SyncedAt,
        };
        var previous = new CrptProductCatalogItem
        {
            Gtin = "00000000000013",
            Name = "Previous",
            CategoryName = "Synthetic Previous Category",
            SyncedAt = SyncedAt,
        };

        CrptNkProductMapper.PreservePreviousCatalogFields(current, previous)
            .CategoryName.Should().Be("Synthetic Previous Category");
    }

    [Fact]
    public void ComputeCanOrderCodes_SetCardPublishedSignedKnownGroup_IsFalse()
    {
        const string json = """
            {
              "good_id": 2002,
              "gtin": "00000000000014",
              "good_name": "Synthetic Set",
              "good_status": "published",
              "good_detailed_status": ["published"],
              "is_set": true,
              "is_kit": false,
              "identified_by": [{ "value": "00000000000014", "type": "gtin", "level": "trade-unit" }]
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var item = CrptNkProductMapper.MapProductListEntry(doc.RootElement, SyncedAt);

        item.NkCardType.Should().Be(NkCardType.Set);
        item.IsSigned.Should().BeTrue();
        item.NkStatus.Should().Be("published");
        CrptNkProductMapper.ComputeCanOrderCodes(
            item.NkStatus,
            item.IsSigned,
            CrptProductGroup.Chemistry,
            item.NkCardType).Should().BeFalse();
    }

    [Theory]
    [InlineData("3307490000 Прочие средства для ароматизации", "Прочие средства для ароматизации")]
    [InlineData("3307490000", "3307490000")]
    public void ReadCategoryName_NormalizesTnvedPrefixedDirectCategory(string raw, string expected)
    {
        var json = $$"""{"category": "{{raw}}"}""";
        using var doc = JsonDocument.Parse(json);

        CrptNkProductMapper.ReadCategoryName(doc.RootElement).Should().Be(expected);
    }

    [Fact]
    public void MapProductListEntry_NormalizesTnvedPrefixedCategory()
    {
        const string json = """
            {
              "good_id": 3001,
              "gtin": "00000000000020",
              "good_name": "Synthetic Aroma Product",
              "good_status": "published",
              "good_detailed_status": ["published"],
              "category": "3307490000 Прочие средства для ароматизации"
            }
            """;
        using var doc = JsonDocument.Parse(json);

        CrptNkProductMapper.MapProductListEntry(doc.RootElement, SyncedAt)
            .CategoryName.Should().Be("Прочие средства для ароматизации");
    }

    [Fact]
    public void MergeFeedProduct_NormalizesTnvedPrefixedCategory()
    {
        var baseline = new CrptProductCatalogItem
        {
            Gtin = "00000000000021",
            Name = "Baseline",
            NkStatus = "published",
            IsSigned = true,
            SyncedAt = SyncedAt,
        };

        const string feedJson = """
            {
              "identified_by": [{ "value": "00000000000021", "type": "gtin", "level": "trade-unit" }],
              "good_status": "published",
              "good_signed": true,
              "category": "3307490000 Прочие средства для ароматизации"
            }
            """;
        using var doc = JsonDocument.Parse(feedJson);

        CrptNkProductMapper.MergeFeedProduct(baseline, doc.RootElement, null, null, SyncedAt)
            .CategoryName.Should().Be("Прочие средства для ароматизации");
    }
}
