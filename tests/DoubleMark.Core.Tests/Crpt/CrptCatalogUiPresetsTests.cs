using System.Security.Cryptography.X509Certificates;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.Settings;
using DoubleMark.Desktop.ViewModels.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptCatalogUiPresetsTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly CrptSettingsStore _store;

    public CrptCatalogUiPresetsTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DoubleMark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _store = new CrptSettingsStore(_tempDirectory);
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
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void CatalogUiPresets_RoundTripThroughSettingsStore()
    {
        var settings = new CrptSettings
        {
            Inn = "0000000000",
            CatalogUi = new CrptCatalogUiPresets
            {
                GtinFilter = "0462",
                NameFilter = "Synthetic",
                TnvedFilter = "3304",
                CategoryFilter = "Косметика",
                ProductGroupFilter = "chemistry",
                ProductStateFilter = CrptCatalogProductStateFilter.Draft,
                CardStatusFilter = CrptCatalogCardStatusFilter.NotSigned,
                CardTypeFilter = CrptCatalogCardTypeFilter.TradeUnit,
                Filter = CrptCatalogFilter.WithSyncErrors,
            },
        };

        _store.Save(settings, new CrptSecrets());
        var loaded = _store.LoadSettings();

        loaded.CatalogUi.GtinFilter.Should().Be("0462");
        loaded.CatalogUi.NameFilter.Should().Be("Synthetic");
        loaded.CatalogUi.TnvedFilter.Should().Be("3304");
        loaded.CatalogUi.CategoryFilter.Should().Be("Косметика");
        loaded.CatalogUi.ProductGroupFilter.Should().Be("chemistry");
        loaded.CatalogUi.ProductStateFilter.Should().Be(CrptCatalogProductStateFilter.Draft);
        loaded.CatalogUi.CardStatusFilter.Should().Be(CrptCatalogCardStatusFilter.NotSigned);
        loaded.CatalogUi.CardTypeFilter.Should().Be(CrptCatalogCardTypeFilter.TradeUnit);
        loaded.CatalogUi.Filter.Should().Be(CrptCatalogFilter.WithSyncErrors);
    }

    [Fact]
    public void ViewModel_SaveAndRestoreCatalogUiPresets()
    {
        var catalog = new TestCatalogStore(new CrptProductCatalogItem
        {
            Gtin = "00000000000001",
            Name = "Synthetic",
            SyncedAt = DateTimeOffset.UtcNow,
        });

        var settings = new CrptSettings
        {
            CatalogUi = new CrptCatalogUiPresets
            {
                NameFilter = "Synth",
                Filter = CrptCatalogFilter.OrderableOnly,
            },
        };
        _store.Save(settings, new CrptSecrets());

        var vm = new CrptCatalogViewModel(
            catalog,
            new TestCatalogSyncService(),
            _store,
            new TestCertificateProvider(),
            new TestAuthService());

        vm.RestoreCatalogUiPresets();
        vm.RefreshItems();

        vm.NameFilter.Should().Be("Synth");
        vm.Filter.Should().Be(CrptCatalogFilter.OrderableOnly);

        vm.NameFilter = "Updated";
        vm.Filter = CrptCatalogFilter.All;
        vm.SaveCatalogUiPresets();

        _store.LoadSettings().CatalogUi.NameFilter.Should().Be("Updated");
        _store.LoadSettings().CatalogUi.Filter.Should().Be(CrptCatalogFilter.All);
    }

    private sealed class TestCatalogStore(params CrptProductCatalogItem[] items) : ICrptProductCatalogStore
    {
        private List<CrptProductCatalogItem> _items = items.ToList();

        public string CatalogPath => "test-catalog.json";

        public IReadOnlyList<CrptProductCatalogItem> Load() => _items;

        public void Save(IReadOnlyList<CrptProductCatalogItem> items) => _items = items.ToList();

        public IReadOnlyList<CrptProductCatalogItem> List() => _items;

        public IReadOnlyList<CrptProductCatalogItem> Filter(Func<CrptProductCatalogItem, bool> predicate) =>
            _items.Where(predicate).ToList();

        public IReadOnlyList<CrptProductCatalogItem> GetOrderableItems() =>
            _items.Where(i => i.CanOrderCodes).ToList();
    }

    private sealed class TestCatalogSyncService : ICrptCatalogSyncService
    {
        public Task<CrptCatalogSyncResult> SyncAsync(
            IProgress<CrptCatalogSyncProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CrptCatalogSyncResult(0, 0, 0, 0));
    }

    private sealed class TestCertificateProvider : ICrptCertificateProvider
    {
        public X509Certificate2 FindCertificate(CrptConnectionSettings settings) =>
            throw new NotSupportedException();

        public IReadOnlyList<CrptCertificateDescriptor> ListEligibleCertificates(string? innFilter = null) => [];
    }

    private sealed class TestAuthService : ICrptAuthService
    {
        public DateTimeOffset? TokenExpiresAt => DateTimeOffset.UtcNow.AddHours(1);

        public Task<string> GetValidTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("token");

        public Task RefreshTokenAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
