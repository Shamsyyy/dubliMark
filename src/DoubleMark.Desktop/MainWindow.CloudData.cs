using System.Windows;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services.Cloud;
using DoubleMark.Desktop.Views;

namespace DoubleMark.Desktop;

public partial class MainWindow
{
    private CloudScanHistoryService _cloudScanHistoryService = null!;
    private UserTemplateService _userTemplateService = null!;
    private int _historyUsageCount;
    private int _historyUsageLimit = CloudScanHistoryService.MaxScanHistory;

    private void InitializeCloudDataServices()
    {
        _cloudScanHistoryService = new CloudScanHistoryService(
            _supabaseClientFactory,
            () => _accountSnapshot.User,
            () => _settings);
        _userTemplateService = new UserTemplateService(
            _supabaseClientFactory,
            () => _accountSnapshot.User,
            _printTemplateService);
    }

    private async Task LoadUserCloudDataAsync()
    {
        if (_accountSnapshot.User == null)
        {
            ClearUserCloudData();
            return;
        }

        await LoadCloudTemplatesAsync();
        await LoadCloudScanHistoryAsync();
        await MaybeOfferLocalTemplateMigrationAsync();
        UpdateHistoryUsageUi();
        SyncConnectedViews();
    }

    private async Task LoadCloudTemplatesAsync()
    {
        var templates = await _userTemplateService.EnsureDefaultTemplatesAsync();
        if (templates.Count > 0)
        {
            _printTemplates = templates;
            var cloud = await _userTemplateService.GetTemplatesAsync();
            var defaultCloud = cloud.FirstOrDefault(t => t.IsDefault) ?? cloud.FirstOrDefault();
            if (defaultCloud != null)
                _settings.DefaultPrintTemplateName = defaultCloud.Template.Name;
        }
    }

    private async Task LoadCloudScanHistoryAsync()
    {
        _uiHistory.Clear();
        var items = await _cloudScanHistoryService.GetHistoryAsync();
        _uiHistory.AddRange(items);
        RebuildDashboardHistoryRows();
        var usage = await _cloudScanHistoryService.GetHistoryUsageAsync();
        _historyUsageCount = usage.Count;
        _historyUsageLimit = usage.Limit;
    }

    private async Task MaybeOfferLocalTemplateMigrationAsync()
    {
        if (_settings.LocalTemplatesMigratedToCloud)
            return;

        var local = _printTemplateService.LoadTemplates();
        if (local.Count == 0)
            return;

        var cloud = await _userTemplateService.GetTemplatesAsync();
        if (cloud.Count > 0)
        {
            _settings.LocalTemplatesMigratedToCloud = true;
            _settings.Save();
            return;
        }

        var result = MessageBox.Show(
            this,
            "Найдены локальные шаблоны. Перенести их в аккаунт DoubleMark?",
            "Перенос шаблонов",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            var count = await _userTemplateService.MigrateLocalTemplatesAsync(_settings);
            await LoadCloudTemplatesAsync();
            ShowToast($"Перенесено шаблонов: {count}", ToastKind.Success);
        }
    }

    private void ClearUserCloudData()
    {
        _uiHistory.Clear();
        _printTemplates = PrintTemplateService.CreateDefaultTemplates();
        _historyUsageCount = 0;
        _historyUsageLimit = CloudScanHistoryService.MaxScanHistory;
        _settings.DefaultPrintTemplateName = _printTemplates.FirstOrDefault()?.Name;
        RebuildDashboardHistoryRows();
        UpdateHistoryUsageUi();
    }

    private void UpdateHistoryUsageUi()
    {
        var text = $"История: {_historyUsageCount} / {_historyUsageLimit}";
        _historyView?.SetUsage(text, _historyUsageCount, _historyUsageLimit);
    }

}
