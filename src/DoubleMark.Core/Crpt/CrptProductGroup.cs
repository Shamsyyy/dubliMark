namespace DoubleMark.Core.Crpt;

/// <summary>
/// Known CRPT product group identifiers (spec §5, §9.5.3).
/// </summary>
public static class CrptProductGroup
{
    public const string Chemistry = "chemistry";
    public const string Milk = "milk";
    public const string Water = "water";
    public const string SoftDrinks = "softdrinks";
    public const string Beer = "beer";

    public static bool IsKnown(string? productGroup) =>
        CrptProductGroupCatalog.IsKnown(productGroup);

    public static string Normalize(string? productGroup) =>
        string.IsNullOrWhiteSpace(productGroup) ? "" : productGroup.Trim().ToLowerInvariant();
}
