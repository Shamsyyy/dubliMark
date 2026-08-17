using System.Windows.Media.Imaging;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using DoubleMark.Core.Print;

namespace DoubleMark.Desktop.Services;

public sealed record PdfPageDecodeResult(
    int PageNumber,
    bool Success,
    string? RawPayload,
    ParseResult? ParseResult,
    string? Error);

public sealed record PdfBatchAnalyzeResult(
    int TotalPagesInFile,
    IReadOnlyList<int> RequestedPages,
    IReadOnlyList<PdfPageDecodeResult> Results)
{
    public int ValidCount => Results.Count(r => r.Success);
    public int FailedCount => Results.Count(r => !r.Success);
}

public sealed record PdfBatchProgress(int CurrentPage, int TotalPages, string Stage, int CompletedCount);

public sealed class PdfBatchDecodeService
{
    private readonly Gs1Parser _parser = new();

    public int GetPageCount(string pdfPath)
    {
        using var session = PdfDocumentSession.OpenFile(pdfPath, PdfRenderProfile.Fast);
        return session.PageCount;
    }

    public PdfPageDecodeResult DecodePage(
        PdfDocumentSession session,
        int pageNumber1Based,
        PdfRenderProfile profile,
        PrintTemplate? template = null)
    {
        try
        {
            var bitmap = session.RenderPage(pageNumber1Based - 1);
            if (!TryDecodePdfPageBitmap(bitmap, template, profile, out var fastDecoded, out var decodeError))
                return new PdfPageDecodeResult(pageNumber1Based, false, null, null, decodeError ?? "Код не найден на странице.");

            var parse = MarkingCodeIntegrity.Enrich(_parser.Parse(fastDecoded!.Raw), fastDecoded.Raw);
            if (!parse.IsValid || parse.Code == null)
                return new PdfPageDecodeResult(pageNumber1Based, false, fastDecoded.Raw, parse, parse.ErrorMessage ?? "Невалидный GS1-код.");

            return new PdfPageDecodeResult(pageNumber1Based, true, fastDecoded.Raw, parse, null);
        }
        catch (Exception ex)
        {
            LoggingService.Warn("PdfBatch", $"Page {pageNumber1Based} decode failed: {ex.Message}");
            return new PdfPageDecodeResult(pageNumber1Based, false, null, null, ex.Message);
        }
    }

    public async Task<PdfBatchAnalyzeResult> AnalyzeAsync(
        string pdfPath,
        IReadOnlyList<int> pages1Based,
        PdfRenderProfile profile,
        PrintTemplate? template = null,
        IProgress<PdfBatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var session = await PdfDocumentSession.OpenFileAsync(pdfPath, profile, cancellationToken)
            .ConfigureAwait(false);

        var results = new List<PdfPageDecodeResult>(pages1Based.Count);
        var completed = 0;

        foreach (var pageNumber in pages1Based)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completed++;
            progress?.Report(new PdfBatchProgress(pageNumber, pages1Based.Count, "Читаем DataMatrix", completed));

            var decoded = await Task.Run(
                () => DecodePage(session, pageNumber, profile, template),
                cancellationToken).ConfigureAwait(false);
            results.Add(decoded);
        }

        return new PdfBatchAnalyzeResult(session.PageCount, pages1Based, results);
    }

    private static bool TryDecodePdfPageBitmap(
        BitmapSource bitmap,
        PrintTemplate? template,
        PdfRenderProfile profile,
        out ImageDecodeResult? result,
        out string? error)
    {
        if (ImageBarcodeDecoder.TryDecodeFromBitmapFast(bitmap, template, out result, out error))
            return true;

        if (profile == PdfRenderProfile.Fast)
        {
            error ??= "Код на изображении не найден.";
            return false;
        }

        return ImageBarcodeDecoder.TryDecodeFromBitmapPdfEnhanced(bitmap, template, out result, out error);
    }
}
