using DoubleMark.Core.Export;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services.Cloud;
using DoubleMark.Desktop.Views;

namespace DoubleMark.Desktop.Services;

public static class ScanHistoryItemBuilder
{
    public static ScanHistoryItem FromScan(
        ParseResult result,
        string raw,
        string source,
        MarkExportResult? exportResult,
        PrintPipelineResult? printResult,
        int? imageGsCount,
        string parseError,
        string rawEscaped,
        string normalizedEscaped,
        string rawHex,
        string templateName,
        string printerName)
    {
        var code = result.Code!;
        var normalized = exportResult?.NormalizedPayload
                         ?? code.RawData
                         ?? Gs1BarcodeEncoding.NormalizeForParse(raw).Payload;
        var gsCount = imageGsCount ?? Gs1BarcodeEncoding.CountGs(normalized);
        var status = result.InfoMessages.Count > 0 ? "Предупреждение" : "Успешно";
        var statusKind = result.IsValid
            ? result.InfoMessages.Count > 0 ? UiStatusKind.Warning : UiStatusKind.Success
            : UiStatusKind.Error;
        var savedFolder = exportResult?.ExportDirectory
                          ?? printResult?.Export?.DirectoryPath
                          ?? "—";
        var printStatus = printResult == null
            ? "—"
            : printResult.BlockedDuplicate
                ? "Дубль заблокирован"
                : printResult.Printed
                    ? "Напечатано"
                    : string.IsNullOrWhiteSpace(printResult.Error) ? "Не печаталось" : printResult.Error;

        return new ScanHistoryItem
        {
            Timestamp = DateTime.Now,
            Status = status,
            StatusKind = statusKind,
            Gtin = code.Gtin ?? "—",
            Serial = code.Serial ?? "—",
            Ai91 = code.VerificationKey ?? "—",
            Ai92 = code.VerificationCode ?? "—",
            Ai93 = code.AdditionalField93 ?? "—",
            HasAi01 = !string.IsNullOrWhiteSpace(code.Gtin),
            HasAi21 = !string.IsNullOrWhiteSpace(code.Serial),
            HasAi91Flag = code.VerificationKey != null,
            HasAi92Flag = code.VerificationCode != null,
            GsCount = gsCount.ToString(),
            Source = source,
            CodeType = CodeTypeShort(code.CodeType),
            RawEscaped = rawEscaped,
            RawPayload = raw,
            MaskedPreview = ScanHistoryMasking.BuildMaskedPreview(raw),
            NormalizedEscaped = normalizedEscaped,
            RawHex = rawHex,
            Error = parseError,
            SavedFolder = savedFolder,
            Template = templateName,
            Printer = printerName,
            PrintStatus = printStatus,
            PreviewImage = null
        };
    }

    public static string DedupeKey(ScanHistoryItem item) =>
        item.Timestamp.ToString("yyyyMMddHHmmss") + "|" + item.RawPayload;

    private static string CodeTypeShort(MarkingCodeType type) =>
        type switch
        {
            MarkingCodeType.Full => "Full",
            MarkingCodeType.Short => "Short",
            _ => "Unknown"
        };
}
