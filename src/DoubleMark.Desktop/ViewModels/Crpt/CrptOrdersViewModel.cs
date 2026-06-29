using System.Collections.ObjectModel;
using DoubleMark.Core.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.ViewModels;

namespace DoubleMark.Desktop.ViewModels.Crpt;

public sealed class CrptOrdersViewModel : ViewModelBase
{
    private readonly ICrptProductCatalogStore _catalogStore;
    private readonly ICrptSuzService _suzService;
    private readonly CrptOrderRepository _orderRepository;

    private string? _selectedGtin;
    private int _quantity = 1;
    private string _productGroup = "";
    private int? _templateId;
    private string _statusMessage = "";
    private bool _isBusy;
    private CrptSuzOrder? _selectedOrder;
    private string _progressText = "";

    public CrptOrdersViewModel(
        ICrptProductCatalogStore catalogStore,
        ICrptSuzService suzService,
        CrptOrderRepository orderRepository)
    {
        _catalogStore = catalogStore;
        _suzService = suzService;
        _orderRepository = orderRepository;

        CreateOrderCommand = new AsyncRelayCommand(CreateOrderAsync, CanCreateOrder);
        DownloadCodesCommand = new AsyncRelayCommand(DownloadCodesAsync, CanDownloadCodes);
        OpenPrintQueueCommand = new RelayCommand(OpenPrintQueue, () => SelectedOrder is not null);

        RefreshCatalogItems();
    }

    public ObservableCollection<CrptProductCatalogItem> OrderableItems { get; } = [];
    public ObservableCollection<CrptSuzOrder> Orders { get; } = [];

    public AsyncRelayCommand CreateOrderCommand { get; }
    public AsyncRelayCommand DownloadCodesCommand { get; }
    public RelayCommand OpenPrintQueueCommand { get; }

    public event Action<string>? OpenPrintQueueRequested;

    public bool IsCatalogEmpty => OrderableItems.Count == 0;

    public string? SelectedGtin
    {
        get => _selectedGtin;
        set
        {
            if (!SetProperty(ref _selectedGtin, value))
                return;

            UpdateSelectionFromCatalog();
            CreateOrderCommand.RaiseCanExecuteChanged();
        }
    }

    public int Quantity
    {
        get => _quantity;
        set
        {
            if (!SetProperty(ref _quantity, Math.Max(1, value)))
                return;

            CreateOrderCommand.RaiseCanExecuteChanged();
        }
    }

    public string ProductGroup
    {
        get => _productGroup;
        private set
        {
            if (!SetProperty(ref _productGroup, value))
                return;

            OnPropertyChanged(nameof(ProductGroupDisplay));
        }
    }

    public string ProductGroupDisplay => CrptProductGroupCatalog.GetDisplayName(ProductGroup);

    public int? TemplateId
    {
        get => _templateId;
        private set => SetProperty(ref _templateId, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            CreateOrderCommand.RaiseCanExecuteChanged();
            DownloadCodesCommand.RaiseCanExecuteChanged();
        }
    }

    public CrptSuzOrder? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (!SetProperty(ref _selectedOrder, value))
                return;

            DownloadCodesCommand.RaiseCanExecuteChanged();
            OpenPrintQueueCommand.RaiseCanExecuteChanged();
        }
    }

    public void Load() => RefreshAll();

    public void RefreshAll()
    {
        RefreshCatalogItems();
        RefreshOrders();
        StatusMessage = "";
        ProgressText = "";
    }

    public void SetPreselectedGtin(string? gtin)
    {
        RefreshCatalogItems();
        if (!string.IsNullOrWhiteSpace(gtin) &&
            OrderableItems.Any(i => i.Gtin == gtin))
            SelectedGtin = gtin;
    }

    public void RefreshCatalogItems()
    {
        OrderableItems.Clear();
        foreach (var item in _catalogStore.GetOrderableItems())
            OrderableItems.Add(item);

        OnPropertyChanged(nameof(IsCatalogEmpty));

        if (SelectedGtin is not null &&
            !OrderableItems.Any(i => i.Gtin == SelectedGtin))
            SelectedGtin = OrderableItems.FirstOrDefault()?.Gtin;

        if (SelectedGtin is null)
            SelectedGtin = OrderableItems.FirstOrDefault()?.Gtin;
    }

    public async Task RefreshOrdersAsync()
    {
        var orders = await _orderRepository.ListAsync();
        Orders.Clear();
        foreach (var order in orders.OrderByDescending(o => o.CreatedAt))
            Orders.Add(order);
    }

    private void RefreshOrders()
    {
        _ = RefreshOrdersAsync();
    }

    private void UpdateSelectionFromCatalog()
    {
        var item = OrderableItems.FirstOrDefault(i => i.Gtin == SelectedGtin);
        ProductGroup = item?.ProductGroup ?? "";
        TemplateId = item?.TemplateId;
    }

    private bool CanCreateOrder() =>
        !IsBusy &&
        !IsCatalogEmpty &&
        !string.IsNullOrWhiteSpace(SelectedGtin) &&
        Quantity > 0 &&
        !string.IsNullOrWhiteSpace(ProductGroup);

    private bool CanDownloadCodes() =>
        !IsBusy &&
        SelectedOrder?.RemoteOrderId is not null &&
        SelectedOrder.RemoteStatus != SuzOrderRemoteStatus.Error;

    private async Task CreateOrderAsync()
    {
        if (!CanCreateOrder())
            return;

        IsBusy = true;
        ProgressText = "Создание заказа…";

        try
        {
            var progress = new Progress<string>(message => ProgressText = message);
            await _suzService.CreateAndDownloadOrderAsync(
                SelectedGtin!,
                Quantity,
                ProductGroup,
                progress);

            await RefreshOrdersAsync();
            StatusMessage = "Заказ создан, коды скачаны.";
        }
        catch (Exception ex)
        {
            await RefreshOrdersAsync();
            StatusMessage = "Ошибка заказа: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    private async Task DownloadCodesAsync()
    {
        if (SelectedOrder?.RemoteOrderId is not { } remoteOrderId)
            return;

        IsBusy = true;
        ProgressText = "Скачивание кодов…";

        try
        {
            await _suzService.DownloadCodesAsync(
                SelectedOrder.LocalId,
                remoteOrderId,
                SelectedOrder.Gtin,
                SelectedOrder.RequestedQuantity);

            StatusMessage = "Коды скачаны.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Ошибка скачивания: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    private void OpenPrintQueue()
    {
        if (SelectedOrder is null)
            return;

        OpenPrintQueueRequested?.Invoke(SelectedOrder.LocalId);
    }
}
