using System.Globalization;
using DubliMark.Core.Export;
using DubliMark.Core.Models;
using DubliMark.Core.Parsing;
using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;

namespace DubliMark.Core.Print;

public sealed class MarkRenderService
{
    public MarkRenderResult Render(MarkRenderRequest request)
    {
        if (!request.ParseResult.IsValid || request.ParseResult.Code == null)
            throw new InvalidOperationException("Only valid marking codes can be rendered for print.");

        var timestamp = request.Timestamp ?? DateTimeOffset.Now;
        var code = request.ParseResult.Code;
        var normalized = NormalizePayload(request.RawPayload, request.ParseResult);
        var matrix = CreateDataMatrix(normalized, MmToPx(request.Template.DataMatrixWidthMm, request.Dpi),
            MmToPx(request.Template.DataMatrixHeightMm, request.Dpi));
        var textBlocks = RenderTextBlocks(request.Template, code, timestamp, request.Source);
        var png = RenderPng(request.Template, matrix, textBlocks, request.Dpi);
        var pdf = LabelPdfWriter.Encode(request.Template, matrix, textBlocks);

        return new MarkRenderResult
        {
            Timestamp = timestamp,
            Source = request.Source,
            Template = request.Template,
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
            PngWidthPx = MmToPx(request.Template.LabelWidthMm, request.Dpi),
            PngHeightPx = MmToPx(request.Template.LabelHeightMm, request.Dpi),
            PdfWidthPt = MmToPt(request.Template.LabelWidthMm),
            PdfHeightPt = MmToPt(request.Template.LabelHeightMm),
            Dpi = request.Dpi
        };
    }

    public static string NormalizePayload(string raw, ParseResult result)
    {
        if (result.IsValid && result.Code?.RawData is { Length: > 0 } parsedRaw)
            return parsedRaw;

        var normalized = Gs1BarcodeEncoding.NormalizeForParse(raw);
        return normalized.FoundAi01 ? normalized.Payload : raw;
    }

    public static string SubstituteText(
        string text,
        MarkingCode code,
        DateTimeOffset timestamp,
        string source)
    {
        var ai92 = code.VerificationCode ?? "";
        var ai92Short = ai92.Length <= 12 ? ai92 : ai92[..9] + "...";
        return text
            .Replace("{gtin}", code.Gtin, StringComparison.Ordinal)
            .Replace("{serial}", code.Serial, StringComparison.Ordinal)
            .Replace("{ai91}", code.VerificationKey ?? "", StringComparison.Ordinal)
            .Replace("{ai92_short}", ai92Short, StringComparison.Ordinal)
            .Replace("{codeType}", code.CodeType.ToString(), StringComparison.Ordinal)
            .Replace("{date}", timestamp.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{time}", timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{source}", source, StringComparison.Ordinal);
    }

    private static IReadOnlyList<RenderedTextBlock> RenderTextBlocks(
        PrintTemplate template,
        MarkingCode code,
        DateTimeOffset timestamp,
        string source) =>
        template.TextBlocks
            .Select(t => new RenderedTextBlock(
                SubstituteText(t.Text, code, timestamp, source),
                t.Xmm,
                t.Ymm,
                t.FontSizePt,
                t.Bold))
            .ToList();

    private static BitMatrix CreateDataMatrix(string payload, int widthPx, int heightPx)
    {
        var writer = new DataMatrixWriter();
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.CHARACTER_SET] = "ISO-8859-1",
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

        DrawMatrix(rgb, width, height, matrix, MmToPx(template.DataMatrixXmm, dpi), MmToPx(template.DataMatrixYmm, dpi));
        foreach (var block in textBlocks)
            DrawTinyText(rgb, width, height, block, dpi);

        return LabelPngWriter.EncodeRgb(width, height, rgb, dpi);
    }

    private static void DrawMatrix(byte[] rgb, int canvasWidth, int canvasHeight, BitMatrix matrix, int left, int top)
    {
        for (var y = 0; y < matrix.Height; y++)
        {
            var py = top + y;
            if (py < 0 || py >= canvasHeight)
                continue;

            for (var x = 0; x < matrix.Width; x++)
            {
                if (!matrix[x, y])
                    continue;

                var px = left + x;
                if (px < 0 || px >= canvasWidth)
                    continue;

                SetPixel(rgb, canvasWidth, px, py, 0, 0, 0);
            }
        }
    }

    private static void DrawTinyText(byte[] rgb, int width, int height, RenderedTextBlock block, int dpi)
    {
        var scale = Math.Max(1, (int)Math.Round(block.FontSizePt * dpi / 72.0 / 7.0));
        var x = MmToPx(block.Xmm, dpi);
        var y = MmToPx(block.Ymm, dpi);

        foreach (var ch in block.Text.ToUpperInvariant())
        {
            DrawGlyph(rgb, width, height, x, y, ch, scale, block.Bold);
            x += 6 * scale;
            if (x >= width)
                break;
        }
    }

    private static void DrawGlyph(byte[] rgb, int width, int height, int x, int y, char ch, int scale, bool bold)
    {
        var glyph = TinyFont.GetGlyph(ch);
        for (var row = 0; row < glyph.Length; row++)
        {
            for (var col = 0; col < glyph[row].Length; col++)
            {
                if (glyph[row][col] != '1')
                    continue;

                FillRect(rgb, width, height, x + col * scale, y + row * scale, bold ? scale + 1 : scale, scale);
            }
        }
    }

    private static void FillRect(byte[] rgb, int width, int height, int x, int y, int w, int h)
    {
        for (var py = y; py < y + h; py++)
        {
            if (py < 0 || py >= height)
                continue;

            for (var px = x; px < x + w; px++)
            {
                if (px < 0 || px >= width)
                    continue;

                SetPixel(rgb, width, px, py, 0, 0, 0);
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

internal static class TinyFont
{
    private static readonly string[] Unknown = ["111", "001", "010", "000", "010", "000", "010"];

    public static string[] GetGlyph(char ch) =>
        Glyphs.TryGetValue(ch, out var glyph) ? glyph : Unknown;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        [' '] = ["000", "000", "000", "000", "000", "000", "000"],
        ['-'] = ["000", "000", "000", "111", "000", "000", "000"],
        ['_'] = ["000", "000", "000", "000", "000", "000", "111"],
        ['.'] = ["000", "000", "000", "000", "000", "110", "110"],
        [':'] = ["000", "110", "110", "000", "110", "110", "000"],
        ['/'] = ["001", "001", "010", "010", "100", "100", "000"],
        ['0'] = ["111", "101", "101", "101", "101", "101", "111"],
        ['1'] = ["010", "110", "010", "010", "010", "010", "111"],
        ['2'] = ["111", "001", "001", "111", "100", "100", "111"],
        ['3'] = ["111", "001", "001", "111", "001", "001", "111"],
        ['4'] = ["101", "101", "101", "111", "001", "001", "001"],
        ['5'] = ["111", "100", "100", "111", "001", "001", "111"],
        ['6'] = ["111", "100", "100", "111", "101", "101", "111"],
        ['7'] = ["111", "001", "001", "010", "010", "100", "100"],
        ['8'] = ["111", "101", "101", "111", "101", "101", "111"],
        ['9'] = ["111", "101", "101", "111", "001", "001", "111"],
        ['A'] = ["010", "101", "101", "111", "101", "101", "101"],
        ['B'] = ["110", "101", "101", "110", "101", "101", "110"],
        ['C'] = ["111", "100", "100", "100", "100", "100", "111"],
        ['D'] = ["110", "101", "101", "101", "101", "101", "110"],
        ['E'] = ["111", "100", "100", "110", "100", "100", "111"],
        ['F'] = ["111", "100", "100", "110", "100", "100", "100"],
        ['G'] = ["111", "100", "100", "101", "101", "101", "111"],
        ['H'] = ["101", "101", "101", "111", "101", "101", "101"],
        ['I'] = ["111", "010", "010", "010", "010", "010", "111"],
        ['J'] = ["001", "001", "001", "001", "101", "101", "111"],
        ['K'] = ["101", "101", "110", "100", "110", "101", "101"],
        ['L'] = ["100", "100", "100", "100", "100", "100", "111"],
        ['M'] = ["101", "111", "111", "101", "101", "101", "101"],
        ['N'] = ["101", "111", "111", "111", "101", "101", "101"],
        ['O'] = ["111", "101", "101", "101", "101", "101", "111"],
        ['P'] = ["111", "101", "101", "111", "100", "100", "100"],
        ['Q'] = ["111", "101", "101", "101", "111", "001", "001"],
        ['R'] = ["111", "101", "101", "111", "110", "101", "101"],
        ['S'] = ["111", "100", "100", "111", "001", "001", "111"],
        ['T'] = ["111", "010", "010", "010", "010", "010", "010"],
        ['U'] = ["101", "101", "101", "101", "101", "101", "111"],
        ['V'] = ["101", "101", "101", "101", "101", "101", "010"],
        ['W'] = ["101", "101", "101", "101", "111", "111", "101"],
        ['X'] = ["101", "101", "101", "010", "101", "101", "101"],
        ['Y'] = ["101", "101", "101", "010", "010", "010", "010"],
        ['Z'] = ["111", "001", "001", "010", "100", "100", "111"]
    };
}
