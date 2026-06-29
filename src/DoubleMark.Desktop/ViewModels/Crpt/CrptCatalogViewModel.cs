using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;

using DoubleMark.Core.Crpt;

using DoubleMark.Crpt;

using DoubleMark.Desktop.Services;

using DoubleMark.Desktop.Services.Crpt;

using DoubleMark.Desktop.Settings;

using DoubleMark.Desktop.ViewModels;



namespace DoubleMark.Desktop.ViewModels.Crpt;



public sealed class CrptCatalogViewModel : ViewModelBase

{

    private readonly ICrptProductCatalogStore _catalogStore;

    private readonly ICrptCatalogSyncService _syncService;

    private readonly ICrptSettingsStore _settingsStore;

    private readonly ICrptCertificateProvider _certificateProvider;

    private readonly ICrptAuthService _authService;



    private CrptCatalogFilter _filter = CrptCatalogFilter.All;

    private string _gtinFilter = "";

    private string _nameFilter = "";

    private string _tnvedFilter = "";

    private string _categoryFilter = "";

    private string _productGroupFilter = "";

    private CrptCatalogProductStateFilter _productStateFilter = CrptCatalogProductStateFilter.All;

    private CrptCatalogCardStatusFilter _cardStatusFilter = CrptCatalogCardStatusFilter.All;

    private CrptCatalogCardTypeFilter _cardTypeFilter = CrptCatalogCardTypeFilter.All;

    private string _progressText = "";

    private string _lastSyncText = "—";

    private string _statusMessage = "";

    private string _elapsedTimeText = "";

    private string? _connectivityWarning;

    private string _emptyStateTitle = "Каталог пуст";

    private string _emptyStateMessage =

        "Нажмите «Обновить из НК» для загрузки GTIN. Если каталог пуст, откройте «Маркировка CRPT → Настройки маркировки»: укажите ИНН, проверьте URL НК и контур (sandbox/production), затем сохраните.";

    private bool _isSyncing;

    private double _syncProgressPercent;

    private int _storeItemCount;

    private CrptCatalogRowViewModel? _selectedItem;

    private CancellationTokenSource? _syncCts;

    private CancellationTokenSource? _searchDebounceCts;

    private bool _isUpdatingCategoryOptions;

    private bool _isUpdatingProductGroupOptions;

    private bool _hasActiveFilters;

    private Stopwatch? _syncStopwatch;

    private bool _isRestoringPresets;



    public CrptCatalogViewModel(

        ICrptProductCatalogStore catalogStore,

        ICrptCatalogSyncService syncService,

        ICrptSettingsStore settingsStore,

        ICrptCertificateProvider certificateProvider,

        ICrptAuthService authService)

    {

        _catalogStore = catalogStore;

        _syncService = syncService;

        _settingsStore = settingsStore;

        _certificateProvider = certificateProvider;

        _authService = authService;

        SyncCommand = new AsyncRelayCommand(SyncAsync, () => !IsSyncing);

        CancelCommand = new RelayCommand(CancelSync, () => IsSyncing);

        OrderCodesCommand = new RelayCommand(

            OrderSelectedCodes,

            () => SelectedItem?.CanOrderCodes == true);

        ResetFiltersCommand = new RelayCommand(ResetFilters, () => HasActiveFilters);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => Items.Count > 0);
    }



    public ObservableCollection<CrptCatalogRowViewModel> Items { get; } = [];

    public ObservableCollection<string> CategoryFilterOptions { get; } = [""];

    public ObservableCollection<string> ProductGroupFilterOptions { get; } = [""];



    public AsyncRelayCommand SyncCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand OrderCodesCommand { get; }

    public RelayCommand ResetFiltersCommand { get; }

    public RelayCommand ExportCsvCommand { get; }



    public Array FilterOptions { get; } = Enum.GetValues(typeof(CrptCatalogFilter));

    public Array ProductStateFilterOptions { get; } = Enum.GetValues(typeof(CrptCatalogProductStateFilter));

    public Array CardStatusFilterOptions { get; } = Enum.GetValues(typeof(CrptCatalogCardStatusFilter));

    public Array CardTypeFilterOptions { get; } = Enum.GetValues(typeof(CrptCatalogCardTypeFilter));



    public event Action<string>? OrderCodesRequested;

    public event Action? SettingsRequested;

    /// <summary>Request saving CSV text; handler returns true when file was written.</summary>
    public event Func<string, bool>? SaveCsvFileRequested;



    public string GtinFilter

    {

        get => _gtinFilter;

        set

        {

            if (!SetProperty(ref _gtinFilter, value))

                return;



            ScheduleTextFilterRefresh();

        }

    }



    public string NameFilter

    {

        get => _nameFilter;

        set

        {

            if (!SetProperty(ref _nameFilter, value))

                return;



            ScheduleTextFilterRefresh();

        }

    }



    public string TnvedFilter

    {

        get => _tnvedFilter;

        set

        {

            if (!SetProperty(ref _tnvedFilter, value))

                return;



            ScheduleTextFilterRefresh();

        }

    }



    public string CategoryFilter

    {

        get => _categoryFilter;

        set

        {

            if (!SetProperty(ref _categoryFilter, value))

                return;



            if (!_isUpdatingCategoryOptions)

                ApplyFilter();

        }

    }



    public string ProductGroupFilter

    {

        get => _productGroupFilter;

        set

        {

            if (!SetProperty(ref _productGroupFilter, value))

                return;



            if (!_isUpdatingProductGroupOptions)

                ApplyFilter();

        }

    }



    public CrptCatalogProductStateFilter ProductStateFilter

    {

        get => _productStateFilter;

        set

        {

            if (!SetProperty(ref _productStateFilter, value))

                return;



            ApplyFilter();

        }

    }



    public CrptCatalogCardStatusFilter CardStatusFilter

    {

        get => _cardStatusFilter;

        set

        {

            if (!SetProperty(ref _cardStatusFilter, value))

                return;



            ApplyFilter();

        }

    }



    public CrptCatalogCardTypeFilter CardTypeFilter

    {

        get => _cardTypeFilter;

        set

        {

            if (!SetProperty(ref _cardTypeFilter, value))

                return;



            ApplyFilter();

        }

    }



    public CrptCatalogFilter Filter

    {

        get => _filter;

        set

        {

            if (!SetProperty(ref _filter, value))

                return;



            ApplyFilter();

        }

    }



    public CrptCatalogRowViewModel? SelectedItem

    {

        get => _selectedItem;

        set

        {

            if (!SetProperty(ref _selectedItem, value))

                return;



            OrderCodesCommand.RaiseCanExecuteChanged();

            OnPropertyChanged(nameof(OrderCodesTooltip));

        }

    }



    public string? OrderCodesTooltip => CrptCatalogDisplayLabels.ResolveOrderCodesTooltip(SelectedItem);



    public string ProgressText

    {

        get => _progressText;

        private set => SetProperty(ref _progressText, value);

    }



    public string LastSyncText

    {

        get => _lastSyncText;

        private set => SetProperty(ref _lastSyncText, value);

    }



    public string StatusMessage

    {

        get => _statusMessage;

        private set => SetProperty(ref _statusMessage, value);

    }



    public string ElapsedTimeText

    {

        get => _elapsedTimeText;

        private set => SetProperty(ref _elapsedTimeText, value);

    }



    public string? ConnectivityWarning

    {

        get => _connectivityWarning;

        private set

        {

            if (!SetProperty(ref _connectivityWarning, value))

                return;



            OnPropertyChanged(nameof(ShowConnectivityWarning));

        }

    }



    public string EmptyStateTitle

    {

        get => _emptyStateTitle;

        private set => SetProperty(ref _emptyStateTitle, value);

    }



    public string EmptyStateMessage

    {

        get => _emptyStateMessage;

        private set => SetProperty(ref _emptyStateMessage, value);

    }



    public bool ShowConnectivityWarning => !string.IsNullOrWhiteSpace(ConnectivityWarning);



    public bool IsStoreEmpty => _storeItemCount == 0;



    public bool IsCatalogEmpty => Items.Count == 0;



    public bool IsFilteredEmpty => !IsStoreEmpty && IsCatalogEmpty;



    public bool ShowEmptyState => IsStoreEmpty && !IsSyncing;

    public bool ShowFilteredEmptyState => IsFilteredEmpty && !IsSyncing;

    public bool HasActiveFilters
    {
        get => _hasActiveFilters;
        private set
        {
            if (SetProperty(ref _hasActiveFilters, value))
                ResetFiltersCommand.RaiseCanExecuteChanged();
        }
    }



    public double SyncProgressPercent

    {

        get => _syncProgressPercent;

        private set => SetProperty(ref _syncProgressPercent, value);

    }



    public bool IsSyncing

    {

        get => _isSyncing;

        private set

        {

            if (!SetProperty(ref _isSyncing, value))

                return;



            SyncCommand.RaiseCanExecuteChanged();

            CancelCommand.RaiseCanExecuteChanged();

            OnPropertyChanged(nameof(ShowEmptyState));

            OnPropertyChanged(nameof(ShowFilteredEmptyState));

        }

    }



    public void Load()
    {
        RestoreCatalogUiPresets();
        RefreshItems();
        StatusMessage = "";
        ProgressText = "";
        ElapsedTimeText = "";
        _ = RefreshConnectivityWarningAsync();
    }



    public void OpenSettings() => SettingsRequested?.Invoke();



    public void RefreshItems()

    {

        var allItems = _catalogStore.List();

        LastSyncText = FormatLastSync(allItems);

        RebuildCategoryFilterOptions(allItems, _settingsStore.LoadSettings());

        RebuildProductGroupFilterOptions(allItems);

        ApplyFilter(allItems);

    }

    internal void RestoreCatalogUiPresets()
    {
        var presets = _settingsStore.LoadSettings().CatalogUi;
        _isRestoringPresets = true;
        try
        {
            SetProperty(ref _gtinFilter, presets.GtinFilter ?? "", nameof(GtinFilter));
            SetProperty(ref _nameFilter, presets.NameFilter ?? "", nameof(NameFilter));
            SetProperty(ref _tnvedFilter, presets.TnvedFilter ?? "", nameof(TnvedFilter));
            SetProperty(ref _categoryFilter, presets.CategoryFilter ?? "", nameof(CategoryFilter));
            SetProperty(ref _productGroupFilter, presets.ProductGroupFilter ?? "", nameof(ProductGroupFilter));
            SetProperty(ref _productStateFilter, presets.ProductStateFilter, nameof(ProductStateFilter));
            SetProperty(ref _cardStatusFilter, presets.CardStatusFilter, nameof(CardStatusFilter));
            SetProperty(ref _cardTypeFilter, presets.CardTypeFilter, nameof(CardTypeFilter));
            SetProperty(ref _filter, presets.Filter, nameof(Filter));
        }
        finally
        {
            _isRestoringPresets = false;
        }
    }

    internal void SaveCatalogUiPresets()
    {
        if (_isRestoringPresets)
            return;

        var settings = _settingsStore.LoadSettings();
        var secrets = _settingsStore.LoadSecrets();
        settings.CatalogUi = new CrptCatalogUiPresets
        {
            GtinFilter = GtinFilter,
            NameFilter = NameFilter,
            TnvedFilter = TnvedFilter,
            CategoryFilter = CategoryFilter,
            ProductGroupFilter = ProductGroupFilter,
            ProductStateFilter = ProductStateFilter,
            CardStatusFilter = CardStatusFilter,
            CardTypeFilter = CardTypeFilter,
            Filter = Filter,
        };
        _settingsStore.Save(settings, secrets);
    }

    private void ExportCsv()
    {
        if (Items.Count == 0)
            return;

        var csv = CrptCatalogCsvExporter.FormatCsv(Items);
        if (SaveCsvFileRequested?.Invoke(csv) == true)
            StatusMessage = $"Экспорт CSV: {Items.Count} строк.";
    }



    public static IEnumerable<CrptProductCatalogItem> FilterItems(

        IReadOnlyList<CrptProductCatalogItem> items,

        CrptCatalogFilter filter,

        IReadOnlyList<string>? visibleCategories = null,

        string? gtinFilter = null,

        string? nameFilter = null,

        string? tnvedFilter = null,

        string? categoryFilter = null,

        string? productGroupFilter = null,

        CrptCatalogProductStateFilter productStateFilter = CrptCatalogProductStateFilter.All,

        CrptCatalogCardStatusFilter cardStatusFilter = CrptCatalogCardStatusFilter.All,

        CrptCatalogCardTypeFilter cardTypeFilter = CrptCatalogCardTypeFilter.All)

    {

        var filtered = ApplyColumnTextFilters(items, gtinFilter, nameFilter, tnvedFilter);

        filtered = CrptNkCategoryDiscovery.FilterByVisibleCategories(filtered, visibleCategories);

        filtered = ApplyCategoryFilter(filtered, categoryFilter);

        filtered = ApplyProductGroupFilter(filtered, productGroupFilter);

        filtered = ApplyProductStateFilter(filtered, productStateFilter);

        filtered = ApplyCardStatusFilter(filtered, cardStatusFilter);

        filtered = ApplyCardTypeFilter(filtered, cardTypeFilter);



        return filter switch

        {

            CrptCatalogFilter.OrderableOnly => filtered.Where(i => i.CanOrderCodes),

            CrptCatalogFilter.WithSyncErrors => filtered.Where(i => !string.IsNullOrWhiteSpace(i.SyncError)),

            _ => filtered,

        };

    }



    public static (string Title, string Message) ResolveEmptyState(int storeItemCount, bool hasActiveFilters)

    {

        if (storeItemCount == 0)

        {

            return (

                "Каталог пуст — обновите из НК",

                "Нажмите «Обновить из НК» для загрузки GTIN. Если каталог пуст, откройте «Маркировка CRPT → Настройки маркировки»: укажите ИНН, проверьте URL НК и контур (sandbox/production), затем сохраните.");

        }



        if (hasActiveFilters)

        {

            return (

                "Нет строк по фильтрам",

                "Измените поиск или фильтры, чтобы увидеть карточки каталога.");

        }



        return (

            "Каталог пуст — обновите из НК",

            "Нажмите «Обновить из НК» для загрузки GTIN.");

    }



    public static string FormatLastSync(IReadOnlyList<CrptProductCatalogItem> items)

    {

        if (items.Count == 0)

            return "—";



        var latest = items.Max(i => i.SyncedAt);

        return $"Последняя синхронизация: {latest.ToLocalTime():g}";

    }



    public static string FormatStageName(string stage) =>

        stage switch

        {

            "init" => "Подготовка",

            "connectivity-check" => "Проверка доступности НК",

            "product-list" => "Список товаров",

            "etagslist" => "Список изменений",

            "feed-product" => "Карточки товаров",

            "product-info" => "Информация о товарах",

            "complete" => "Завершение",

            _ => stage,

        };



    public static string FormatProgress(CrptCatalogSyncProgress progress)

    {

        if (progress.Stage == "complete")

            return "Синхронизация завершена";



        var stageName = FormatStageName(progress.Stage);

        var counts = progress.Total > 0 && progress.Total < int.MaxValue

            ? $"{progress.Processed} / {progress.Total}"

            : progress.Processed > 0

                ? $"{progress.Processed}"

                : "—";



        var gtinPart = string.IsNullOrWhiteSpace(progress.CurrentGtin)

            ? ""

            : $" · GTIN {progress.CurrentGtin}";



        return $"{stageName}: {counts}{gtinPart}";

    }



    public static double ComputeProgressPercent(CrptCatalogSyncProgress progress)

    {

        if (progress.Stage == "complete")

            return 100;



        if (progress.Total <= 0 || progress.Total >= int.MaxValue)

            return progress.Stage == "connectivity-check" ? 2 : 0;



        return Math.Min(100, (double)progress.Processed / progress.Total * 100);

    }



    public static string FormatSyncHeadline(

        CrptCatalogSyncResult result,

        int visibleItems,

        CrptSettings settings)

    {

        _ = settings;



        if (result.ListedInNk == 0)

            return "Синхронизация завершена: в НК 0 карточек";



        var storedCount = result.Added + result.Updated + result.Skipped;

        var inCatalog = visibleItems > 0 ? visibleItems : storedCount;



        if (inCatalog == 0 && result.Errors > 0)

            return $"В НК {result.ListedInNk} карточек, импортировано 0 (ошибок: {result.Errors})";



        return $"Загружено {result.ListedInNk} из НК, в каталоге {inCatalog}";

    }



    public static string FormatSyncResultMessage(

        CrptCatalogSyncResult result,

        int visibleItems,

        CrptCatalogFilter filter,

        CrptSettings settings)

    {

        _ = settings;



        var summary =

            $"Добавлено: {result.Added}, обновлено: {result.Updated}, без изменений: {result.Skipped}, ошибок: {result.Errors}.";



        if (result.ListedInNk > 0 && result.Added + result.Updated == 0 && visibleItems == 0)

            return summary + $" В НК найдено {result.ListedInNk} карточек, но ни одна не импортирована.";



        if (visibleItems == 0 && filter == CrptCatalogFilter.OrderableOnly && result.Added + result.Updated > 0)

        {

            return summary +

                   " Каталог загружен, но фильтр «Только для заказа» скрывает все строки — переключите фильтр на «Все».";

        }



        return summary;

    }



    internal static void ValidateSyncPrerequisites(CrptSettings settings, CrptSecrets secrets, ICrptCertificateProvider certificateProvider)

    {

        if (string.IsNullOrWhiteSpace(settings.Inn))

        {

            throw new InvalidOperationException(

                "ИНН не указан. Откройте «Настройки маркировки CRPT», введите ИНН и нажмите «Сохранить».");

        }



        if (settings.NkUseJwtFromTrueApi)

        {

            var connection = CrptConnectionSettingsBridge.ToConnectionSettings(settings, secrets);

            try

            {

                certificateProvider.FindCertificate(connection);

            }

            catch (Exception ex)

            {

                throw new InvalidOperationException(

                    "УКЭП не найден для синхронизации каталога. Выберите сертификат в настройках маркировки CRPT и нажмите «Сохранить». " +

                    ex.Message,

                    ex);

            }

        }

    }



    public static string FormatElapsed(TimeSpan elapsed)

    {

        if (elapsed.TotalHours >= 1)

            return $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";



        return $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

    }



    private static IEnumerable<CrptProductCatalogItem> ApplyColumnTextFilters(

        IEnumerable<CrptProductCatalogItem> items,

        string? gtinFilter,

        string? nameFilter,

        string? tnvedFilter)

    {

        if (!string.IsNullOrWhiteSpace(gtinFilter))

        {

            var term = gtinFilter.Trim();

            items = items.Where(item => item.Gtin.Contains(term, StringComparison.OrdinalIgnoreCase));

        }



        if (!string.IsNullOrWhiteSpace(nameFilter))

        {

            var term = nameFilter.Trim();

            items = items.Where(item => item.Name.Contains(term, StringComparison.OrdinalIgnoreCase));

        }



        if (!string.IsNullOrWhiteSpace(tnvedFilter))

        {

            var term = tnvedFilter.Trim();

            items = items.Where(item =>

                item.TnvedCode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);

        }



        return items;

    }



    private static IEnumerable<CrptProductCatalogItem> ApplyCategoryFilter(

        IEnumerable<CrptProductCatalogItem> items,

        string? categoryFilter)

    {

        if (string.IsNullOrWhiteSpace(categoryFilter))

            return items;



        var term = CrptNkCategoryDiscovery.NormalizeCategoryName(categoryFilter.Trim()) ?? "";

        return items.Where(item =>
        {
            var itemCategory = CrptNkCategoryDiscovery.NormalizeCategoryName(item.CategoryName);
            return !string.IsNullOrWhiteSpace(itemCategory) &&
                   string.Equals(itemCategory, term, StringComparison.OrdinalIgnoreCase);
        });

    }



    private static IEnumerable<CrptProductCatalogItem> ApplyProductGroupFilter(

        IEnumerable<CrptProductCatalogItem> items,

        string? productGroupFilter)

    {

        if (string.IsNullOrWhiteSpace(productGroupFilter))

            return items;



        var code = productGroupFilter.Trim();

        return items.Where(item =>

            !string.IsNullOrWhiteSpace(item.ProductGroup) &&

            string.Equals(item.ProductGroup.Trim(), code, StringComparison.OrdinalIgnoreCase));

    }



    private static IEnumerable<CrptProductCatalogItem> ApplyProductStateFilter(

        IEnumerable<CrptProductCatalogItem> items,

        CrptCatalogProductStateFilter filter)

    {

        if (filter == CrptCatalogProductStateFilter.All)

            return items;



        var expected = filter switch

        {

            CrptCatalogProductStateFilter.Published => NkProductState.Published,

            CrptCatalogProductStateFilter.Draft => NkProductState.Draft,

            CrptCatalogProductStateFilter.Moderation => NkProductState.Moderation,

            CrptCatalogProductStateFilter.Errors => NkProductState.Errors,

            CrptCatalogProductStateFilter.Archived => NkProductState.Archived,

            _ => (NkProductState?)null,

        };



        return expected is null

            ? items

            : items.Where(item => item.NkProductState == expected);

    }



    private static IEnumerable<CrptProductCatalogItem> ApplyCardStatusFilter(

        IEnumerable<CrptProductCatalogItem> items,

        CrptCatalogCardStatusFilter filter)

    {

        if (filter == CrptCatalogCardStatusFilter.All)

            return items;



        var expected = filter switch

        {

            CrptCatalogCardStatusFilter.Published => "published",

            CrptCatalogCardStatusFilter.NotSigned => "notsigned",

            CrptCatalogCardStatusFilter.Draft => "draft",

            CrptCatalogCardStatusFilter.Moderation => "moderation",

            CrptCatalogCardStatusFilter.Errors => "errors",

            _ => null,

        };



        return expected is null

            ? items

            : items.Where(item =>

                string.Equals(item.NkCardStatusPrimary, expected, StringComparison.OrdinalIgnoreCase));

    }



    private static IEnumerable<CrptProductCatalogItem> ApplyCardTypeFilter(

        IEnumerable<CrptProductCatalogItem> items,

        CrptCatalogCardTypeFilter filter)

    {

        if (filter == CrptCatalogCardTypeFilter.All)

            return items;



        var expected = filter switch

        {

            CrptCatalogCardTypeFilter.TradeUnit => NkCardType.TradeUnit,

            CrptCatalogCardTypeFilter.Set => NkCardType.Set,

            CrptCatalogCardTypeFilter.Kit => NkCardType.Kit,

            _ => (NkCardType?)null,

        };



        return expected is null

            ? items

            : items.Where(item => item.NkCardType == expected);

    }



    private async Task RefreshConnectivityWarningAsync()

    {

        var settings = _settingsStore.LoadSettings();

        if (settings.Environment != CrptEnvironment.Production)

        {

            ConnectivityWarning = null;

            return;

        }



        var (success, error) = await CrptNkConnectivity.TryCheckReachableAsync(settings.NkBaseUrl);

        ConnectivityWarning = success

            ? null

            : error ?? "Промышленный контур НК недоступен. Проверьте VPN или доступ к серверам ЦРПТ.";

    }



    private async Task SyncAsync()

    {

        IsSyncing = true;

        StatusMessage = "Синхронизация каталога…";

        ProgressText = FormatStageName("connectivity-check");

        SyncProgressPercent = 0;

        ElapsedTimeText = "00:00";



        _syncCts = new CancellationTokenSource();

        _syncStopwatch = Stopwatch.StartNew();

        using var elapsedCts = CancellationTokenSource.CreateLinkedTokenSource(_syncCts.Token);

        var elapsedTask = RunElapsedTimerAsync(elapsedCts.Token);



        try

        {

            var (settings, secrets) = _settingsStore.Load();

            ValidateSyncPrerequisites(settings, secrets, _certificateProvider);

            if (settings.NkUseJwtFromTrueApi)

                await EnsureValidAuthTokenAsync(_syncCts.Token);



            var progress = new Progress<CrptCatalogSyncProgress>(p =>

            {

                ProgressText = FormatProgress(p);

                SyncProgressPercent = ComputeProgressPercent(p);

            });



            var result = await _syncService.SyncAsync(progress, _syncCts.Token);

            RefreshItems();

            StatusMessage = FormatSyncResultMessage(result, Items.Count, Filter, settings);

            ProgressText = FormatSyncHeadline(result, Items.Count, settings);

            SyncProgressPercent = 100;

        }

        catch (OperationCanceledException) when (_syncCts.IsCancellationRequested)

        {

            StatusMessage = "Синхронизация отменена.";

            ProgressText = "";

        }

        catch (Exception ex)

        {

            LoggingService.Error("CrptCatalog", "Catalog sync failed", ex);

            StatusMessage = "Ошибка синхронизации: " + ex.Message;

            ProgressText = "";

        }

        finally

        {

            elapsedCts.Cancel();

            try

            {

                await elapsedTask;

            }

            catch (OperationCanceledException)

            {

                // Expected when sync completes or is cancelled.

            }



            _syncStopwatch = null;

            _syncCts.Dispose();

            _syncCts = null;

            IsSyncing = false;

            _ = RefreshConnectivityWarningAsync();

        }

    }



    private async Task EnsureValidAuthTokenAsync(CancellationToken cancellationToken)

    {

        try

        {

            await _authService.GetValidTokenAsync(cancellationToken);

        }

        catch (Exception ex)

        {

            throw new InvalidOperationException(

                "Не удалось получить JWT True API для синхронизации каталога. " +

                "Откройте «Настройки маркировки CRPT», выберите УКЭП, нажмите «Сохранить» и «Проверить подключение». " +

                ex.Message,

                ex);

        }



        var expiresAt = _authService.TokenExpiresAt;

        if (expiresAt is null || !CrptAuthResponseParser.IsPlausibleTokenExpiry(expiresAt.Value))

        {

            throw new InvalidOperationException(

                "JWT True API не получен. Нажмите «Проверить подключение» в настройках маркировки CRPT.");

        }

    }



    private async Task RunElapsedTimerAsync(CancellationToken cancellationToken)

    {

        while (!cancellationToken.IsCancellationRequested)

        {

            if (_syncStopwatch is not null)

                ElapsedTimeText = FormatElapsed(_syncStopwatch.Elapsed);



            try

            {

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            }

            catch (OperationCanceledException)

            {

                break;

            }

        }

    }



    private void CancelSync() => _syncCts?.Cancel();



    private void OrderSelectedCodes()

    {

        if (SelectedItem is null || !SelectedItem.CanOrderCodes)

            return;



        OrderCodesRequested?.Invoke(SelectedItem.Gtin);

    }



    private void ResetFilters()

    {

        _searchDebounceCts?.Cancel();

        _searchDebounceCts?.Dispose();

        _searchDebounceCts = null;



        SetProperty(ref _gtinFilter, "", nameof(GtinFilter));

        SetProperty(ref _nameFilter, "", nameof(NameFilter));

        SetProperty(ref _tnvedFilter, "", nameof(TnvedFilter));

        SetProperty(ref _categoryFilter, "", nameof(CategoryFilter));

        SetProperty(ref _productGroupFilter, "", nameof(ProductGroupFilter));

        SetProperty(ref _productStateFilter, CrptCatalogProductStateFilter.All, nameof(ProductStateFilter));

        SetProperty(ref _cardStatusFilter, CrptCatalogCardStatusFilter.All, nameof(CardStatusFilter));

        SetProperty(ref _cardTypeFilter, CrptCatalogCardTypeFilter.All, nameof(CardTypeFilter));

        SetProperty(ref _filter, CrptCatalogFilter.All, nameof(Filter));



        ApplyFilter();

    }



    private void ScheduleTextFilterRefresh()

    {

        _searchDebounceCts?.Cancel();

        _searchDebounceCts?.Dispose();

        _searchDebounceCts = new CancellationTokenSource();

        var token = _searchDebounceCts.Token;

        _ = DebounceTextFilterAsync(token);

    }



    private async Task DebounceTextFilterAsync(CancellationToken cancellationToken)

    {

        try

        {

            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);

            ApplyFilter();

        }

        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)

        {

            // Expected when search text changes again.

        }

    }



    private void ApplyFilter() => ApplyFilter(_catalogStore.List());



    private void ApplyFilter(IReadOnlyList<CrptProductCatalogItem> source)

    {

        var settings = _settingsStore.LoadSettings();

        var selectedGtin = SelectedItem?.Gtin;

        _storeItemCount = source.Count;



        var filtered = FilterItems(

            source,

            Filter,

            settings.NkVisibleCategories,

            GtinFilter,

            NameFilter,

            TnvedFilter,

            CategoryFilter,

            ProductGroupFilter,

            ProductStateFilter,

            CardStatusFilter,

            CardTypeFilter)

            .ToList();



        Items.Clear();

        foreach (var item in filtered)

            Items.Add(new CrptCatalogRowViewModel(item));

        ApplyDefaultSort();
        ExportCsvCommand.RaiseCanExecuteChanged();



        SelectedItem = selectedGtin is null

            ? null

            : Items.FirstOrDefault(row => row.Gtin == selectedGtin);



        HasActiveFilters = HasActiveFiltersCore(settings.NkVisibleCategories);

        var emptyState = ResolveEmptyState(_storeItemCount, HasActiveFilters);

        EmptyStateTitle = emptyState.Title;

        EmptyStateMessage = emptyState.Message;



        OnPropertyChanged(nameof(IsStoreEmpty));

        OnPropertyChanged(nameof(IsCatalogEmpty));

        OnPropertyChanged(nameof(IsFilteredEmpty));

        OnPropertyChanged(nameof(ShowEmptyState));

        OnPropertyChanged(nameof(ShowFilteredEmptyState));

        if (!_isRestoringPresets)
            SaveCatalogUiPresets();
    }



    private static void ApplyDefaultSort(ObservableCollection<CrptCatalogRowViewModel> items)
    {
        var view = CollectionViewSource.GetDefaultView(items);
        if (view is null || view.SortDescriptions.Count > 0)
            return;

        view.SortDescriptions.Add(new SortDescription(
            nameof(CrptCatalogRowViewModel.UpdatedAtSortKey),
            ListSortDirection.Descending));
    }

    private void ApplyDefaultSort() => ApplyDefaultSort(Items);



    private void RebuildCategoryFilterOptions(IReadOnlyList<CrptProductCatalogItem> source, CrptSettings settings)

    {

        var categories = CrptNkCategoryDiscovery.MergeKnownCategories(

            settings.NkKnownCategories,

            CrptNkCategoryDiscovery.CollectCategoryNames(source));



        var selected = _categoryFilter;

        _isUpdatingCategoryOptions = true;

        try

        {

            CategoryFilterOptions.Clear();

            CategoryFilterOptions.Add("");

            foreach (var category in categories)

                CategoryFilterOptions.Add(category);



            if (!string.IsNullOrWhiteSpace(selected) &&

                !categories.Contains(selected, StringComparer.OrdinalIgnoreCase))

            {

                CategoryFilterOptions.Add(selected);

            }

        }

        finally

        {

            _isUpdatingCategoryOptions = false;

        }



        SetProperty(ref _categoryFilter, selected, nameof(CategoryFilter));

    }



    private void RebuildProductGroupFilterOptions(IReadOnlyList<CrptProductCatalogItem> source)

    {

        var groups = source

            .Select(item => item.ProductGroup)

            .Where(group => !string.IsNullOrWhiteSpace(group))

            .Select(group => group!.Trim())

            .Distinct(StringComparer.OrdinalIgnoreCase)

            .OrderBy(group => CrptProductGroupCatalog.GetDisplayName(group), StringComparer.OrdinalIgnoreCase)

            .ToList();



        var selected = _productGroupFilter;

        _isUpdatingProductGroupOptions = true;

        try

        {

            ProductGroupFilterOptions.Clear();

            ProductGroupFilterOptions.Add("");

            foreach (var group in groups)

                ProductGroupFilterOptions.Add(group);



            if (!string.IsNullOrWhiteSpace(selected) &&

                !groups.Contains(selected, StringComparer.OrdinalIgnoreCase))

            {

                ProductGroupFilterOptions.Add(selected);

            }

        }

        finally

        {

            _isUpdatingProductGroupOptions = false;

        }



        SetProperty(ref _productGroupFilter, selected, nameof(ProductGroupFilter));

    }



    private bool HasActiveFiltersCore(IReadOnlyList<string> visibleCategories) =>

        !string.IsNullOrWhiteSpace(GtinFilter)

        || !string.IsNullOrWhiteSpace(NameFilter)

        || !string.IsNullOrWhiteSpace(TnvedFilter)

        || !string.IsNullOrWhiteSpace(CategoryFilter)

        || !string.IsNullOrWhiteSpace(ProductGroupFilter)

        || ProductStateFilter != CrptCatalogProductStateFilter.All

        || CardStatusFilter != CrptCatalogCardStatusFilter.All

        || CardTypeFilter != CrptCatalogCardTypeFilter.All

        || Filter != CrptCatalogFilter.All

        || (visibleCategories is { Count: > 0 });

}


