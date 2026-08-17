using System.IO;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;

namespace DoubleMark.Desktop.Settings;

/// <summary>
/// Non-secret CRPT integration settings (persisted in crpt-settings.json).
/// Secrets live in <see cref="CrptSecrets"/> via <see cref="CrptSettingsStore"/>.
/// </summary>
public sealed class CrptSettings
{
    public CrptEnvironment Environment { get; set; } = CrptEnvironment.Sandbox;
    public List<CrptOrganizationRole> Roles { get; set; } = [CrptOrganizationRole.Manufacturer];
    public List<string> ProductGroups { get; set; } = [];

    public string Inn { get; set; } = "";
    public string? Gs1OrganizationNumber { get; set; }

    public string SuzBaseUrl { get; set; } = DefaultSuzBaseUrl;
    public string TrueApiBaseUrl { get; set; } = DefaultTrueApiBaseUrl;

    public bool AutoRefreshToken { get; set; } = true;
    public string? ContactPerson { get; set; }

    public string NkBaseUrl { get; set; } = DefaultNkBaseUrl;
    public int NkHttpTimeoutSeconds { get; set; } = CrptRiskMitigations.NkHttpTimeoutSeconds;
    public bool NkUseJwtFromTrueApi { get; set; } = true;

    [Obsolete("Sync stores all NK cards; filter by state/status in catalog UI instead.")]
    public bool NkSyncOnlyPublished { get; set; }

    [Obsolete("Sync stores all NK cards; filter by state/status in catalog UI instead.")]
    public bool NkSyncOnlySigned { get; set; }

    /// <summary>Auto-discovered NK catalog categories (updated on sync).</summary>
    public List<string> NkKnownCategories { get; set; } = [];

    /// <summary>User-selected categories visible in catalog UI. Empty — show all.</summary>
    public List<string> NkVisibleCategories { get; set; } = [];

    /// <summary>Use <c>GET /v3/etagslist</c> when local catalog exists (Phase C7.1).</summary>
    public bool NkIncrementalSyncEnabled { get; set; } = true;

    /// <summary>Persisted catalog UI filter presets (Phase C7.2).</summary>
    public CrptCatalogUiPresets CatalogUi { get; set; } = new();

    /// <summary>templateId by productGroup, e.g. { "chemistry": 46 }.</summary>
    public Dictionary<string, int> ProductGroupTemplateDefaults { get; set; } = [];

    /// <summary>
    /// Local product catalog path (§6.2). Empty — default crpt-catalog.json under settings directory.
    /// Catalog sync is implemented in a later phase.
    /// </summary>
    public string? ProductCatalogPath { get; set; }

    public const string DefaultSuzBaseUrl = "https://suzgrid.crpt.ru/";
    public const string DefaultTrueApiBaseUrl = "https://markirovka.crpt.ru/";
    public const string DefaultNkBaseUrl = CrptUrl.ProductionNkBaseUrl;
    public const string DefaultProductCatalogFileName = "crpt-catalog.json";

    public string EffectiveProductCatalogPath =>
        string.IsNullOrWhiteSpace(ProductCatalogPath)
            ? Path.Combine(AppSettings.SettingsDirectory, DefaultProductCatalogFileName)
            : ProductCatalogPath;

    public string PrimaryProductGroup =>
        ProductGroups.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g)) ?? "chemistry";

    public int? ResolveTemplateId(string? productGroup = null) =>
        CrptRiskMitigations.ResolveTemplateId(productGroup ?? PrimaryProductGroup, ProductGroupTemplateDefaults);
}
