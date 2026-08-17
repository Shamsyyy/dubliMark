namespace DoubleMark.Crpt;

/// <summary>
/// Official CRPT documentation URLs (spec §16).
/// </summary>
public static class CrptDocumentationLinks
{
    public const string TrueApiDocs = "https://docs.crpt.ru/gismt/True_API/";

    /// <summary>API СУЗ 3.0 — no public URL in spec; download from LK Честного ЗНАКа.</summary>
    public const string SuzApi30Label = "API СУЗ 3.0";

    public const string SuzApi30DownloadNote =
        "Скачать из ЛК Честного ЗНАКа → Помощь (промышленный контур).";

    public const string NationalCatalogApiDocs = "https://docs.crpt.ru/gismt/API_НК/";

    public const string MarkirovkaKnowledgeBase = "https://markirovka.ru";

    public const string BecomeTechnologyPartner =
        "https://markirovka.ru/knowledge/developers/become-technology-partner/kak-stat-tekhnologicheskim-partnerom-tsrpt-instruktsiya";

    public static IReadOnlyList<string> AllDocumentedUrls { get; } =
        [
            TrueApiDocs,
            NationalCatalogApiDocs,
            MarkirovkaKnowledgeBase,
            BecomeTechnologyPartner,
        ];
}
