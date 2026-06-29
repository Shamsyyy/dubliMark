namespace DoubleMark.Core.Crpt;

/// <summary>
/// CRPT True API / SUZ productGroup codes → Russian display names.
/// Source: docs.crpt.ru Exchange «Приложение 1. Справочник „Список поддерживаемых товарных групп“» (v565, 2026-06).
/// </summary>
public static class CrptProductGroupCatalog
{
    public sealed record Entry(int? DbId, string Code, string DisplayNameRu);

    private static readonly Dictionary<string, Entry> ByCode = BuildIndex();

    /// <summary>All documented product groups, keyed by API code (case-insensitive).</summary>
    public static IReadOnlyDictionary<string, Entry> All => ByCode;

    public static bool IsKnown(string? productGroupCode) =>
        !string.IsNullOrWhiteSpace(productGroupCode) &&
        ByCode.ContainsKey(CrptProductGroup.Normalize(productGroupCode));

    public static string GetDisplayName(string? productGroupCode)
    {
        if (string.IsNullOrWhiteSpace(productGroupCode))
            return "—";

        var code = CrptProductGroup.Normalize(productGroupCode);
        return ByCode.TryGetValue(code, out var entry)
            ? entry.DisplayNameRu
            : code;
    }

    public static Entry? TryGetEntry(string? productGroupCode)
    {
        if (string.IsNullOrWhiteSpace(productGroupCode))
            return null;

        return ByCode.TryGetValue(CrptProductGroup.Normalize(productGroupCode), out var entry)
            ? entry
            : null;
    }

    private static Dictionary<string, Entry> BuildIndex()
    {
        var entries = new Entry[]
        {
            new(1, "lp", "Лёгкая промышленность"),
            new(2, "shoes", "Обувные товары"),
            new(3, "tobacco", "Табачная продукция"),
            new(4, "perfumery", "Духи и туалетная вода"),
            new(5, "tires", "Шины и покрышки пневматические резиновые новые"),
            new(6, "electronics", "Фотокамеры (кроме кинокамер), фотовспышки и лампы-вспышки"),
            new(7, "pharma", "Лекарственные препараты для медицинского применения"),
            new(8, "milk", "Молочная продукция"),
            new(9, "bicycle", "Велосипеды и велосипедные рамы"),
            new(10, "wheelchairs", "Медицинские изделия"),
            new(11, "alcohol", "Алкоголь"),
            new(12, "otp", "Альтернативная табачная продукция"),
            new(13, "water", "Упакованная вода"),
            new(14, "furs", "Товары из натурального меха"),
            new(15, "beer", "Пиво, напитки, изготавливаемые на основе пива, слабоалкогольные напитки"),
            new(16, "ncp", "Никотиносодержащая продукция"),
            new(17, "bio", "Специализированная пищевая продукция и БАД к пище"),
            new(19, "antiseptic", "Антисептики и дезинфицирующие средства"),
            new(20, "petfood", "Корма для животных"),
            new(21, "seafood", "Морепродукты"),
            new(22, "nabeer", "Безалкогольное пиво"),
            new(23, "softdrinks", "Соковая продукция и безалкогольные напитки"),
            new(25, "meat", "Мясные изделия"),
            new(26, "vetpharma", "Ветеринарные препараты"),
            new(27, "toys", "Игры и игрушки для детей"),
            new(28, "radio", "Радиоэлектронная продукция"),
            new(31, "titan", "Титановая металлопродукция"),
            new(32, "conserve", "Консервированная продукция"),
            new(33, "vegetableoil", "Растительные масла"),
            new(34, "opticfiber", "Оптоволокно и оптоволоконная продукция"),
            new(35, "chemistry", "Косметика, бытовая химия и товары личной гигиены"),
            new(36, "books", "Печатная продукция"),
            new(37, "grocery", "Бакалейная продукция"),
            new(38, "pharmaraw", "Фармацевтическое сырьё, лекарственные средства"),
            new(39, "construction", "Строительные материалы"),
            new(40, "fire", "Пиротехника и огнетушащее оборудование"),
            new(41, "heater", "Отопительные приборы"),
            new(42, "cableraw", "Кабельно-проводниковая продукция"),
            new(43, "autofluids", "Моторные масла"),
            new(44, "polymer", "Полимерные трубы"),
            new(45, "sweets", "Сладости и кондитерские изделия"),
            new(48, "carparts", "Автозапчасти и комплектующие транспортных средств"),
            new(49, "furslp", "Натуральный мех"),
            new(50, "nicotindev", "Радиоэлектронная продукция. Электронные системы доставки никотина"),
            new(51, "gadgets", "Радиоэлектронная продукция. Ноутбуки и смартфоны"),
            new(52, "frozen", "Полуфабрикаты и замороженные продукты"),
            new(53, "fertilizers", "Удобрения в потребительской упаковке"),
            new(54, "homeware", "Товары для дома и интерьера"),
            new(59, "pyrotechnics", "Пиротехнические изделия"),
        };

        var index = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            index[entry.Code] = entry;

        return index;
    }
}
