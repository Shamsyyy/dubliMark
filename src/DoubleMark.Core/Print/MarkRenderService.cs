using System.Globalization;
using DoubleMark.Core.Export;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;

namespace DoubleMark.Core.Print;

public sealed class MarkRenderService
{
    public MarkRenderResult Render(MarkRenderRequest request)
    {
        if (!request.ParseResult.IsValid || request.ParseResult.Code == null)
            throw new InvalidOperationException("Only valid marking codes can be rendered for print.");

        var timestamp = request.Timestamp ?? DateTimeOffset.Now;
        var code = request.ParseResult.Code;
        var normalized = NormalizePayload(request.RawPayload, request.ParseResult);
        var baseTemplate = TemplateLayoutHelper.NormalizeForRender(request.Template);
        var blockTemplates = TemplateLayoutHelper.BuildEffectiveTextBlocks(
            baseTemplate,
            request.ShowDate,
            request.ShowShipment,
            request.ShowOrder);
        var substitutedBlocks = blockTemplates
            .Select(block => block with
            {
                Text = SubstituteText(
                    block.Text,
                    code,
                    timestamp,
                    request.Source,
                    request.ShipmentNumber,
                    request.OrderNumber)
            })
            .ToList();
        var effectiveTemplate = baseTemplate with { TextBlocks = substitutedBlocks };
        var matrix = CreateDataMatrix(normalized, MmToPx(effectiveTemplate.DataMatrixWidthMm, request.Dpi),
            MmToPx(effectiveTemplate.DataMatrixHeightMm, request.Dpi));
        var textBlocks = RenderTextBlocks(effectiveTemplate);
        var png = RenderPng(effectiveTemplate, matrix, textBlocks, request.Dpi);
        var pdf = LabelPdfWriter.Encode(effectiveTemplate, matrix, textBlocks);

        return new MarkRenderResult
        {
            Timestamp = timestamp,
            Source = request.Source,
            Template = effectiveTemplate,
            RawPayload = request.RawPayload,
            NormalizedPayload = normalized,
            RawPayloadEscaped = MarkExportService.EscapePayload(request.RawPayload),
            NormalizedPayloadEscaped = MarkExportService.EscapePayload(normalized),
            RawHex = Gs1BarcodeEncoding.ToHex(request.RawPayload),
            GsCount = Gs1BarcodeEncoding.CountGs(normalized),
            HasAi01 = normalized.StartsWith("01", StringComparison.Ordinal),
            HasAi21 = normalized.Length >= 18 && normalized.AsSpan(16, 2).SequenceEqual("21"),
            HasAi91 = code.VerificationKey != null,
            HasAi92 = code.VerificationCode != null,
            HasAi93 = code.AdditionalField93 != null,
            CodeType = code.CodeType.ToString(),
            Gtin = code.Gtin,
            Serial = code.Serial,
            Ai91 = code.VerificationKey,
            Ai92 = code.VerificationCode,
            Ai93 = code.AdditionalField93,
            PngBytes = png,
            PdfBytes = pdf,
            PngWidthPx = MmToPx(effectiveTemplate.LabelWidthMm, request.Dpi),
            PngHeightPx = MmToPx(effectiveTemplate.LabelHeightMm, request.Dpi),
            PdfWidthPt = MmToPt(effectiveTemplate.LabelWidthMm),
            PdfHeightPt = MmToPt(effectiveTemplate.LabelHeightMm),
            Dpi = request.Dpi
        };
    }

    public static string NormalizePayload(string raw, ParseResult result)
    {
        if (result.IsValid && result.Code != null)
            return Gs1BarcodeEncoding.BuildBarcodePayload(result.Code);

        var normalized = Gs1BarcodeEncoding.NormalizeForParse(raw);
        return normalized.FoundAi01 ? normalized.Payload : raw;
    }

    public static string SubstituteText(
        string text,
        MarkingCode code,
        DateTimeOffset timestamp,
        string source,
        string? shipmentNumber = null,
        string? orderNumber = null)
    {
        var ai92 = code.VerificationCode ?? "";
        var ai92Short = ai92.Length <= 12 ? ai92 : ai92[..9] + "...";
        var shipment = string.IsNullOrWhiteSpace(shipmentNumber) ? "—" : shipmentNumber.Trim();
        var order = string.IsNullOrWhiteSpace(orderNumber) ? "—" : orderNumber.Trim();
        return text
            .Replace("{gtin}", code.Gtin, StringComparison.Ordinal)
            .Replace("{serial}", code.Serial, StringComparison.Ordinal)
            .Replace("{ai91}", code.VerificationKey ?? "", StringComparison.Ordinal)
            .Replace("{ai92_short}", ai92Short, StringComparison.Ordinal)
            .Replace("{codeType}", code.CodeType.ToString(), StringComparison.Ordinal)
            .Replace("{date}", timestamp.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{time}", timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{source}", source, StringComparison.Ordinal)
            .Replace("{shipment}", shipment, StringComparison.Ordinal)
            .Replace("{shipment_no}", shipment, StringComparison.Ordinal)
            .Replace("{order}", order, StringComparison.Ordinal)
            .Replace("{order_no}", order, StringComparison.Ordinal);
    }

    private static IReadOnlyList<RenderedTextBlock> RenderTextBlocks(PrintTemplate template) =>
        template.TextBlocks
            .Where(b => TemplateLayoutHelper.IsInsideLabel(template, b))
            .Select(t => new RenderedTextBlock(t.Text, t.Xmm, t.Ymm, t.FontSizePt, t.Bold, t.Orientation))
            .ToList();

    private static BitMatrix CreateDataMatrix(string payload, int widthPx, int heightPx)
    {
        var writer = new DataMatrixWriter();
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.CHARACTER_SET] = "ISO-8859-1",
            [EncodeHintType.GS1_FORMAT] = true,
            [EncodeHintType.MARGIN] = 1
        };
        return writer.encode(payload, BarcodeFormat.DATA_MATRIX, widthPx, heightPx, hints);
    }

    private static byte[] RenderPng(
        PrintTemplate template,
        BitMatrix matrix,
        IReadOnlyList<RenderedTextBlock> textBlocks,
        int dpi)
    {
        var width = MmToPx(template.LabelWidthMm, dpi);
        var height = MmToPx(template.LabelHeightMm, dpi);
        var rgb = Enumerable.Repeat((byte)255, width * height * 3).ToArray();

        DrawMatrix(
            rgb,
            width,
            height,
            matrix,
            MmToPx(template.DataMatrixXmm, dpi),
            MmToPx(template.DataMatrixYmm, dpi),
            MmToPx(template.DataMatrixWidthMm, dpi),
            MmToPx(template.DataMatrixHeightMm, dpi));
        foreach (var block in textBlocks)
        {
            TextBlockRenderHelper.PaintBlock(
                rgb,
                width,
                height,
                block.Text,
                block.FontSizePt,
                block.Bold,
                block.Orientation,
                dpi,
                MmToPx(block.Xmm, dpi),
                MmToPx(block.Ymm, dpi));
        }

        return LabelPngWriter.EncodeRgb(width, height, rgb, dpi);
    }

    private static void DrawMatrix(
        byte[] rgb,
        int canvasWidth,
        int canvasHeight,
        BitMatrix matrix,
        int left,
        int top,
        int targetWidthPx,
        int targetHeightPx)
    {
        if (matrix.Width <= 0 || matrix.Height <= 0 || targetWidthPx <= 0 || targetHeightPx <= 0)
            return;

        for (var y = 0; y < targetHeightPx; y++)
        {
            var py = top + y;
            if (py < 0 || py >= canvasHeight)
                continue;

            var my = y * matrix.Height / targetHeightPx;
            if (my < 0 || my >= matrix.Height)
                continue;

            for (var x = 0; x < targetWidthPx; x++)
            {
                var mx = x * matrix.Width / targetWidthPx;
                if (mx < 0 || mx >= matrix.Width)
                    continue;
                if (!matrix[mx, my])
                    continue;

                var px = left + x;
                if (px < 0 || px >= canvasWidth)
                    continue;

                SetPixel(rgb, canvasWidth, px, py, 0, 0, 0);
            }
        }
    }

    private static void SetPixel(byte[] rgb, int width, int x, int y, byte r, byte g, byte b)
    {
        var idx = (y * width + x) * 3;
        rgb[idx] = r;
        rgb[idx + 1] = g;
        rgb[idx + 2] = b;
    }

    private static int MmToPx(double mm, int dpi) => Math.Max(1, (int)Math.Round(mm * dpi / 25.4));
    private static double MmToPt(double mm) => mm * 72.0 / 25.4;
}
