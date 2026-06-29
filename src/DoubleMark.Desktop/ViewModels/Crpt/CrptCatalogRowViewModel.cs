using DoubleMark.Core.Crpt;

namespace DoubleMark.Desktop.ViewModels.Crpt;

public sealed class CrptCatalogRowViewModel
{
    public CrptCatalogRowViewModel(CrptProductCatalogItem item) => Item = item;

    public CrptProductCatalogItem Item { get; }

    public string Gtin => Item.Gtin;

    public string Name => Item.Name;

    public string? TnvedCode => Item.TnvedCode;

    public string? ProductGroup => Item.ProductGroup;

    public string ProductGroupDisplay => CrptProductGroupCatalog.GetDisplayName(Item.ProductGroup);

    public string? CategoryName => CrptNkCategoryDiscovery.NormalizeCategoryName(Item.CategoryName);

    public string NkStatus => Item.NkStatus;

    public bool IsSigned => Item.IsSigned;

    public bool CanOrderCodes => Item.CanOrderCodes;

    public string ProductStateDisplay => CrptCatalogDisplayLabels.FormatProductState(Item.NkProductState);

    public string CardStatusDisplay => CrptCatalogDisplayLabels.FormatCardStatus(Item.NkCardStatusPrimary);

    public string CardTypeDisplay => CrptCatalogDisplayLabels.FormatCardType(Item.NkCardType);

    public string UpdatedAtDisplay => CrptCatalogDisplayLabels.FormatUpdatedAt(Item.NkUpdatedAt);

    public DateTimeOffset UpdatedAtSortKey => Item.NkUpdatedAt ?? DateTimeOffset.MinValue;
}
