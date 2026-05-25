using System.Globalization;
using System.Windows;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Views;

namespace DoubleMark.Desktop;

public partial class MainWindow
{
    private void WireTemplatesViewEvents(TemplatesView view)
    {
        view.TemplateSelected += OnTemplatesViewTemplateSelected;
        view.CreateTemplateRequested += OnTemplatesCreateRequested;
        view.CopyTemplateRequested += OnTemplatesCopyRequested;
        view.DeleteTemplateRequested += OnTemplatesDeleteRequested;
        view.DmPresetRequested += OnTemplatesDmPresetRequested;
        view.ApplyLayoutRequested += OnTemplatesApplyLayoutRequested;
        view.ApplyTextBlocksRequested += OnTemplatesApplyTextBlocksRequested;
        view.TextBlocksEditedRequested += OnTemplatesCanvasTextEdited;
        view.TextBlocksCommittedRequested += OnTemplatesCanvasTextCommitted;
        view.LabelExtrasApplyRequested += OnTemplatesLabelExtrasApplyRequested;
        view.PrintPreviewRequested += OnTemplatesPrintPreviewRequested;
        view.ManageTemplatesRequested += OnPrintTemplatesClick;
    }

    private async void OnTemplatesCreateRequested(object? sender, EventArgs e) =>
        await OnTemplatesCreateRequestedAsync();

    private async Task OnTemplatesCreateRequestedAsync()
    {
        if (!await EnsureSubscriptionForFeatureAsync("Управление шаблонами"))
            return;

        if (_printTemplates.Count == 0)
            _printTemplates = _printTemplateService.LoadOrCreateDefaults().ToList();

        var name = PrintTemplateService.CreateUniqueName(_printTemplates, "Новый шаблон");
        var template = TemplateLayoutHelper.CreateFromDmPreset(name, 14, 14);
        _printTemplates = _printTemplates.Append(template).ToList();
        await SaveTemplatesAsync(template.Name);
        _settings.DefaultPrintTemplateName = template.Name;
        _settings.Save();
        RefreshSettingsIntoUi();
        ShowToast($"Создан шаблон «{name}»", ToastKind.Success);
    }

    private async void OnTemplatesCopyRequested(object? sender, EventArgs e)
    {
        if (!await EnsureSubscriptionForFeatureAsync("Управление шаблонами"))
            return;

        var active = ResolveActiveTemplate();
        var name = PrintTemplateService.CreateUniqueName(_printTemplates, active.Name + " копия");
        _printTemplates = _printTemplates.Append(active with { Name = name }).ToList();
        await SaveTemplatesAsync(name);
        _settings.DefaultPrintTemplateName = name;
        _settings.Save();
        RefreshSettingsIntoUi();
        ShowToast($"Скопирован шаблон «{name}»", ToastKind.Success);
    }

    private async void OnTemplatesDeleteRequested(object? sender, EventArgs e)
    {
        if (!await EnsureSubscriptionForFeatureAsync("Управление шаблонами"))
            return;

        if (_printTemplates.Count <= 1)
        {
            ShowToast("Нельзя удалить последний шаблон", ToastKind.Warning);
            return;
        }

        var active = ResolveActiveTemplate();
        _printTemplates = _printTemplates
            .Where(t => !string.Equals(t.Name, active.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var next = _printTemplates[0].Name;
        await SaveTemplatesAsync(next);
        _settings.DefaultPrintTemplateName = next;
        _settings.Save();
        RefreshSettingsIntoUi();
        ShowToast($"Шаблон «{active.Name}» удалён", ToastKind.Success);
    }

    private async void OnTemplatesDmPresetRequested(object? sender, (double W, double H) preset)
    {
        if (!await EnsureSubscriptionForFeatureAsync("Управление шаблонами"))
            return;

        await ApplyDmSizeToActiveTemplateAsync(preset.W, preset.H);
    }

    private async void OnTemplatesApplyLayoutRequested(object? sender, TemplateLayoutEdit layout)
    {
        if (!await EnsureSubscriptionForFeatureAsync("Управление шаблонами"))
            return;

        var active = ResolveActiveTemplate();
        var updated = TemplateLayoutHelper.RelayoutTextBlocks(TemplateLayoutHelper.ClampDataMatrixInLabel(active with
        {
            LabelWidthMm = layout.LabelWidthMm,
            LabelHeightMm = layout.LabelHeightMm,
            DataMatrixWidthMm = layout.DataMatrixWidthMm,
            DataMatrixHeightMm = layout.DataMatrixHeightMm,
            DataMatrixXmm = layout.DataMatrixXmm,
            DataMatrixYmm = layout.DataMatrixYmm
        }));

        if (!PrintTemplateService.IsUsable(updated))
        {
            ShowToast("Некорректные размеры шаблона", ToastKind.Warning);
            return;
        }

        _printTemplates = _printTemplates
            .Select(t => string.Equals(t.Name, active.Name, StringComparison.OrdinalIgnoreCase) ? updated : t)
            .ToList();

        await SaveTemplatesAsync(active.Name);
        RefreshSettingsIntoUi();
        ShowToast("Параметры шаблона применены", ToastKind.Success);
    }

    private async void OnTemplatesApplyTextBlocksRequested(object? sender, TemplateTextEdit edit)
    {
        if (!await EnsureSubscriptionForFeatureAsync("Управление шаблонами"))
            return;

        if (!TryApplyTextBlocksInMemory(edit, out _))
            return;

        await SaveTemplatesAsync(ResolveActiveTemplate().Name);
        RefreshSettingsIntoUi();
        ShowToast("Позиции текста сохранены", ToastKind.Success);
    }

    private void OnTemplatesCanvasTextEdited(object? sender, TemplateTextEdit edit)
    {
        if (!TryApplyTextBlocksInMemory(edit, out var updated))
            return;

        _templatesView?.UpdatePreviewImage(RenderActiveTemplatePreview(updated));
    }

    private async void OnTemplatesCanvasTextCommitted(object? sender, TemplateTextEdit edit)
    {
        if (!await EnsureSubscriptionForFeatureAsync("Управление шаблонами"))
            return;

        if (!TryApplyTextBlocksInMemory(edit, out var updated))
            return;

        await SaveTemplatesAsync(updated.Name);
        _templatesView?.UpdatePreviewImage(RenderActiveTemplatePreview(updated));
    }

    private bool TryApplyTextBlocksInMemory(TemplateTextEdit edit, out PrintTemplate updated)
    {
        updated = ResolveActiveTemplate();
        var blocks = edit.Blocks
            .Where(b => !string.IsNullOrWhiteSpace(b.Text))
            .Select(b => new PrintTextBlock
            {
                Text = b.Text.Trim(),
                Xmm = b.Xmm,
                Ymm = b.Ymm,
                FontSizePt = Math.Clamp(b.FontSizePt, 2, 12),
                Bold = b.Bold,
                Orientation = b.Orientation
            })
            .ToList();

        if (blocks.Count == 0)
        {
            ShowToast("Добавьте хотя бы одну строку текста", ToastKind.Warning);
            return false;
        }

        updated = ResolveActiveTemplate() with { TextBlocks = blocks };
        if (!PrintTemplateService.IsUsable(updated))
        {
            ShowToast("Некорректные параметры текста", ToastKind.Warning);
            return false;
        }

        var activeName = updated.Name;
        var merged = updated;
        _printTemplates = _printTemplates
            .Select(t => string.Equals(t.Name, activeName, StringComparison.OrdinalIgnoreCase) ? merged : t)
            .ToList();
        return true;
    }

    private void OnTemplatesLabelExtrasApplyRequested(object? sender, LabelExtrasEdit extras)
    {
        _settings.LabelShowDate = extras.ShowDate;
        _settings.LabelShowShipment = extras.ShowShipment;
        _settings.LabelShowOrder = extras.ShowOrder;
        _settings.LabelShipmentNumber = string.IsNullOrWhiteSpace(extras.ShipmentNumber)
            ? null
            : extras.ShipmentNumber.Trim();
        _settings.LabelOrderNumber = string.IsNullOrWhiteSpace(extras.OrderNumber)
            ? null
            : extras.OrderNumber.Trim();
        _settings.Save();
        SyncConnectedViews();
        RefreshPreviewForActiveTemplate();
        ShowToast("Настройки текста этикетки сохранены", ToastKind.Success);
    }

    private void OnTemplatesPrintPreviewRequested(object? sender, EventArgs e)
    {
        var template = ResolveActiveTemplate();
        var png = TemplatePreviewRenderer.TryRenderPngBytes(
            template,
            _settings.LabelShowDate,
            _settings.LabelShowShipment,
            _settings.LabelShowOrder,
            _settings.LabelShipmentNumber,
            _settings.LabelOrderNumber,
            _lastSuccessfulScan?.ParseResult,
            _lastSuccessfulScan?.Raw,
            _lastSuccessfulScan?.Source ?? "Preview",
            dpi: 200);

        if (png == null)
        {
            ShowToast("Не удалось построить предпросмотр", ToastKind.Warning);
            return;
        }

        var subtitle = _lastSuccessfulScan == null
            ? "Демо-код (отсканируйте ЧЗ для реального предпросмотра)"
            : "Последний успешный скан";

        var window = new TemplatePrintPreviewWindow(png, subtitle) { Owner = this };
        window.ShowDialog();
    }

    private async Task SaveTemplatesAsync(string? activeName = null)
    {
        if (_accountSnapshot.User != null)
            await _userTemplateService.SaveAllTemplatesAsync(_printTemplates, activeName ?? _settings.DefaultPrintTemplateName);
        else
            _printTemplateService.SaveTemplates(_printTemplates);

        EnsureActiveTemplateSelection();
    }

    private async Task ApplyDmSizeToActiveTemplateAsync(double dmWidth, double dmHeight)
    {
        var active = ResolveActiveTemplate();
        var updated = TemplateLayoutHelper.ApplyDmSize(active, dmWidth, dmHeight);

        _printTemplates = _printTemplates
            .Select(t => string.Equals(t.Name, active.Name, StringComparison.OrdinalIgnoreCase) ? updated : t)
            .ToList();

        await SaveTemplatesAsync(active.Name);
        RefreshPrintSettingsUi();
        LoggingService.Info("Templates", $"DM size applied: {dmWidth:0.#}x{dmHeight:0.#}mm template={active.Name}");
        ShowToast($"DM: {dmWidth.ToString("0.#", CultureInfo.InvariantCulture)}×{dmHeight.ToString("0.#", CultureInfo.InvariantCulture)} мм", ToastKind.Success);
    }
}
