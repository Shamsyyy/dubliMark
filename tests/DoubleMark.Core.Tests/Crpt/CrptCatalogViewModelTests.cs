using System.Security.Cryptography.X509Certificates;

using DoubleMark.Core.Crpt;

using DoubleMark.Crpt;

using DoubleMark.Desktop.Services.Crpt;

using DoubleMark.Desktop.Settings;

using DoubleMark.Desktop.ViewModels.Crpt;

using FluentAssertions;



namespace DoubleMark.Core.Tests.Crpt;



public class CrptCatalogViewModelTests

{

    private static CrptProductCatalogItem CreateItem(

        string gtin,

        bool canOrder,

        string? name = null,

        string? tnvedCode = null,

        NkProductState productState = NkProductState.Published,

        string cardStatusPrimary = "published",

        NkCardType cardType = NkCardType.TradeUnit,

        string? categoryName = null,

        string? productGroup = null,

        string? syncError = null,

        DateTimeOffset? updatedAt = null) =>

        new()

        {

            Gtin = gtin,

            Name = name ?? "Synthetic " + gtin,

            TnvedCode = tnvedCode,

            CategoryName = categoryName,

            ProductGroup = productGroup,

            NkProductState = productState,

            NkCardStatusPrimary = cardStatusPrimary,

            NkCardType = cardType,

            CanOrderCodes = canOrder,

            SyncError = syncError,

            NkUpdatedAt = updatedAt,

            SyncedAt = DateTimeOffset.UtcNow,

        };



    [Fact]

    public void FilterItems_OrderableOnly_ReturnsOnlyOrderable()

    {

        var items = new[]

        {

            CreateItem("00000000000001", canOrder: true),

            CreateItem("00000000000002", canOrder: false),

        };



        var filtered = CrptCatalogViewModel.FilterItems(items, CrptCatalogFilter.OrderableOnly).ToList();



        filtered.Should().HaveCount(1);

        filtered[0].Gtin.Should().Be("00000000000001");

    }



    [Fact]

    public void FilterItems_WithSyncErrors_ReturnsOnlyErrored()

    {

        var items = new[]

        {

            CreateItem("00000000000001", canOrder: true),

            CreateItem("00000000000002", canOrder: false, syncError: "nk timeout"),

        };



        var filtered = CrptCatalogViewModel.FilterItems(items, CrptCatalogFilter.WithSyncErrors).ToList();



        filtered.Should().HaveCount(1);

        filtered[0].Gtin.Should().Be("00000000000002");

    }



    [Fact]

    public void FilterItems_NameFilter_MatchesNameIgnoreCase()

    {

        var items = new[]

        {

            CreateItem("00000000000001", canOrder: true, name: "Сокол Premium"),

            CreateItem("00000000000002", canOrder: false, name: "Synthetic Beta"),

        };



        var filtered = CrptCatalogViewModel.FilterItems(

            items,

            CrptCatalogFilter.All,

            nameFilter: "сокол").ToList();



        filtered.Should().ContainSingle().Which.Name.Should().Be("Сокол Premium");

    }



    [Fact]

    public void FilterItems_GtinFilter_MatchesGtinSubstring()

    {

        var items = new[]

        {

            CreateItem("04601234567890", canOrder: true),

            CreateItem("00000000000002", canOrder: false),

        };



        var filtered = CrptCatalogViewModel.FilterItems(

            items,

            CrptCatalogFilter.All,

            gtinFilter: "6789").ToList();



        filtered.Should().ContainSingle().Which.Gtin.Should().Be("04601234567890");

    }



    [Fact]

    public void FilterItems_TnvedFilter_MatchesTnvedPartial()

    {

        var items = new[]

        {

            CreateItem("00000000000001", canOrder: true, tnvedCode: "3304990000"),

            CreateItem("00000000000002", canOrder: false, tnvedCode: "6403990000"),

        };



        var filtered = CrptCatalogViewModel.FilterItems(

            items,

            CrptCatalogFilter.All,

            tnvedFilter: "3304").ToList();



        filtered.Should().ContainSingle().Which.TnvedCode.Should().Be("3304990000");

    }



    [Fact]

    public void FilterItems_ColumnTextFilters_CombineWithAndLogic()

    {

        var items = new[]

        {

            CreateItem("04601234567890", canOrder: true, name: "Alpha One", tnvedCode: "3304990000"),

            CreateItem("04601234567891", canOrder: true, name: "Alpha Two", tnvedCode: "6403990000"),

        };



        var filtered = CrptCatalogViewModel.FilterItems(

            items,

            CrptCatalogFilter.All,

            gtinFilter: "7890",

            nameFilter: "Alpha",

            tnvedFilter: "3304").ToList();



        filtered.Should().ContainSingle().Which.Gtin.Should().Be("04601234567890");

    }



    [Fact]

    public void FilterItems_CategoryFilter_ReturnsMatchingCategoryOnly()

    {

        var items = new[]

        {

            CreateItem("00000000000001", canOrder: true, categoryName: "Synthetic Alpha"),

            CreateItem("00000000000002", canOrder: false, categoryName: "Synthetic Beta"),

        };



        var filtered = CrptCatalogViewModel.FilterItems(

            items,

            CrptCatalogFilter.All,

            categoryFilter: "Synthetic Beta").ToList();



        filtered.Should().ContainSingle().Which.CategoryName.Should().Be("Synthetic Beta");

    }



    [Fact]

    public void FilterItems_ProductGroupFilter_ReturnsMatchingGroupOnly()

    {

        var items = new[]

        {

            CreateItem("00000000000001", canOrder: true, productGroup: CrptProductGroup.Chemistry),

            CreateItem("00000000000002", canOrder: false, productGroup: CrptProductGroup.Milk),

        };



        var filtered = CrptCatalogViewModel.FilterItems(

            items,

            CrptCatalogFilter.All,

            productGroupFilter: CrptProductGroup.Milk).ToList();



        filtered.Should().ContainSingle().Which.ProductGroup.Should().Be(CrptProductGroup.Milk);

    }



    [Fact]

    public void FilterItems_ProductGroupFilter_EmptyValueReturnsAll()

    {

        var items = new[]

        {

            CreateItem("00000000000001", canOrder: true, productGroup: CrptProductGroup.Chemistry),

            CreateItem("00000000000002", canOrder: false, productGroup: CrptProductGroup.Milk),

        };



        var filtered = CrptCatalogViewModel.FilterItems(

            items,

            CrptCatalogFilter.All,

            productGroupFilter: "").ToList();



        filtered.Should().HaveCount(2);

    }



    [Fact]

    public void RefreshItems_BuildsProductGroupFilterOptionsFromLoadedItemsOnly()

    {

        var store = new TestCatalogStore(

            CreateItem("00000000000001", canOrder: true, productGroup: CrptProductGroup.Chemistry),

            CreateItem("00000000000002", canOrder: false, productGroup: CrptProductGroup.Milk),

            CreateItem("00000000000003", canOrder: false, productGroup: CrptProductGroup.Chemistry));

        var vm = CreateViewModel(store);

        vm.Load();



        vm.ProductGroupFilterOptions.Should().Equal("", CrptProductGroup.Chemistry, CrptProductGroup.Milk);

        vm.ProductGroupFilter.Should().BeEmpty();

    }



    [Fact]

    public void ProductGroupFilter_FiltersRowsBySelectedCode()

    {

        var store = new TestCatalogStore(

            CreateItem("00000000000001", canOrder: true, productGroup: CrptProductGroup.Chemistry),

            CreateItem("00000000000002", canOrder: false, productGroup: CrptProductGroup.Milk));

        var vm = CreateViewModel(store);

        vm.Load();



        vm.ProductGroupFilter = CrptProductGroup.Chemistry;



        vm.Items.Should().ContainSingle().Which.ProductGroup.Should().Be(CrptProductGroup.Chemistry);

        vm.HasActiveFilters.Should().BeTrue();

    }



    [Fact]

    public void FilterItems_ProductStateFilter_ReturnsMatchingStateOnly()

    {

        var items = new[]

        {

            CreateItem("00000000000001", canOrder: false, productState: NkProductState.Draft),

            CreateItem("00000000000002", canOrder: true, productState: NkProductState.Published),

        };



        var filtered = CrptCatalogViewModel.FilterItems(

            items,

            CrptCatalogFilter.All,

            productStateFilter: CrptCatalogProductStateFilter.Draft).ToList();



        filtered.Should().ContainSingle().Which.NkProductState.Should().Be(NkProductState.Draft);

    }



    [Fact]

    public void FilterItems_CardStatusFilter_ReturnsMatchingPrimaryStatus()

    {

        var items = new[]

        {

            CreateItem("00000000000001", canOrder: false, cardStatusPrimary: "notsigned"),

            CreateItem("00000000000002", canOrder: true, cardStatusPrimary: "published"),

        };



        var filtered = CrptCatalogViewModel.FilterItems(

            items,

            CrptCatalogFilter.All,

            cardStatusFilter: CrptCatalogCardStatusFilter.NotSigned).ToList();



        filtered.Should().ContainSingle().Which.NkCardStatusPrimary.Should().Be("notsigned");

    }



    [Fact]

    public void FilterItems_CardTypeFilter_ReturnsMatchingTypeOnly()

    {

        var items = new[]

        {

            CreateItem("00000000000001", canOrder: false, cardType: NkCardType.Set),

            CreateItem("00000000000002", canOrder: true, cardType: NkCardType.TradeUnit),

        };



        var filtered = CrptCatalogViewModel.FilterItems(

            items,

            CrptCatalogFilter.All,

            cardTypeFilter: CrptCatalogCardTypeFilter.Set).ToList();



        filtered.Should().ContainSingle().Which.NkCardType.Should().Be(NkCardType.Set);

    }



    [Fact]

    public void FilterItems_AllEnumFilters_DoNotRestrictWhenAllSelected()

    {

        var items = new[]

        {

            CreateItem("00000000000001", canOrder: false, productState: NkProductState.Draft, cardStatusPrimary: "draft", cardType: NkCardType.Set),

            CreateItem("00000000000002", canOrder: true, productState: NkProductState.Published, cardStatusPrimary: "published", cardType: NkCardType.TradeUnit),

        };



        var filtered = CrptCatalogViewModel.FilterItems(

            items,

            CrptCatalogFilter.All,

            productStateFilter: CrptCatalogProductStateFilter.All,

            cardStatusFilter: CrptCatalogCardStatusFilter.All,

            cardTypeFilter: CrptCatalogCardTypeFilter.All).ToList();



        filtered.Should().HaveCount(2);

    }



    [Fact]

    public void FilterItems_CombinesFiltersWithAndLogic()

    {

        var items = new[]

        {

            CreateItem("00000000000001", canOrder: true, productState: NkProductState.Published, cardStatusPrimary: "published", cardType: NkCardType.TradeUnit),

            CreateItem("00000000000002", canOrder: true, productState: NkProductState.Published, cardStatusPrimary: "published", cardType: NkCardType.Set),

            CreateItem("00000000000003", canOrder: false, productState: NkProductState.Draft, cardStatusPrimary: "draft", cardType: NkCardType.TradeUnit),

        };



        var filtered = CrptCatalogViewModel.FilterItems(

            items,

            CrptCatalogFilter.OrderableOnly,

            productStateFilter: CrptCatalogProductStateFilter.Published,

            cardTypeFilter: CrptCatalogCardTypeFilter.TradeUnit).ToList();



        filtered.Should().ContainSingle().Which.Gtin.Should().Be("00000000000001");

    }



    [Fact]

    public void ResolveEmptyState_WhenStoreEmpty_ShowsSyncNeededMessage()

    {

        var (title, message) = CrptCatalogViewModel.ResolveEmptyState(storeItemCount: 0, hasActiveFilters: false);



        title.Should().Be("Каталог пуст — обновите из НК");

        message.Should().Contain("Обновить из НК");

    }



    [Fact]

    public void ResolveEmptyState_WhenFilteredEmpty_ShowsFilterMessage()

    {

        var (title, message) = CrptCatalogViewModel.ResolveEmptyState(storeItemCount: 10, hasActiveFilters: true);



        title.Should().Be("Нет строк по фильтрам");

        message.Should().Contain("фильтр");

    }



    [Fact]

    public void CrptCatalogRowViewModel_ExposesRussianLabelsAndFormattedDate()

    {

        var updatedAt = new DateTimeOffset(2024, 3, 15, 10, 30, 0, TimeSpan.FromHours(3));

        var item = CreateItem(

            "00000000000001",

            canOrder: true,

            productState: NkProductState.Moderation,

            cardStatusPrimary: "notsigned",

            cardType: NkCardType.Kit,

            updatedAt: updatedAt);



        var row = new CrptCatalogRowViewModel(item);



        row.ProductStateDisplay.Should().Be("На модерации");

        row.CardStatusDisplay.Should().Be("Не подписана");

        row.CardTypeDisplay.Should().Be("Комплект");

        row.UpdatedAtDisplay.Should().Be(updatedAt.ToLocalTime().ToString("g"));

        row.CanOrderCodes.Should().BeTrue();

    }



    [Fact]

    public void FormatStageName_MapsKnownStagesToRussian()

    {

        CrptCatalogViewModel.FormatStageName("product-list").Should().Be("Список товаров");

        CrptCatalogViewModel.FormatStageName("feed-product").Should().Be("Карточки товаров");

        CrptCatalogViewModel.FormatStageName("connectivity-check").Should().Be("Проверка доступности НК");

    }



    [Fact]

    public void FormatProgress_ShowsStageCountsAndGtin()

    {

        var text = CrptCatalogViewModel.FormatProgress(

            new CrptCatalogSyncProgress("feed-product", 150, 230, "00000000000000"));



        text.Should().Be("Карточки товаров: 150 / 230 · GTIN 00000000000000");

    }



    [Fact]

    public void FormatProgress_Complete_ShowsFinishedMessage()

    {

        CrptCatalogViewModel.FormatProgress(

            new CrptCatalogSyncProgress("complete", 100, 100, null))

            .Should().Be("Синхронизация завершена");

    }



    [Fact]

    public void ComputeProgressPercent_ReturnsRatioForValidTotal()

    {

        CrptCatalogViewModel.ComputeProgressPercent(

            new CrptCatalogSyncProgress("feed-product", 50, 200, null))

            .Should().Be(25);

    }



    [Fact]

    public void ComputeProgressPercent_ConnectivityCheck_ReturnsSmallValue()

    {

        CrptCatalogViewModel.ComputeProgressPercent(

            new CrptCatalogSyncProgress("connectivity-check", 0, 0, null))

            .Should().Be(2);

    }



    [Fact]

    public void FormatElapsed_FormatsMinutesAndSeconds()

    {

        CrptCatalogViewModel.FormatElapsed(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(7))

            .Should().Be("03:07");

    }



    [Fact]

    public void FormatLastSync_WhenEmpty_ReturnsDash()

    {

        CrptCatalogViewModel.FormatLastSync(Array.Empty<CrptProductCatalogItem>()).Should().Be("—");

    }



    [Fact]

    public void FormatSyncHeadline_WhenAllStored_ShowsLoadedCount()

    {

        var settings = new CrptSettings();

        var result = new CrptCatalogSyncResult(51, 0, 0, 0, ListedInNk: 51);



        CrptCatalogViewModel.FormatSyncHeadline(result, 51, settings)

            .Should().Be("Загружено 51 из НК, в каталоге 51");

    }



    [Fact]

    public void FormatSyncHeadline_WhenPartialImport_ShowsCatalogCount()

    {

        var settings = new CrptSettings();

        var result = new CrptCatalogSyncResult(31, 0, 0, 20, ListedInNk: 51);



        CrptCatalogViewModel.FormatSyncHeadline(result, 31, settings)

            .Should().Be("Загружено 51 из НК, в каталоге 31");

    }



    [Fact]

    public void FormatSyncHeadline_DoesNotMentionPublishedOrSignedSkip()

    {

        var settings = new CrptSettings { NkSyncOnlyPublished = true, NkSyncOnlySigned = true };

        var result = new CrptCatalogSyncResult(

            51,

            0,

            0,

            0,

            ListedInNk: 51,

            FilteredByPublished: 20,

            FilteredBySigned: 10);



        var headline = CrptCatalogViewModel.FormatSyncHeadline(result, 51, settings);



        headline.Should().NotContain("опублик");

        headline.Should().NotContain("подпис");

        headline.Should().NotContain("фильтр");

        headline.Should().Be("Загружено 51 из НК, в каталоге 51");

    }



    [Fact]

    public void FormatSyncResultMessage_WhenNothingImported_DoesNotMentionSyncFilters()

    {

        var settings = new CrptSettings();

        var result = new CrptCatalogSyncResult(0, 0, 0, 51, ListedInNk: 51);



        var message = CrptCatalogViewModel.FormatSyncResultMessage(result, 0, CrptCatalogFilter.All, settings);



        message.Should().Contain("51");

        message.Should().NotContain("фильтр");

        message.Should().NotContain("настройках маркировки");

    }



    [Fact]

    public void FormatSyncResultMessage_WhenOrderableFilterHidesRows_MentionsFilter()

    {

        var settings = new CrptSettings();

        var result = new CrptCatalogSyncResult(10, 0, 0, 0, ListedInNk: 10);



        var message = CrptCatalogViewModel.FormatSyncResultMessage(result, 0, CrptCatalogFilter.OrderableOnly, settings);



        message.Should().Contain("Только для заказа");

    }



    [Fact]

    public void ResetFilters_ClearsAllFiltersAndRestoresRows()

    {

        var store = new TestCatalogStore(

            CreateItem("00000000000001", canOrder: true, cardType: NkCardType.TradeUnit),

            CreateItem("00000000000002", canOrder: true, cardType: NkCardType.Set));

        var vm = CreateViewModel(store);

        vm.Load();

        vm.Items.Should().HaveCount(2);



        vm.CardTypeFilter = CrptCatalogCardTypeFilter.Kit;

        vm.Items.Should().BeEmpty();

        vm.IsFilteredEmpty.Should().BeTrue();

        vm.ShowFilteredEmptyState.Should().BeTrue();

        vm.ShowEmptyState.Should().BeFalse();

        vm.HasActiveFilters.Should().BeTrue();



        vm.ResetFiltersCommand.Execute(null);



        vm.Items.Should().HaveCount(2);

        vm.GtinFilter.Should().BeEmpty();

        vm.NameFilter.Should().BeEmpty();

        vm.TnvedFilter.Should().BeEmpty();

        vm.CategoryFilter.Should().BeEmpty();

        vm.ProductGroupFilter.Should().BeEmpty();

        vm.ProductStateFilter.Should().Be(CrptCatalogProductStateFilter.All);

        vm.CardStatusFilter.Should().Be(CrptCatalogCardStatusFilter.All);

        vm.CardTypeFilter.Should().Be(CrptCatalogCardTypeFilter.All);

        vm.Filter.Should().Be(CrptCatalogFilter.All);

        vm.IsFilteredEmpty.Should().BeFalse();

        vm.HasActiveFilters.Should().BeFalse();

    }



    [Fact]

    public void ShowEmptyState_OnlyWhenStoreEmpty_NotWhenFilteredEmpty()

    {

        var store = new TestCatalogStore(CreateItem("00000000000001", canOrder: true));

        var vm = CreateViewModel(store);

        vm.Load();



        vm.ShowEmptyState.Should().BeFalse();

        vm.ShowFilteredEmptyState.Should().BeFalse();



        vm.CardTypeFilter = CrptCatalogCardTypeFilter.Set;

        vm.ShowEmptyState.Should().BeFalse();

        vm.ShowFilteredEmptyState.Should().BeTrue();

    }



    private static CrptCatalogViewModel CreateViewModel(TestCatalogStore store) =>

        new(

            store,

            new TestCatalogSyncService(),

            new TestSettingsStore(),

            new TestCertificateProvider(),

            new TestAuthService());



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



    private sealed class TestSettingsStore : ICrptSettingsStore

    {

        private CrptSettings _settings = new();



        public string SettingsPath => "test-settings.json";



        public string SecretsPath => "test-secrets.json";



        public CrptSettings LoadSettings() => _settings;



        public CrptSecrets LoadSecrets() => new();



        public (CrptSettings Settings, CrptSecrets Secrets) Load() => (_settings, new CrptSecrets());



        public CrptSettingsSnapshot LoadMerged() => new(_settings, new CrptSecrets());



        public void Save(CrptSettings settings, CrptSecrets secrets) => _settings = settings;



        public void Save(CrptSettingsSnapshot snapshot) => _settings = snapshot.Settings;

    }



    private sealed class TestCertificateProvider : ICrptCertificateProvider

    {

        public X509Certificate2 FindCertificate(CrptConnectionSettings settings) =>

            throw new NotSupportedException();



        public IReadOnlyList<CrptCertificateDescriptor> ListEligibleCertificates(string? innFilter = null) => [];

    }



    private sealed class TestAuthService : ICrptAuthService

    {

        public DateTimeOffset? TokenExpiresAt => null;



        public Task<string> GetValidTokenAsync(CancellationToken cancellationToken = default) =>

            Task.FromResult("test-token");



        public Task RefreshTokenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    }

}


