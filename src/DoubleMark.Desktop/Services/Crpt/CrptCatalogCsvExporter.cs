using System.Globalization;
using System.IO;
using System.Text;
using DoubleMark.Desktop.ViewModels.Crpt;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// Exports visible catalog rows to CSV (Phase C7.3). No marking codes or secrets.
/// </summary>
public static class CrptCatalogCsvExporter
{
    private static readonly string[] Headers =
    [
        "GTIN",
        "Название",
        "ТН ВЭД",
        "Дата изменения",
        "Состояние",
        "Статус карточки",
        "Тип",
        "Категория",
        "Товарная группа",
        "Можно заказать",
    ];

    public static string FormatCsv(IEnumerable<CrptCatalogRowViewModel> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', Headers));

        foreach (var row in rows)
        {
            sb.Append(Escape(row.Gtin)).Append(';');
            sb.Append(Escape(row.Name)).Append(';');
            sb.Append(Escape(row.TnvedCode ?? "")).Append(';');
            sb.Append(Escape(row.UpdatedAtDisplay)).Append(';');
            sb.Append(Escape(row.ProductStateDisplay)).Append(';');
            sb.Append(Escape(row.CardStatusDisplay)).Append(';');
            sb.Append(Escape(row.CardTypeDisplay)).Append(';');
            sb.Append(Escape(row.CategoryName ?? "")).Append(';');
            sb.Append(Escape(row.ProductGroupDisplay)).Append(';');
            sb.Append(Escape(row.CanOrderCodes ? "Да" : "Нет"));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static void WriteUtf8Bom(string path, string csv) =>
        File.WriteAllText(path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var needsQuotes = value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuotes)
            return value;

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
