using System.Collections.ObjectModel;
using DoubleMark.Core.Crpt;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.ViewModels;

namespace DoubleMark.Desktop.ViewModels.Crpt;

public sealed class CrptPrintQueueViewModel : ViewModelBase
{
    private readonly CrptOrderRepository _orderRepository;
    private readonly ICrptProductCatalogStore _catalogStore;
    private readonly ICrptPrintService _printService;
    private readonly ICrptGisMtService _gisMtService;

    private string? _selectedOrderLocalId;
    private string _statusMessage = "";
    private bool _isBusy;
    private CrptMarkingCodeRowViewModel? _selectedCode;

    public CrptPrintQueueViewModel(
        CrptOrderRepository orderRepository,
        ICrptProductCatalogStore catalogStore,
        ICrptPrintService printService,
        ICrptGisMtService gisMtService)
    {
        _orderRepository = orderRepository;
        _catalogStore = catalogStore;
        _printService = printService;
        _gisMtService = gisMtService;

        MarkPrintedCommand = new AsyncRelayCommand(MarkPrintedAsync, CanMarkPrinted);
        RenderLabelCommand = new AsyncRelayCommand(RenderSelectedLabelAsync, CanRenderSelectedLabel);
        RenderBatchCommand = new AsyncRelayCommand(RenderBatchAsync, CanRenderBatch);
        SendUtilisationCommand = new AsyncRelayCommand(SendUtilisationAsync, CanSendUtilisation);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
    }

    public ObservableCollection<CrptSuzOrder> Orders { get; } = [];
    public ObservableCollection<CrptMarkingCodeRowViewModel> Codes { get; } = [];

    public AsyncRelayCommand MarkPrintedCommand { get; }
    public AsyncRelayCommand RenderLabelCommand { get; }
    public AsyncRelayCommand RenderBatchCommand { get; }
    public AsyncRelayCommand SendUtilisationCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public bool IsCatalogEmpty => _catalogStore.GetOrderableItems().Count == 0;

    public string? SelectedOrderLocalId
    {
        get => _selectedOrderLocalId;
        set
        {
            if (!SetProperty(ref _selectedOrderLocalId, value))
                return;

            _ = LoadCodesAsync();
        }
    }

    public CrptMarkingCodeRowViewModel? SelectedCode
    {
        get => _selectedCode;
        set
        {
            if (!SetProperty(ref _selectedCode, value))
                return;

            MarkPrintedCommand.RaiseCanExecuteChanged();
            RenderLabelCommand.RaiseCanExecuteChanged();
            RenderBatchCommand.RaiseCanExecuteChanged();
            SendUtilisationCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            MarkPrintedCommand.RaiseCanExecuteChanged();
            RenderLabelCommand.RaiseCanExecuteChanged();
            RenderBatchCommand.RaiseCanExecuteChanged();
            SendUtilisationCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }

    public void Load() => _ = RefreshAsync();

    public void SetPreselectedOrder(string? orderLocalId)
    {
        SelectedOrderLocalId = orderLocalId;
        if (orderLocalId is not null &&
            Orders.All(o => o.LocalId != orderLocalId))
            _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var orders = await _orderRepository.ListAsync();
            Orders.Clear();
            foreach (var order in orders.OrderByDescending(o => o.CreatedAt))
                Orders.Add(order);

            OnPropertyChanged(nameof(IsCatalogEmpty));

            if (SelectedOrderLocalId is null)
                SelectedOrderLocalId = Orders.FirstOrDefault()?.LocalId;
            else if (Orders.All(o => o.LocalId != SelectedOrderLocalId))
                SelectedOrderLocalId = Orders.FirstOrDefault()?.LocalId;

            await LoadCodesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public static bool CanMarkCodePrinted(CrptMarkingCodeItem code) =>
        code.Status switch
        {
            CrptCodeLifecycleStatus.Received =>
                CrptCodeLifecycleTransitions.CanTransition(
                    CrptCodeLifecycleStatus.Received,
                    CrptCodeLifecycleStatus.QueuedForPrint),
            CrptCodeLifecycleStatus.QueuedForPrint =>
                CrptCodeLifecycleTransitions.CanTransition(
                    code.Status,
                    CrptCodeLifecycleStatus.Printed),
            _ => false,
        };

    public static CrptMarkingCodeItem MarkCodePrinted(CrptMarkingCodeItem code, DateTimeOffset printedAt)
    {
        var status = code.Status;
        if (status == CrptCodeLifecycleStatus.Received)
            status = CrptCodeLifecycleStatus.QueuedForPrint;

        if (!CrptCodeLifecycleTransitions.CanTransition(status, CrptCodeLifecycleStatus.Printed))
            throw new InvalidOperationException($"Cannot mark code #{code.Id} as printed from {code.Status}.");

        var updated = code with
        {
            Status = CrptCodeLifecycleStatus.Printed,
            PrintedAt = printedAt,
        };

        if (!string.Equals(updated.RawPayload, code.RawPayload, StringComparison.Ordinal))
            throw new InvalidOperationException($"Printed status must not alter raw payload for code #{code.Id}.");

        return updated;
    }

    public static bool IsPrintableCode(CrptMarkingCodeItem code) =>
        code.Status is CrptCodeLifecycleStatus.Received or CrptCodeLifecycleStatus.QueuedForPrint;

    public static IReadOnlyList<CrptMarkingCodeItem> SelectPrintableCodes(IEnumerable<CrptMarkingCodeItem> codes) =>
        codes.Where(IsPrintableCode).ToList();

    public static IReadOnlyList<CrptMarkingCodeItem> SelectUtilisationCandidates(IEnumerable<CrptMarkingCodeItem> codes) =>
        codes.Where(c => c.Status == CrptCodeLifecycleStatus.Printed).ToList();

    public static bool CanSubmitUtilisation(IEnumerable<CrptMarkingCodeItem> codes) =>
        SelectUtilisationCandidates(codes).Count > 0;

    private bool CanMarkPrinted() => !IsBusy && SelectedCode is not null && SelectedCode.CanMarkPrinted;

    private bool CanRenderSelectedLabel() =>
        !IsBusy && SelectedCode is not null && IsPrintableCode(SelectedCode.Source);

    private bool CanRenderBatch() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(SelectedOrderLocalId) &&
        Codes.Any(c => IsPrintableCode(c.Source));

    private bool CanSendUtilisation() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(SelectedOrderLocalId) &&
        CanSubmitUtilisation(Codes.Select(c => c.Source));

    private async Task MarkPrintedAsync()
    {
        if (SelectedCode?.Source is not { } source || !CanMarkCodePrinted(source))
            return;

        IsBusy = true;
        try
        {
            var updated = MarkCodePrinted(source, DateTimeOffset.UtcNow);
            await _orderRepository.UpdateCodeAsync(updated);
            await LoadCodesAsync();
            StatusMessage = $"Код #{updated.Id} отмечен как напечатанный.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task RenderSelectedLabelAsync()
    {
        if (SelectedCode?.Source is not { } source || !IsPrintableCode(source))
            return Task.CompletedTask;

        IsBusy = true;
        try
        {
            var render = _printService.RenderLabel(source, CrptPrintDefaults.MinimalTemplate);
            StatusMessage = $"Этикетка #{source.Id} подготовлена ({render.PngBytes.Length} байт PNG, GS={render.GsCount}).";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        return Task.CompletedTask;
    }

    private Task RenderBatchAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedOrderLocalId))
            return Task.CompletedTask;

        var printable = SelectPrintableCodes(Codes.Select(c => c.Source));
        if (printable.Count == 0)
            return Task.CompletedTask;

        IsBusy = true;
        try
        {
            var renders = _printService.RenderBatch(printable, CrptPrintDefaults.MinimalTemplate);
            StatusMessage = $"Пакетная подготовка: {renders.Count} этикеток через MarkRenderService.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        return Task.CompletedTask;
    }

    private async Task SendUtilisationAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedOrderLocalId))
            return;

        var candidates = SelectUtilisationCandidates(Codes.Select(c => c.Source));
        if (candidates.Count == 0)
        {
            StatusMessage = "Отчёт о нанесении доступен только для кодов со статусом Printed.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _gisMtService.SendUtilisationForCodesAsync(
                candidates.Select(c => c.Id).ToList(),
                CancellationToken.None);
            await LoadCodesAsync();
            StatusMessage = $"Отчёт о нанесении отправлен: {result.DocumentId} ({result.CodesSubmitted} кодов).";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadCodesAsync()
    {
        Codes.Clear();
        SelectedCode = null;

        if (string.IsNullOrWhiteSpace(SelectedOrderLocalId))
            return;

        var codes = await _orderRepository.ListCodesByOrderAsync(SelectedOrderLocalId);
        foreach (var code in codes)
            Codes.Add(new CrptMarkingCodeRowViewModel(code));

        SelectedCode = Codes.FirstOrDefault();
        RenderLabelCommand.RaiseCanExecuteChanged();
        RenderBatchCommand.RaiseCanExecuteChanged();
        SendUtilisationCommand.RaiseCanExecuteChanged();
    }
}

internal static class CrptPrintDefaults
{
    public static PrintTemplate MinimalTemplate { get; } = new()
    {
        Name = "CRPT minimal",
        LabelWidthMm = 58,
        LabelHeightMm = 40,
        DataMatrixWidthMm = 24,
        DataMatrixHeightMm = 24,
        DataMatrixXmm = 2,
        DataMatrixYmm = 8,
        MarginMm = 1,
        RotationDegrees = 0,
        DefaultCopies = 1,
    };
}

public sealed class CrptMarkingCodeRowViewModel : ViewModelBase
{
    public CrptMarkingCodeRowViewModel(CrptMarkingCodeItem source) => Source = source;

    public CrptMarkingCodeItem Source { get; private set; }

    public int Id => Source.Id;
    public string StatusText => Source.Status.ToString();
    public string PayloadHint => "Код маркировки (скрыт)";
    public bool CanMarkPrinted => CrptPrintQueueViewModel.CanMarkCodePrinted(Source);

    public void Refresh(CrptMarkingCodeItem source)
    {
        Source = source;
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanMarkPrinted));
    }
}
