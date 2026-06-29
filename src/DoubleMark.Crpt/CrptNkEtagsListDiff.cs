using DoubleMark.Core.Crpt;

namespace DoubleMark.Crpt;

/// <summary>
/// Compares remote NK etags with local catalog for incremental sync (Phase C7.1).
/// </summary>
public static class CrptNkEtagsListDiff
{
    public sealed record EtagsListEntry(int GoodId, string Etag);

    public sealed record EtagsListPage(
        IReadOnlyList<EtagsListEntry> Entries,
        int GoodsCount,
        int Offset,
        int? LastProductNumber);

    /// <summary>
    /// Returns good_ids from the local catalog whose remote etag differs or is not yet stored locally.
    /// Remote entries for unknown good_ids are ignored — new NK cards are discovered via product-list sync.
    /// </summary>
    public static IReadOnlyList<int> FindChangedGoodIds(
        IReadOnlyList<EtagsListEntry> remoteEntries,
        IReadOnlyDictionary<int, CrptProductCatalogItem> existingByGoodId)
    {
        var remoteByGoodId = BuildRemoteEtagMap(remoteEntries);
        var changed = new List<int>();

        foreach (var (goodId, local) in existingByGoodId)
        {
            if (!remoteByGoodId.TryGetValue(goodId, out var remoteEtag))
            {
                changed.Add(goodId);
                continue;
            }

            if (!string.Equals(local.NkEtag, remoteEtag, StringComparison.Ordinal))
                changed.Add(goodId);
        }

        return changed;
    }

    public static Dictionary<int, string> BuildRemoteEtagMap(IEnumerable<EtagsListEntry> entries)
    {
        var map = new Dictionary<int, string>();
        foreach (var entry in entries)
            map[entry.GoodId] = entry.Etag;
        return map;
    }
}
