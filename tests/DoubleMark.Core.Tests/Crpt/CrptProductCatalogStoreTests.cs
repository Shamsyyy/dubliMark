using DoubleMark.Core.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptProductCatalogStoreTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _catalogPath;
    private readonly CrptProductCatalogStore _store;

    public CrptProductCatalogStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DoubleMark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _catalogPath = Path.Combine(_tempDirectory, "crpt-catalog.json");
        _store = new CrptProductCatalogStore(_catalogPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp test directory.
        }
    }

    [Fact]
    public void Load_WhenMissing_ReturnsEmpty()
    {
        _store.Load().Should().BeEmpty();
    }

    [Fact]
    public void SaveLoad_RoundtripPreservesItems()
    {
        var items = CreateSampleItems();
        _store.Save(items);

        var loaded = _store.Load();
        loaded.Should().BeEquivalentTo(items);
        File.Exists(_catalogPath).Should().BeTrue();
    }

    [Fact]
    public void GetOrderableItems_ReturnsOnlyCanOrderCodes()
    {
        _store.Save(CreateSampleItems());

        var orderable = _store.GetOrderableItems();

        orderable.Should().HaveCount(1);
        orderable[0].Gtin.Should().Be("00000000000001");
        orderable.Should().OnlyContain(item => item.CanOrderCodes);
    }

    [Fact]
    public void Filter_AppliesPredicate()
    {
        _store.Save(CreateSampleItems());

        var chemistry = _store.Filter(item => item.ProductGroup == "chemistry");

        chemistry.Should().HaveCount(1);
        chemistry[0].Gtin.Should().Be("00000000000001");
    }

    private static IReadOnlyList<CrptProductCatalogItem> CreateSampleItems() =>
    [
        new CrptProductCatalogItem
        {
            Gtin = "00000000000000",
            Name = "Draft Product",
            NkStatus = "draft",
            IsSigned = false,
            CanOrderCodes = false,
            SyncedAt = DateTimeOffset.UtcNow,
        },
        new CrptProductCatalogItem
        {
            Gtin = "00000000000001",
            Name = "Ready Product",
            ProductGroup = "chemistry",
            NkStatus = "published",
            IsSigned = true,
            CanOrderCodes = true,
            TemplateId = 46,
            SyncedAt = DateTimeOffset.UtcNow,
        },
    ];
}
