using DoubleMark.Core.Print;using DoubleMark.Desktop.Services.Cloud;
using DoubleMark.Desktop.Settings;
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
        InitializeHistoryServices();
    }

    private async Task LoadUserCloudDataAsync()
    {
        if (_accountSnapshot.User == null)
        {
            ClearUserCloudData();
            await ReloadScanHistoryAsync();
            return;
        }

        await LoadCloudTemplatesAsync();
        await MaybeOfferLocalTemplateMigrationAsync();
        await ReloadScanHistoryAsync();
    }

    private async Task LoadCloudTemplatesAsync()
    {
        var templates = await _userTemplateService.EnsureDefaultTemplatesAsync();
        if (templates.Count > 0)
        {
            _printTemplates = templates
                .Select(TemplateLayoutHelper.ClampDataMatrixInLabel)
                .ToList();
            var cloud = await _userTemplateService.GetTemplatesAsync();
            var defaultCloud = cloud.FirstOrDefault(t => t.IsDefault) ?? cloud.FirstOrDefault();
            if (defaultCloud != null)
                _settings.DefaultPrintTemplateName = defaultCloud.Template.Name;
        }
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

        var confirmed = ConfirmDialogWindow.Show(
            this,
            "Перенос шаблонов",
            "Найдены локальные шаблоны. Перенести их в аккаунт DoubleMark?",
            confirmText: "Перенести",
            cancelText: "Не сейчас");

        if (confirmed)
        {
            var count = await _userTemplateService.MigrateLocalTemplatesAsync(_settings);
            await LoadCloudTemplatesAsync();
            ShowToast($"Перенесено шаблонов: {count}", ToastKind.Success);
        }
    }

    private void ClearUserCloudData()
    {
        _printTemplates = PrintTemplateService.CreateDefaultTemplates();
        _settings.DefaultPrintTemplateName = _printTemplates.FirstOrDefault()?.Name;
        if (_settings.HistoryViewMode == HistoryViewMode.Cloud)
        {
            _historyUsageCount = 0;
            _historyUsageLimit = CloudScanHistoryService.MaxScanHistory;
        }
    }

    private void UpdateHistoryUsageUi()
    {
        var text = _settings.HistoryViewMode == HistoryViewMode.Cloud
            ? $"Облако: {_historyUsageCount} / {_historyUsageLimit}"
            : $"Локально: {_historyUsageCount} записей";
        var showLimitWarning = _settings.HistoryViewMode == HistoryViewMode.Cloud;
        _historyView?.SetUsage(text, _historyUsageCount, _historyUsageLimit, showLimitWarning);
    }
}
