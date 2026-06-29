using System.Net;
using System.Net.Http.Headers;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptNkEtagsListTests
{
    [Fact]
    public void ParseEtagsListResponse_ReadsGoodsAndPagination()
    {
        const string json = """
                            {
                              "result": {
                                "goods_count": 2,
                                "offset": 0,
                                "last_product_number": 2,
                                "goods": [
                                  { "good_id": 101, "etag": "abc123" },
                                  { "good_id": 202, "etag": "def456" }
                                ]
                              }
                            }
                            """;

        var page = CrptNkProductMapper.ParseEtagsListResponse(json);

        page.GoodsCount.Should().Be(2);
        page.Offset.Should().Be(0);
        page.LastProductNumber.Should().Be(2);
        page.Entries.Should().HaveCount(2);
        page.Entries[0].GoodId.Should().Be(101);
        page.Entries[0].Etag.Should().Be("abc123");
    }

    [Fact]
    public void FindChangedGoodIds_DetectsNewAndChangedEtags()
    {
        var remote = new List<CrptNkEtagsListDiff.EtagsListEntry>
        {
            new(1, "etag-a"),
            new(2, "etag-b-new"),
            new(3, "etag-c"),
        };

        var existing = new Dictionary<int, CrptProductCatalogItem>
        {
            [1] = CreateItem("00000000000001", goodId: 1, etag: "etag-a"),
            [2] = CreateItem("00000000000002", goodId: 2, etag: "etag-b-old"),
        };

        var changed = CrptNkEtagsListDiff.FindChangedGoodIds(remote, existing);

        changed.Should().BeEquivalentTo([2]);
    }

    [Fact]
    public void FindChangedGoodIds_IgnoresUnknownRemoteGoodIds()
    {
        var remote = new List<CrptNkEtagsListDiff.EtagsListEntry>
        {
            new(1, "etag-a"),
            new(999, "etag-unknown"),
        };

        var existing = new Dictionary<int, CrptProductCatalogItem>
        {
            [1] = CreateItem("00000000000001", goodId: 1, etag: "etag-a"),
        };

        CrptNkEtagsListDiff.FindChangedGoodIds(remote, existing).Should().BeEmpty();
    }

    [Fact]
    public void FindChangedGoodIds_TreatsMissingRemoteEtagAsChanged()
    {
        var existing = new Dictionary<int, CrptProductCatalogItem>
        {
            [1] = CreateItem("00000000000001", goodId: 1, etag: "etag-a"),
        };

        CrptNkEtagsListDiff.FindChangedGoodIds([], existing).Should().BeEquivalentTo([1]);
    }

    [Fact]
    public void ShouldUseIncrementalSync_WhenEnabledAndCatalogLargeEnough()
    {
        var settings = new DoubleMark.Desktop.Settings.CrptSettings { NkIncrementalSyncEnabled = true };
        var items = Enumerable.Range(1, CrptCatalogSyncService.IncrementalSyncMinCatalogSize)
            .Select(i => CreateItem(i.ToString("D14"), goodId: i, etag: "x"))
            .ToArray();

        CrptCatalogSyncService.ShouldUseIncrementalSync(settings, items).Should().BeTrue();
    }

    [Fact]
    public void ShouldUseIncrementalSync_FalseWhenCatalogBelowThreshold()
    {
        var settings = new DoubleMark.Desktop.Settings.CrptSettings { NkIncrementalSyncEnabled = true };
        var items = Enumerable.Range(1, 51)
            .Select(i => CreateItem(i.ToString("D14"), goodId: i, etag: "x"))
            .ToArray();

        CrptCatalogSyncService.ShouldUseIncrementalSync(settings, items).Should().BeFalse();
    }

    [Fact]
    public void ShouldUseIncrementalSync_FalseWhenDisabledOrEmpty()
    {
        var disabled = new DoubleMark.Desktop.Settings.CrptSettings { NkIncrementalSyncEnabled = false };
        var enabled = new DoubleMark.Desktop.Settings.CrptSettings { NkIncrementalSyncEnabled = true };

        CrptCatalogSyncService.ShouldUseIncrementalSync(disabled, [CreateItem("1", 1, "x")]).Should().BeFalse();
        CrptCatalogSyncService.ShouldUseIncrementalSync(enabled, []).Should().BeFalse();
        CrptCatalogSyncService.ShouldFetchEtagsList(51).Should().BeFalse();
        CrptCatalogSyncService.ShouldFetchEtagsList(100).Should().BeTrue();
    }

    private static CrptProductCatalogItem CreateItem(string gtin, int goodId, string? etag) =>
        new()
        {
            Gtin = gtin,
            GoodId = goodId,
            NkEtag = etag,
            SyncedAt = DateTimeOffset.UtcNow,
        };
}
