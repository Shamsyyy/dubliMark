using DoubleMark.Core.Crpt;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// Local NK product catalog persistence (spec §4.1, §6.2).
/// </summary>
public interface ICrptProductCatalogStore
{
    string CatalogPath { get; }

    IReadOnlyList<CrptProductCatalogItem> Load();
    void Save(IReadOnlyList<CrptProductCatalogItem> items);
    IReadOnlyList<CrptProductCatalogItem> List();
    IReadOnlyList<CrptProductCatalogItem> Filter(Func<CrptProductCatalogItem, bool> predicate);
    IReadOnlyList<CrptProductCatalogItem> GetOrderableItems();
}
