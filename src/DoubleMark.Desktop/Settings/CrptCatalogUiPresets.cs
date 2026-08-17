using DoubleMark.Desktop.ViewModels.Crpt;

namespace DoubleMark.Desktop.Settings;

/// <summary>
/// Persisted catalog UI filter state (Phase C7.2).
/// </summary>
public sealed class CrptCatalogUiPresets
{
    public string GtinFilter { get; set; } = "";
    public string NameFilter { get; set; } = "";
    public string TnvedFilter { get; set; } = "";
    public string CategoryFilter { get; set; } = "";
    public string ProductGroupFilter { get; set; } = "";
    public CrptCatalogProductStateFilter ProductStateFilter { get; set; } = CrptCatalogProductStateFilter.All;
    public CrptCatalogCardStatusFilter CardStatusFilter { get; set; } = CrptCatalogCardStatusFilter.All;
    public CrptCatalogCardTypeFilter CardTypeFilter { get; set; } = CrptCatalogCardTypeFilter.All;
    public CrptCatalogFilter Filter { get; set; } = CrptCatalogFilter.All;
}
