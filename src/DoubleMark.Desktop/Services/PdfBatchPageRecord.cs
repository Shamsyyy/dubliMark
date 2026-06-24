using DoubleMark.Core.Models;

namespace DoubleMark.Desktop.Services;

public enum PdfBatchPageStatus
{
    Printed,
    VerifiedOk,
    DecodeFailed,
    NotReadyForPrint,
    PrintFailed
}

public sealed class PdfBatchPageRecord
{
    public int PageNumber { get; init; }
    public PdfBatchPageStatus Status { get; set; }
    public string? Reason { get; set; }
    public string? Gtin { get; init; }
    public string? Serial { get; init; }
    public string? RawPayload { get; set; }
    public ParseResult? ParseResult { get; set; }

    public bool IsProblem => Status is PdfBatchPageStatus.DecodeFailed
        or PdfBatchPageStatus.NotReadyForPrint
        or PdfBatchPageStatus.PrintFailed;

    public string StatusLabel => Status switch
    {
        PdfBatchPageStatus.Printed => "Напечатано",
        PdfBatchPageStatus.VerifiedOk => "Распознано",
        PdfBatchPageStatus.DecodeFailed => "Не прочитан",
        PdfBatchPageStatus.NotReadyForPrint => "Не готов к печати",
        PdfBatchPageStatus.PrintFailed => "Сбой печати",
        _ => "—"
    };

    public static PdfBatchPageRecord FromDecode(int pageNumber, PdfPageDecodeResult decoded)
    {
        var gtin = decoded.ParseResult?.Code?.Gtin;
        var serial = decoded.ParseResult?.Code?.Serial;
        if (!decoded.Success)
        {
            var reason = BuildDecodeFailureReason(decoded);
            return new PdfBatchPageRecord
            {
                PageNumber = pageNumber,
                Status = PdfBatchPageStatus.DecodeFailed,
                Reason = reason,
                Gtin = gtin,
                Serial = serial,
                RawPayload = decoded.RawPayload,
                ParseResult = decoded.ParseResult
            };
        }

        if (!ScanDiagnosticsHelper.IsReadyForPrint(decoded.ParseResult!, out var readyReason))
        {
            return new PdfBatchPageRecord
            {
                PageNumber = pageNumber,
                Status = PdfBatchPageStatus.NotReadyForPrint,
                Reason = "Не готов к печати: " + readyReason,
                Gtin = gtin,
                Serial = serial,
                RawPayload = decoded.RawPayload,
                ParseResult = decoded.ParseResult
            };
        }

        return new PdfBatchPageRecord
        {
            PageNumber = pageNumber,
            Status = PdfBatchPageStatus.VerifiedOk,
            Gtin = gtin,
            Serial = serial,
            RawPayload = decoded.RawPayload,
            ParseResult = decoded.ParseResult
        };
    }

    private static string BuildDecodeFailureReason(PdfPageDecodeResult decoded)
    {
        if (!string.IsNullOrWhiteSpace(decoded.Error))
            return decoded.Error;

        if (decoded.ParseResult != null && !decoded.ParseResult.IsValid)
            return decoded.ParseResult.ErrorMessage ?? "Код не прошёл проверку GS1";

        return "DataMatrix не найден на странице (попробуйте перепроверку с улучшенным чтением)";
    }
}
