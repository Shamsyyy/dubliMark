namespace DoubleMark.Core.Print;

public enum LabelFontId
{
    ArialIndustrial = 0,
    VerdanaSafe = 1,
    SegoeModern = 2,
    CalibriSoft = 3,
    GeorgiaClassic = 4
}

public static class LabelFontRegistry
{
    public sealed record FontOption(LabelFontId Id, string DisplayName, string FamilyName, string Description)
    {
        public override string ToString() => DisplayName;
    }

    public static IReadOnlyList<FontOption> All { get; } =
    [
        new(LabelFontId.ArialIndustrial, "Arial — промышленный",
            "Arial",
            "Классический sans-serif для этикеток, хорошо читается при мелком кегле."),
        new(LabelFontId.VerdanaSafe, "Verdana — устойчивый",
            "Verdana",
            "Большая x-height, устойчив к размытию и дефектам термопечати."),
        new(LabelFontId.SegoeModern, "Segoe UI — современный",
            "Segoe UI",
            "Чистый современный шрифт Windows."),
        new(LabelFontId.CalibriSoft, "Calibri — мягкий",
            "Calibri",
            "Мягкий humanist sans-serif."),
        new(LabelFontId.GeorgiaClassic, "Georgia — классика",
            "Georgia",
            "Аккуратный serif для контрастных надписей.")
    ];

    public static FontOption Resolve(LabelFontId id) =>
        All.FirstOrDefault(f => f.Id == id) ?? All[0];

    public static string ResolveFamily(LabelFontId id) => Resolve(id).FamilyName;

    public static LabelFontId Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return LabelFontId.ArialIndustrial;

        if (Enum.TryParse<LabelFontId>(value, true, out var parsed))
            return parsed;

        return All.FirstOrDefault(f =>
            string.Equals(f.FamilyName, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.DisplayName, value, StringComparison.OrdinalIgnoreCase))?.Id
               ?? LabelFontId.ArialIndustrial;
    }
}
