using System.Text.Json;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services.Account;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services.Cloud;

public enum TemplateSyncStatus
{
    NotSignedIn,
    Idle,
    Loading,
    Saving,
    Synced,
    Error
}

public sealed class CloudPrintTemplate
{
    public string? CloudId { get; init; }
    public PrintTemplate Template { get; init; } = new();
    public string? Description { get; init; }
    public string? PrinterName { get; init; }
    public bool IsDefault { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class UserTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly SupabaseClientFactory _clientFactory;
    private readonly Func<AccountUser?> _getCurrentUser;
    private readonly PrintTemplateService _localTemplateService;

    public TemplateSyncStatus Status { get; private set; } = TemplateSyncStatus.Idle;
    public string? StatusMessage { get; private set; }

    public UserTemplateService(
        SupabaseClientFactory clientFactory,
        Func<AccountUser?> getCurrentUser,
        PrintTemplateService localTemplateService)
    {
        _clientFactory = clientFactory;
        _getCurrentUser = getCurrentUser;
        _localTemplateService = localTemplateService;
    }

    public async Task<IReadOnlyList<CloudPrintTemplate>> GetTemplatesAsync()
    {
        var user = _getCurrentUser();
        if (user == null)
        {
            SetStatus(TemplateSyncStatus.NotSignedIn, "Войдите в аккаунт для синхронизации шаблонов");
            return Array.Empty<CloudPrintTemplate>();
        }

        try
        {
            SetStatus(TemplateSyncStatus.Loading, "Загрузка шаблонов...");
            LoggingService.Info("Templates", "Loading user templates userId=" + user.Id);

            var result = await _clientFactory.GetClient()
                .From<UserPrintTemplateRow>()
                .Where(row => row.UserId == user.Id)
                .Get();

            var templates = result.Models
                .Select(ToCloudTemplate)
                .Where(t => t != null)
                .Cast<CloudPrintTemplate>()
                .ToList();

            LoggingService.Info("Templates", "Loaded count=" + templates.Count);
            SetStatus(TemplateSyncStatus.Synced, "Шаблоны синхронизированы");
            return templates;
        }
        catch (Exception ex)
        {
            LoggingService.Error("Templates", "Load failed", ex);
            SetStatus(TemplateSyncStatus.Error, "Не удалось загрузить шаблоны с сервера.");
            return Array.Empty<CloudPrintTemplate>();
        }
    }

    public async Task<IReadOnlyList<PrintTemplate>> EnsureDefaultTemplatesAsync()
    {
        var existing = await GetTemplatesAsync();
        if (existing.Count > 0)
            return existing.Select(t => t.Template).ToList();

        var user = _getCurrentUser();
        if (user == null)
            return _localTemplateService.LoadOrCreateDefaults();

        var defaults = PrintTemplateService.CreateDefaultTemplates();
        foreach (var template in defaults)
            await SaveTemplateAsync(template, isDefault: template.Name == "ЧЗ 30x20 мм");

        return (await GetTemplatesAsync()).Select(t => t.Template).ToList();
    }

    public async Task<CloudPrintTemplate?> SaveTemplateAsync(
        PrintTemplate template,
        string? cloudId = null,
        bool? isDefault = null,
        string? description = null)
    {
        var user = _getCurrentUser();
        if (user == null)
        {
            SetStatus(TemplateSyncStatus.NotSignedIn, "Войдите в аккаунт, чтобы сохранять шаблоны.");
            return null;
        }

        try
        {
            SetStatus(TemplateSyncStatus.Saving, "Сохранение...");
            LoggingService.Info("Templates", "Save started template=" + template.Name);

            var now = DateTime.UtcNow;
            var row = new UserPrintTemplateRow
            {
                Id = string.IsNullOrWhiteSpace(cloudId) ? Guid.NewGuid().ToString() : cloudId,
                UserId = user.Id,
                Name = template.Name,
                Description = description,
                WidthMm = (decimal)template.LabelWidthMm,
                HeightMm = (decimal)template.LabelHeightMm,
                TemplateData = JsonSerializer.Serialize(template, JsonOptions),
                IsDefault = isDefault ?? false,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _clientFactory.GetClient().From<UserPrintTemplateRow>().Upsert(row);

            if (row.IsDefault)
                await SetDefaultTemplateAsync(row.Id);

            LoggingService.Info("Templates", "Save success id=" + row.Id);
            SetStatus(TemplateSyncStatus.Synced, "Шаблоны синхронизированы");
            return ToCloudTemplate(row);
        }
        catch (Exception ex)
        {
            LoggingService.Error("Templates", "Save failed", ex);
            SetStatus(TemplateSyncStatus.Error, "Ошибка синхронизации");
            return null;
        }
    }

    public async Task<bool> SaveAllTemplatesAsync(IReadOnlyList<PrintTemplate> templates, string? defaultTemplateName)
    {
        var user = _getCurrentUser();
        if (user == null)
            return false;

        var existing = await GetTemplatesAsync();
        var saved = new List<CloudPrintTemplate>();

        foreach (var template in templates)
        {
            var match = existing.FirstOrDefault(e =>
                string.Equals(e.Template.Name, template.Name, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(e.Template.LabelWidthMm - template.LabelWidthMm) < 0.01
                && Math.Abs(e.Template.LabelHeightMm - template.LabelHeightMm) < 0.01);

            var isDefault = !string.IsNullOrWhiteSpace(defaultTemplateName)
                            && string.Equals(template.Name, defaultTemplateName, StringComparison.OrdinalIgnoreCase);

            var cloud = await SaveTemplateAsync(template, match?.CloudId, isDefault);
            if (cloud != null)
                saved.Add(cloud);
        }

        var savedIds = saved.Select(s => s.CloudId).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var orphan in existing.Where(e => e.CloudId != null && !savedIds.Contains(e.CloudId)))
            await DeleteTemplateAsync(orphan.CloudId!);

        return saved.Count > 0;
    }

    public async Task<bool> DeleteTemplateAsync(string cloudId)
    {
        var user = _getCurrentUser();
        if (user == null)
            return false;

        try
        {
            await _clientFactory.GetClient()
                .From<UserPrintTemplateRow>()
                .Where(row => row.Id == cloudId && row.UserId == user.Id)
                .Delete();
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Error("Templates", "Delete failed", ex);
            return false;
        }
    }

    public async Task<bool> SetDefaultTemplateAsync(string cloudId)
    {
        var user = _getCurrentUser();
        if (user == null)
            return false;

        try
        {
            var all = await _clientFactory.GetClient()
                .From<UserPrintTemplateRow>()
                .Where(row => row.UserId == user.Id)
                .Get();

            foreach (var row in all.Models)
            {
                row.IsDefault = string.Equals(row.Id, cloudId, StringComparison.OrdinalIgnoreCase);
                row.UpdatedAt = DateTime.UtcNow;
                await _clientFactory.GetClient().From<UserPrintTemplateRow>().Upsert(row);
            }

            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Error("Templates", "Set default failed", ex);
            return false;
        }
    }

    public async Task<int> MigrateLocalTemplatesAsync(AppSettings settings)
    {
        var user = _getCurrentUser();
        if (user == null || settings.LocalTemplatesMigratedToCloud)
            return 0;

        var local = _localTemplateService.LoadTemplates();
        if (local.Count == 0)
        {
            settings.LocalTemplatesMigratedToCloud = true;
            settings.Save();
            return 0;
        }

        var existing = await GetTemplatesAsync();
        var migrated = 0;

        foreach (var template in local)
        {
            if (existing.Any(e =>
                    string.Equals(e.Template.Name, template.Name, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(e.Template.LabelWidthMm - template.LabelWidthMm) < 0.01
                    && Math.Abs(e.Template.LabelHeightMm - template.LabelHeightMm) < 0.01))
                continue;

            var isDefault = string.Equals(template.Name, settings.DefaultPrintTemplateName, StringComparison.OrdinalIgnoreCase);
            if (await SaveTemplateAsync(template, isDefault: isDefault) != null)
                migrated++;
        }

        settings.LocalTemplatesMigratedToCloud = true;
        settings.Save();
        LoggingService.Info("Templates", "Local migration completed count=" + migrated);
        return migrated;
    }

    private void SetStatus(TemplateSyncStatus status, string message)
    {
        Status = status;
        StatusMessage = message;
    }

    private static CloudPrintTemplate? ToCloudTemplate(UserPrintTemplateRow row)
    {
        try
        {
            var json = row.TemplateData switch
            {
                string s => s,
                JsonElement el => el.GetRawText(),
                _ => JsonSerializer.Serialize(row.TemplateData)
            };

            var template = JsonSerializer.Deserialize<PrintTemplate>(json, JsonOptions);
            if (template == null || string.IsNullOrWhiteSpace(template.Name))
                return null;

            return new CloudPrintTemplate
            {
                CloudId = row.Id,
                Template = template,
                Description = row.Description,
                PrinterName = row.PrinterName,
                IsDefault = row.IsDefault,
                UpdatedAt = row.UpdatedAt ?? row.CreatedAt
            };
        }
        catch
        {
            return null;
        }
    }
}
