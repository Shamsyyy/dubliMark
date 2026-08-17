using System.Collections.ObjectModel;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.Settings;
using DoubleMark.Desktop.ViewModels;

namespace DoubleMark.Desktop.ViewModels.Crpt;

public sealed class CrptSettingsViewModel : ViewModelBase
{
    private readonly ICrptSettingsStore _settingsStore;
    private readonly ICrptAuthService _authService;
    private readonly ICrptCertificateProvider _certificateProvider;
    private readonly CrptAuthRuntimeState _runtimeState;

    private CrptEnvironment _environment = CrptEnvironment.Sandbox;
    private string _inn = "";
    private string? _gs1OrganizationNumber;
    private string _suzBaseUrl = CrptSettings.DefaultSuzBaseUrl;
    private string _trueApiBaseUrl = CrptSettings.DefaultTrueApiBaseUrl;
    private string _nkBaseUrl = CrptSettings.DefaultNkBaseUrl;
    private string? _omsId;
    private string? _connectionId;
    private string? _contactPerson;
    private string? _certificateThumbprint;
    private string? _nkApiKey;
    private bool _autoRefreshToken = true;
    private bool _nkUseJwtFromTrueApi = true;
    private bool _nkSyncOnlyPublished;
    private bool _nkSyncOnlySigned;
    private List<string> _nkKnownCategories = [];
    private string _statusMessage = "";
    private bool _isBusy;
    private string? _productionConnectivityWarning;
    private CrptCertificateDescriptor? _selectedCertificate;

    public CrptSettingsViewModel(
        ICrptSettingsStore settingsStore,
        ICrptAuthService authService,
        ICrptCertificateProvider certificateProvider,
        CrptAuthRuntimeState runtimeState)
    {
        _settingsStore = settingsStore;
        _authService = authService;
        _certificateProvider = certificateProvider;
        _runtimeState = runtimeState;

        SaveCommand = new AsyncRelayCommand(() => SaveAsync(), () => !IsBusy);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsBusy);
        RefreshTokenCommand = new AsyncRelayCommand(RefreshTokenAsync, () => !IsBusy);
        ReloadCertificatesCommand = new RelayCommand(ReloadCertificates);
        ShowAllCategoriesCommand = new RelayCommand(ShowAllCategories);
        ClearAllCategoriesCommand = new RelayCommand(ClearAllCategories);

        foreach (CrptOrganizationRole role in Enum.GetValues(typeof(CrptOrganizationRole)))
            RoleSelections.Add(new CrptRoleSelectionViewModel(role, role == CrptOrganizationRole.Manufacturer));

        foreach (var group in KnownProductGroups)
            ProductGroupSelections.Add(new CrptProductGroupSelectionViewModel(group, group == CrptProductGroup.Chemistry));
    }

    public ObservableCollection<CrptRoleSelectionViewModel> RoleSelections { get; } = [];
    public ObservableCollection<CrptProductGroupSelectionViewModel> ProductGroupSelections { get; } = [];
    public ObservableCollection<CrptCategorySelectionViewModel> CategorySelections { get; } = [];
    public ObservableCollection<CrptCertificateDescriptor> Certificates { get; } = [];
    public ObservableCollection<CrptTemplateDefaultViewModel> TemplateDefaults { get; } = [];

    public static IReadOnlyList<string> KnownProductGroups { get; } =
    [
        CrptProductGroup.Chemistry,
        CrptProductGroup.Milk,
        CrptProductGroup.Water,
        CrptProductGroup.SoftDrinks,
        CrptProductGroup.Beer,
    ];

    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand TestConnectionCommand { get; }
    public AsyncRelayCommand RefreshTokenCommand { get; }
    public RelayCommand ReloadCertificatesCommand { get; }
    public RelayCommand ShowAllCategoriesCommand { get; }
    public RelayCommand ClearAllCategoriesCommand { get; }

    public ObservableCollection<CrptEnvironmentOption> EnvironmentOptions { get; } =
        new(CrptEnvironmentDisplay.All);

    public Array LegacyEnvironmentOptions { get; } = Enum.GetValues(typeof(CrptEnvironment));

    public CrptEnvironment Environment
    {
        get => _environment;
        set
        {
            if (!SetProperty(ref _environment, value))
                return;

            var settings = new CrptSettings();
            CrptEnvironmentDefaults.Apply(settings, value);
            SuzBaseUrl = settings.SuzBaseUrl;
            TrueApiBaseUrl = settings.TrueApiBaseUrl;
            NkBaseUrl = settings.NkBaseUrl;
            OnPropertyChanged(nameof(SelectedEnvironmentOption));
            OnPropertyChanged(nameof(SelectedEnvironmentDescription));
            OnPropertyChanged(nameof(EnvironmentWhenToUseHint));
            _ = RefreshProductionConnectivityWarningAsync();
        }
    }

    public CrptEnvironmentOption? SelectedEnvironmentOption
    {
        get => EnvironmentOptions.FirstOrDefault(o => o.Value == Environment);
        set
        {
            if (value is null || value.Value == Environment)
                return;

            Environment = value.Value;
        }
    }

    public string SelectedEnvironmentDescription =>
        SelectedEnvironmentOption?.Description ?? "";

    public string EnvironmentWhenToUseHint => CrptEnvironmentDisplay.WhenToUseHint;

    public string? ProductionConnectivityWarning
    {
        get => _productionConnectivityWarning;
        private set
        {
            if (!SetProperty(ref _productionConnectivityWarning, value))
                return;

            OnPropertyChanged(nameof(ShowProductionConnectivityWarning));
        }
    }

    public bool ShowProductionConnectivityWarning =>
        Environment == CrptEnvironment.Production &&
        !string.IsNullOrWhiteSpace(ProductionConnectivityWarning);

    public string Inn
    {
        get => _inn;
        set => SetProperty(ref _inn, value);
    }

    public string? Gs1OrganizationNumber
    {
        get => _gs1OrganizationNumber;
        set => SetProperty(ref _gs1OrganizationNumber, value);
    }

    public string SuzBaseUrl
    {
        get => _suzBaseUrl;
        set => SetProperty(ref _suzBaseUrl, value);
    }

    public string TrueApiBaseUrl
    {
        get => _trueApiBaseUrl;
        set => SetProperty(ref _trueApiBaseUrl, value);
    }

    public string NkBaseUrl
    {
        get => _nkBaseUrl;
        set
        {
            if (!SetProperty(ref _nkBaseUrl, value))
                return;

            if (Environment == CrptEnvironment.Production)
                _ = RefreshProductionConnectivityWarningAsync();
        }
    }

    public string? OmsId
    {
        get => _omsId;
        set => SetProperty(ref _omsId, value);
    }

    public string? ConnectionId
    {
        get => _connectionId;
        set => SetProperty(ref _connectionId, value);
    }

    public string? ContactPerson
    {
        get => _contactPerson;
        set => SetProperty(ref _contactPerson, value);
    }

    public string? CertificateThumbprint
    {
        get => _certificateThumbprint;
        set
        {
            if (!SetProperty(ref _certificateThumbprint, value))
                return;

            SelectedCertificate = Certificates.FirstOrDefault(c =>
                c.Thumbprint.Equals(value, StringComparison.OrdinalIgnoreCase));
        }
    }

    public CrptCertificateDescriptor? SelectedCertificate
    {
        get => _selectedCertificate;
        set
        {
            if (!SetProperty(ref _selectedCertificate, value))
                return;

            CertificateThumbprint = value?.Thumbprint;
            OnPropertyChanged(nameof(CertificateExpiryText));
        }
    }

    public string? NkApiKey
    {
        get => _nkApiKey;
        set => SetProperty(ref _nkApiKey, value);
    }

    public bool AutoRefreshToken
    {
        get => _autoRefreshToken;
        set => SetProperty(ref _autoRefreshToken, value);
    }

    public bool NkUseJwtFromTrueApi
    {
        get => _nkUseJwtFromTrueApi;
        set => SetProperty(ref _nkUseJwtFromTrueApi, value);
    }

    public bool NkSyncOnlyPublished
    {
        get => _nkSyncOnlyPublished;
        set => SetProperty(ref _nkSyncOnlyPublished, value);
    }

    public bool NkSyncOnlySigned
    {
        get => _nkSyncOnlySigned;
        set => SetProperty(ref _nkSyncOnlySigned, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            SaveCommand.RaiseCanExecuteChanged();
            TestConnectionCommand.RaiseCanExecuteChanged();
            RefreshTokenCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string TokenExpiresText =>
        DisplayTokenExpiresAt(_runtimeState.TokenExpiresAt ?? _authService.TokenExpiresAt);

    public string CertificateExpiryText =>
        SelectedCertificate is null
            ? "—"
            : SelectedCertificate.NotAfter.ToLocalTime().ToString("g");

    public void Load()
    {
        var snapshot = _settingsStore.LoadMerged();
        var settings = snapshot.Settings;
        var secrets = snapshot.Secrets;

        Environment = settings.Environment;
        Inn = settings.Inn;
        Gs1OrganizationNumber = settings.Gs1OrganizationNumber;
        SuzBaseUrl = settings.SuzBaseUrl;
        TrueApiBaseUrl = settings.TrueApiBaseUrl;
        NkBaseUrl = settings.NkBaseUrl;
        OmsId = secrets.OmsId;
        ConnectionId = secrets.ConnectionId;
        ContactPerson = settings.ContactPerson;
        CertificateThumbprint = secrets.CertificateThumbprint;
        NkApiKey = secrets.NkApiKey;
        AutoRefreshToken = settings.AutoRefreshToken;
        NkUseJwtFromTrueApi = settings.NkUseJwtFromTrueApi;
        NkSyncOnlyPublished = settings.NkSyncOnlyPublished;
        NkSyncOnlySigned = settings.NkSyncOnlySigned;
        _nkKnownCategories = settings.NkKnownCategories.ToList();
        RebuildCategorySelections(settings.NkVisibleCategories);

        foreach (var role in RoleSelections)
            role.IsSelected = settings.Roles.Contains(role.Role);

        foreach (var group in ProductGroupSelections)
            group.IsSelected = settings.ProductGroups.Contains(group.ProductGroup, StringComparer.OrdinalIgnoreCase);

        TemplateDefaults.Clear();
        foreach (var group in settings.ProductGroups.Where(g => !string.IsNullOrWhiteSpace(g)))
        {
            settings.ProductGroupTemplateDefaults.TryGetValue(group, out var templateId);
            TemplateDefaults.Add(new CrptTemplateDefaultViewModel(group, templateId));
        }

        ReloadCertificates();
        RefreshTokenDisplay();
        StatusMessage = "";
        OnPropertyChanged(nameof(SelectedEnvironmentOption));
        OnPropertyChanged(nameof(SelectedEnvironmentDescription));
        _ = RefreshProductionConnectivityWarningAsync();
    }

    public void RefreshTokenDisplay() => OnPropertyChanged(nameof(TokenExpiresText));

    public static string DisplayTokenExpiresAt(DateTimeOffset? expiresAt) =>
        expiresAt is null || !CrptAuthResponseParser.IsPlausibleTokenExpiry(expiresAt.Value)
            ? "Токен не получен"
            : $"Токен действует до {expiresAt.Value.ToLocalTime():g}";

    private async Task RefreshProductionConnectivityWarningAsync()
    {
        if (Environment != CrptEnvironment.Production)
        {
            ProductionConnectivityWarning = null;
            return;
        }

        var (success, error) = await CrptNkConnectivity.TryCheckReachableAsync(NkBaseUrl.Trim());
        ProductionConnectivityWarning = success
            ? null
            : error ?? "Промышленный контур НК недоступен с этой сети. Проверьте VPN или доступ к серверам ЦРПТ.";
    }

    private void ReloadCertificates()
    {
        Certificates.Clear();
        foreach (var certificate in _certificateProvider.ListEligibleCertificates(
                     string.IsNullOrWhiteSpace(Inn) ? null : Inn.Trim()))
            Certificates.Add(certificate);

        SelectedCertificate = Certificates.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(CertificateThumbprint) &&
            c.Thumbprint.Equals(CertificateThumbprint, StringComparison.OrdinalIgnoreCase))
            ?? Certificates.FirstOrDefault();
    }

    public async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            var settings = BuildSettings();
            var secrets = BuildSecrets();
            _settingsStore.Save(settings, secrets);
            StatusMessage = "Настройки сохранены.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        await Task.CompletedTask;
    }

    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        try
        {
            _settingsStore.Save(BuildSettings(), BuildSecrets());
            await _authService.GetValidTokenAsync();
            _runtimeState.TokenExpiresAt = _authService.TokenExpiresAt;
            RefreshTokenDisplay();
            StatusMessage = "Подключение успешно.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Ошибка подключения: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshTokenAsync()
    {
        IsBusy = true;
        try
        {
            _settingsStore.Save(BuildSettings(), BuildSecrets());
            await _authService.RefreshTokenAsync();
            _runtimeState.TokenExpiresAt = _authService.TokenExpiresAt;
            RefreshTokenDisplay();
            StatusMessage = "Токен продлён.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Не удалось продлить токен: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal CrptSettings BuildSettings()
    {
        var selectedRoles = RoleSelections.Where(r => r.IsSelected).Select(r => r.Role).ToList();
        if (selectedRoles.Count == 0)
            selectedRoles.Add(CrptOrganizationRole.Manufacturer);

        var selectedGroups = ProductGroupSelections
            .Where(g => g.IsSelected)
            .Select(g => g.ProductGroup)
            .ToList();

        var templateDefaults = TemplateDefaults
            .Where(t => t.TemplateId.HasValue)
            .ToDictionary(t => t.ProductGroup, t => t.TemplateId!.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var group in selectedGroups)
        {
            if (!templateDefaults.ContainsKey(group) &&
                TemplateDefaults.FirstOrDefault(t =>
                    t.ProductGroup.Equals(group, StringComparison.OrdinalIgnoreCase)) is { TemplateId: { } id })
                templateDefaults[group] = id;
        }

        return new CrptSettings
        {
            Environment = Environment,
            Roles = selectedRoles,
            ProductGroups = selectedGroups,
            Inn = Inn.Trim(),
            Gs1OrganizationNumber = string.IsNullOrWhiteSpace(Gs1OrganizationNumber)
                ? null
                : Gs1OrganizationNumber.Trim(),
            SuzBaseUrl = SuzBaseUrl.Trim(),
            TrueApiBaseUrl = TrueApiBaseUrl.Trim(),
            NkBaseUrl = NkBaseUrl.Trim(),
            AutoRefreshToken = AutoRefreshToken,
            ContactPerson = string.IsNullOrWhiteSpace(ContactPerson) ? null : ContactPerson.Trim(),
            NkUseJwtFromTrueApi = NkUseJwtFromTrueApi,
            NkSyncOnlyPublished = NkSyncOnlyPublished,
            NkSyncOnlySigned = NkSyncOnlySigned,
            NkKnownCategories = _nkKnownCategories.ToList(),
            NkVisibleCategories = BuildVisibleCategories(),
            ProductGroupTemplateDefaults = templateDefaults,
        };
    }

    internal List<string> BuildVisibleCategories()
    {
        if (CategorySelections.Count == 0)
            return [];

        var selected = CategorySelections
            .Where(category => category.IsSelected)
            .Select(category => category.CategoryName)
            .ToList();

        return selected.Count == CategorySelections.Count ? [] : selected;
    }

    private void RebuildCategorySelections(IReadOnlyList<string> visibleCategories)
    {
        CategorySelections.Clear();
        var showAll = visibleCategories.Count == 0;
        var visibleSet = new HashSet<string>(visibleCategories, StringComparer.OrdinalIgnoreCase);

        foreach (var category in _nkKnownCategories.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            var isSelected = showAll || visibleSet.Contains(category);
            CategorySelections.Add(new CrptCategorySelectionViewModel(category, isSelected));
        }
    }

    private void ShowAllCategories()
    {
        foreach (var category in CategorySelections)
            category.IsSelected = true;
    }

    private void ClearAllCategories()
    {
        foreach (var category in CategorySelections)
            category.IsSelected = false;
    }

    internal CrptSecrets BuildSecrets() => new()
    {
        OmsId = string.IsNullOrWhiteSpace(OmsId) ? null : OmsId.Trim(),
        ConnectionId = string.IsNullOrWhiteSpace(ConnectionId) ? null : ConnectionId.Trim(),
        CertificateThumbprint = ResolveCertificateThumbprint(),
        NkApiKey = string.IsNullOrWhiteSpace(NkApiKey) ? null : NkApiKey.Trim(),
    };

    private string? ResolveCertificateThumbprint()
    {
        var thumbprint = string.IsNullOrWhiteSpace(CertificateThumbprint)
            ? SelectedCertificate?.Thumbprint
            : CertificateThumbprint.Trim();
        return string.IsNullOrWhiteSpace(thumbprint) ? null : thumbprint;
    }

    public void SyncTemplateDefaultsFromProductGroups()
    {
        var selected = ProductGroupSelections.Where(g => g.IsSelected).Select(g => g.ProductGroup).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = TemplateDefaults.Count - 1; index >= 0; index--)
        {
            if (!selected.Contains(TemplateDefaults[index].ProductGroup))
                TemplateDefaults.RemoveAt(index);
        }

        foreach (var group in selected)
        {
            if (TemplateDefaults.Any(t => t.ProductGroup.Equals(group, StringComparison.OrdinalIgnoreCase)))
                continue;

            TemplateDefaults.Add(new CrptTemplateDefaultViewModel(group, null));
        }
    }
}

public sealed class CrptRoleSelectionViewModel : ViewModelBase
{
    private bool _isSelected;

    public CrptRoleSelectionViewModel(CrptOrganizationRole role, bool isSelected)
    {
        Role = role;
        _isSelected = isSelected;
    }

    public CrptOrganizationRole Role { get; }

    public string DisplayName => Role switch
    {
        CrptOrganizationRole.Manufacturer => "Производитель",
        CrptOrganizationRole.Importer => "Импортёр",
        CrptOrganizationRole.Wholesaler => "Оптовик",
        CrptOrganizationRole.Retailer => "Розница",
        CrptOrganizationRole.Seller => "Продавец",
        CrptOrganizationRole.Exporter => "Экспортёр",
        CrptOrganizationRole.Government => "Госорган",
        CrptOrganizationRole.HoReCa => "HoReCa",
        _ => Role.ToString(),
    };

    public bool IsEnabled => CrptMvpScope.GetRoleScope(Role) != CrptRoleScope.OutOfScope;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class CrptProductGroupSelectionViewModel : ViewModelBase
{
    private bool _isSelected;

    public CrptProductGroupSelectionViewModel(string productGroup, bool isSelected)
    {
        ProductGroup = productGroup;
        _isSelected = isSelected;
    }

    public string ProductGroup { get; }

    public string DisplayName => CrptProductGroupCatalog.GetDisplayName(ProductGroup);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class CrptTemplateDefaultViewModel : ViewModelBase
{
    private int? _templateId;

    public CrptTemplateDefaultViewModel(string productGroup, int? templateId)
    {
        ProductGroup = productGroup;
        _templateId = templateId;
    }

    public string ProductGroup { get; }

    public string ProductGroupDisplay => CrptProductGroupCatalog.GetDisplayName(ProductGroup);

    public int? TemplateId
    {
        get => _templateId;
        set => SetProperty(ref _templateId, value);
    }
}

public sealed class CrptCategorySelectionViewModel : ViewModelBase
{
    private bool _isSelected;

    public CrptCategorySelectionViewModel(string categoryName, bool isSelected)
    {
        CategoryName = categoryName;
        _isSelected = isSelected;
    }

    public string CategoryName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
