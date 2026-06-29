using DoubleMark.Core.Crpt;

namespace DoubleMark.Desktop.ViewModels.Crpt;

public static class CrptCatalogDisplayLabels
{
    public static string FormatProductState(NkProductState state) =>
        state switch
        {
            NkProductState.Published => "Опубликован",
            NkProductState.Draft => "Черновик",
            NkProductState.Moderation => "На модерации",
            NkProductState.Errors => "Ошибки",
            NkProductState.Archived => "Архив",
            _ => "—",
        };

    public static string FormatCardStatus(string? cardStatusPrimary)
    {
        if (string.IsNullOrWhiteSpace(cardStatusPrimary))
            return "—";

        return cardStatusPrimary.Trim().ToLowerInvariant() switch
        {
            "published" => "Опубликована",
            "notsigned" => "Не подписана",
            "draft" => "Черновик",
            "moderation" => "На модерации",
            "errors" => "С ошибками",
            _ => cardStatusPrimary,
        };
    }

    public static string FormatCardType(NkCardType cardType) =>
        cardType switch
        {
            NkCardType.TradeUnit => "Единица товара",
            NkCardType.Set => "Набор",
            NkCardType.Kit => "Комплект",
            _ => "—",
        };

    public static string FormatUpdatedAt(DateTimeOffset? updatedAt) =>
        updatedAt is null ? "—" : updatedAt.Value.ToLocalTime().ToString("g");

    public static string FormatProductStateFilter(CrptCatalogProductStateFilter filter) =>
        filter switch
        {
            CrptCatalogProductStateFilter.All => "Все",
            CrptCatalogProductStateFilter.Published => "Опубликован",
            CrptCatalogProductStateFilter.Draft => "Черновик",
            CrptCatalogProductStateFilter.Moderation => "На модерации",
            CrptCatalogProductStateFilter.Errors => "Ошибки",
            CrptCatalogProductStateFilter.Archived => "Архив",
            _ => filter.ToString(),
        };

    public static string FormatCardStatusFilter(CrptCatalogCardStatusFilter filter) =>
        filter switch
        {
            CrptCatalogCardStatusFilter.All => "Все",
            CrptCatalogCardStatusFilter.Published => "Опубликована",
            CrptCatalogCardStatusFilter.NotSigned => "Не подписана",
            CrptCatalogCardStatusFilter.Draft => "Черновик",
            CrptCatalogCardStatusFilter.Moderation => "На модерации",
            CrptCatalogCardStatusFilter.Errors => "С ошибками",
            _ => filter.ToString(),
        };

    public static string FormatCardTypeFilter(CrptCatalogCardTypeFilter filter) =>
        filter switch
        {
            CrptCatalogCardTypeFilter.All => "Все",
            CrptCatalogCardTypeFilter.TradeUnit => "Единица товара",
            CrptCatalogCardTypeFilter.Set => "Набор",
            CrptCatalogCardTypeFilter.Kit => "Комплект",
            _ => filter.ToString(),
        };

    public static string FormatLegacyFilter(CrptCatalogFilter filter) =>
        filter switch
        {
            CrptCatalogFilter.All => "Все",
            CrptCatalogFilter.OrderableOnly => "Только для заказа",
            CrptCatalogFilter.WithSyncErrors => "С ошибками синхронизации",
            _ => filter.ToString(),
        };

    public static string FormatProductGroupFilter(string? productGroupCode) =>
        string.IsNullOrWhiteSpace(productGroupCode)
            ? "Все"
            : CrptProductGroupCatalog.GetDisplayName(productGroupCode);

    public static string? ResolveOrderCodesTooltip(CrptCatalogRowViewModel? row)
    {
        if (row is null)
            return null;

        if (row.CanOrderCodes)
            return "Заказать коды маркировки в СУЗ для выбранного GTIN.";

        return row.Item.NkCardType switch
        {
            NkCardType.Set => "Заказ кодов недоступен для наборов.",
            NkCardType.Kit => "Заказ кодов недоступен для комплектов.",
            _ when row.Item.NkProductState != NkProductState.Published =>
                "Заказ доступен только для опубликованных карточек единицы товара.",
            _ when !row.IsSigned => "Карточка должна быть подписана.",
            _ => "Заказ кодов недоступен: укажите товарную группу или проверьте статус карточки.",
        };
    }
}
