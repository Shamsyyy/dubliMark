using System.Text.RegularExpressions;

namespace DoubleMark.Core.Crpt;

/// <summary>
/// NK catalog category discovery and UI visibility filtering (Phase C3).
/// </summary>
public static class CrptNkCategoryDiscovery
{
    private static readonly Regex CategoryTnvedPrefixPattern = new(@"^\d{10}\s+", RegexOptions.Compiled);

    /// <summary>
    /// Removes optional leading TN VED code (10 digits + space) from NK category display names.
    /// Does not alter strings that consist only of a 10-digit TN VED code.
    /// </summary>
    public static string? NormalizeCategoryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        var trimmed = name.Trim();
        if (trimmed.Length == 10 && trimmed.All(char.IsDigit))
            return trimmed;

        var stripped = CategoryTnvedPrefixPattern.Replace(trimmed, "", 1);
        return string.IsNullOrWhiteSpace(stripped) ? trimmed : stripped;
    }

    /// <summary>
    /// Merges auto-discovered category names into the known list.
    /// Result is sorted ordinally ignore case with case-insensitive uniqueness.
    /// Existing known casing is preserved when names differ only by case.
    /// </summary>
    public static List<string> MergeKnownCategories(
        IEnumerable<string>? existingKnown,
        IEnumerable<string?>? discoveredNames)
    {
        var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (existingKnown is not null)
        {
            foreach (var name in existingKnown)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var trimmed = NormalizeCategoryName(name.Trim());
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                byKey.TryAdd(trimmed, trimmed);
            }
        }

        if (discoveredNames is not null)
        {
            foreach (var name in discoveredNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var trimmed = NormalizeCategoryName(name.Trim());
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                byKey.TryAdd(trimmed, trimmed);
            }
        }

        return byKey.Values
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IEnumerable<string?> CollectCategoryNames(IEnumerable<CrptProductCatalogItem> items) =>
        items.Select(item => NormalizeCategoryName(item.CategoryName));

    /// <summary>
    /// When <paramref name="visibleCategories"/> is empty, all items are returned.
    /// Otherwise only items with a matching <see cref="CrptProductCatalogItem.CategoryName"/> are kept.
    /// </summary>
    public static IEnumerable<CrptProductCatalogItem> FilterByVisibleCategories(
        IEnumerable<CrptProductCatalogItem> items,
        IReadOnlyList<string>? visibleCategories)
    {
        if (visibleCategories is null || visibleCategories.Count == 0)
            return items;

        var allowed = new HashSet<string>(
            visibleCategories
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Select(category => NormalizeCategoryName(category.Trim())!)
                .Where(category => !string.IsNullOrWhiteSpace(category)),
            StringComparer.OrdinalIgnoreCase);

        if (allowed.Count == 0)
            return items;

        return items.Where(item =>
        {
            var categoryName = NormalizeCategoryName(item.CategoryName);
            return !string.IsNullOrWhiteSpace(categoryName) &&
                   allowed.Contains(categoryName);
        });
    }
}
