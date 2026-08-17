namespace DoubleMark.Core.Print;

public static class PageRangeParser
{
    public static bool TryParse(string? input, int totalPages, out IReadOnlyList<int> pages, out string? error)
    {
        pages = Array.Empty<int>();
        error = null;

        if (totalPages <= 0)
        {
            error = "PDF не содержит страниц.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            pages = Enumerable.Range(1, totalPages).ToList();
            return true;
        }

        var selected = new SortedSet<int>();
        foreach (var segment in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseSegment(segment, totalPages, selected, out error))
                return false;
        }

        if (selected.Count == 0)
        {
            error = "Укажите хотя бы одну страницу.";
            return false;
        }

        pages = selected.ToList();
        return true;
    }

    private static bool TryParseSegment(string segment, int totalPages, SortedSet<int> selected, out string? error)
    {
        error = null;
        var dash = segment.IndexOf('-');
        if (dash >= 0)
        {
            var startText = segment[..dash].Trim();
            var endText = segment[(dash + 1)..].Trim();
            if (!int.TryParse(startText, out var start) || !int.TryParse(endText, out var end))
            {
                error = $"Неверный диапазон: {segment}";
                return false;
            }

            if (start > end)
                (start, end) = (end, start);

            for (var page = start; page <= end; page++)
            {
                if (!IsPageInRange(page, totalPages, out error))
                    return false;
                selected.Add(page);
            }

            return true;
        }

        if (!int.TryParse(segment, out var singlePage))
        {
            error = $"Неверная страница: {segment}";
            return false;
        }

        if (!IsPageInRange(singlePage, totalPages, out error))
            return false;

        selected.Add(singlePage);
        return true;
    }

    private static bool IsPageInRange(int page, int totalPages, out string? error)
    {
        if (page >= 1 && page <= totalPages)
        {
            error = null;
            return true;
        }

        error = $"Страница {page} вне PDF (1–{totalPages}).";
        return false;
    }
}
