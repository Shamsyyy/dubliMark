using System.IO;
using Microsoft.Win32;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Services.Cloud;
using DoubleMark.Desktop.Settings;
using DoubleMark.Desktop.Views;

namespace DoubleMark.Desktop;

public partial class MainWindow
{
    private LocalScanHistoryService _localScanHistoryService = null!;
    private readonly CuratedHistoryExportService _curatedHistoryExportService = new();

    private void InitializeHistoryServices()
    {
        _localScanHistoryService = new LocalScanHistoryService();
    }

    private async Task ReloadScanHistoryAsync()
    {
        _uiHistory.Clear();

        if (_settings.HistoryViewMode == HistoryViewMode.Cloud)
        {
            if (!_settings.CloudHistoryEnabled || _accountSnapshot.User == null)
            {
                RebuildDashboardHistoryRows();
                UpdateHistoryUsageUi();
                return;
            }

            var items = await _cloudScanHistoryService.GetHistoryAsync();
            _uiHistory.AddRange(items);
            var usage = await _cloudScanHistoryService.GetHistoryUsageAsync();
            _historyUsageCount = usage.Count;
            _historyUsageLimit = usage.Limit;
        }
        else
        {
            if (!_settings.LocalHistoryEnabled)
            {
                RebuildDashboardHistoryRows();
                UpdateHistoryUsageUi();
                return;
            }

            var items = _localScanHistoryService.Load(_settings);
            _uiHistory.AddRange(items.Take(ScanHistoryStore.MaxEntries));
            _historyUsageCount = items.Count;
            _historyUsageLimit = ScanHistoryStore.MaxEntries;
        }

        RebuildDashboardHistoryRows();
        UpdateHistoryUsageUi();
        SyncHistorySettingsUi();
        SyncConnectedViews();
    }

    private void SyncHistorySettingsUi()
    {
        _historyView?.ApplySettings(
            _settings.CloudHistoryEnabled,
            _settings.LocalHistoryEnabled,
            _settings.HistoryViewMode,
            _settings.EffectiveLocalHistoryBrowseDirectory);
    }

    private async void OnHistorySettingsChanged(object? sender, EventArgs e)
    {
        if (_historyView == null)
            return;

        _settings.CloudHistoryEnabled = _historyView.CloudStorageEnabled;
        _settings.LocalHistoryEnabled = _historyView.LocalStorageEnabled;
        _settings.HistoryViewMode = _historyView.SelectedViewMode;
        _settings.Save();
        await ReloadScanHistoryAsync();
    }

    private async void OnHistoryBrowseFolderRequested(object? sender, EventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Папка с export JSON для локальной истории",
            InitialDirectory = _settings.EffectiveLocalHistoryBrowseDirectory
        };

        if (dialog.ShowDialog() != true)
            return;

        _settings.LocalHistoryDirectory = dialog.FolderName;
        _settings.Save();
        SyncHistorySettingsUi();
        await ReloadScanHistoryAsync();
        ShowToast("Локальная папка обновлена", ToastKind.Success);
    }

    private async void OnHistoryReloadRequested(object? sender, EventArgs e)
    {
        await ReloadScanHistoryAsync();
        ShowToast("История обновлена", ToastKind.Success);
    }

    private void OnHistoryExportSelectedRequested(object? sender, IReadOnlyList<ScanHistoryItem> items)
    {
        if (items.Count == 0)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Папка для выделенных кодов ЧЗ",
            InitialDirectory = Directory.Exists(_settings.EffectiveCuratedHistoryDirectory)
                ? _settings.EffectiveCuratedHistoryDirectory
                : _settings.EffectiveExportDirectory
        };

        if (dialog.ShowDialog() != true)
            return;

        _settings.CuratedHistoryDirectory = dialog.FolderName;
        _settings.Save();

        var result = _curatedHistoryExportService.ExportItems(items, dialog.FolderName);
        if (result.Exported == 0)
        {
            ShowToast("Не удалось сохранить выделенные коды (нет данных для экспорта)", ToastKind.Warning);
            return;
        }

        var message = result.Skipped > 0
            ? $"Сохранено {result.Exported} из {items.Count} в {result.TargetRoot}"
            : $"Сохранено {result.Exported} кодов в {result.TargetRoot}";
        ShowToast(message, ToastKind.Success);
    }
}
