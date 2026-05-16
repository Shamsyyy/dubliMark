using System.Globalization;
using System.Text;
using ZXing.Common;

namespace DubliMark.Core.Print;

internal static class LabelPdfWriter
{
    public static byte[] Encode(PrintTemplate template, BitMatrix matrix, IReadOnlyList<RenderedTextBlock> textBlocks)
    {
        var widthPt = MmToPt(template.LabelWidthMm);
        var heightPt = MmToPt(template.LabelHeightMm);
        var content = BuildContent(template, matrix, textBlocks, widthPt, heightPt);
        var contentBytes = Encoding.ASCII.GetBytes(content);
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            string.Format(CultureInfo.InvariantCulture,
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:F3} {1:F3}] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>",
                widthPt, heightPt),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>",
            $"<< /Length {contentBytes.Length} >>\nstream\n{content}\nendstream"
        };

        using var stream = new MemoryStream();
        WriteAscii(stream, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };

        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {objects.Length + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            WriteAscii(stream, $"{offset:0000000000} 00000 n \n");

        WriteAscii(stream,
            $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return stream.ToArray();
    }

    private static string BuildContent(
        PrintTemplate template,
        BitMatrix matrix,
        IReadOnlyList<RenderedTextBlock> textBlocks,
        double widthPt,
        double heightPt)
    {
        var sb = new StringBuilder();
        sb.AppendFormat(CultureInfo.InvariantCulture, "1 1 1 rg 0 0 {0:F3} {1:F3} re f\n", widthPt, heightPt);
        sb.AppendLine("0 0 0 rg");
        AppendRotation(sb, template.RotationDegrees, widthPt, heightPt);

        AppendMatrix(sb, template, matrix, heightPt);
        AppendText(sb, textBlocks, heightPt);

        if (template.RotationDegrees != 0)
            sb.AppendLine("Q");

        return sb.ToString();
    }

    private static void AppendMatrix(StringBuilder sb, PrintTemplate template, BitMatrix matrix, double pageHeightPt)
    {
        var left = MmToPt(template.DataMatrixXmm);
        var top = MmToPt(template.DataMatrixYmm);
        var width = MmToPt(template.DataMatrixWidthMm);
        var height = MmToPt(template.DataMatrixHeightMm);
        var cellW = width / matrix.Width;
        var cellH = height / matrix.Height;

        for (var y = 0; y < matrix.Height; y++)
        {
            var x = 0;
            while (x < matrix.Width)
            {
                while (x < matrix.Width && !matrix[x, y])
                    x++;
                if (x >= matrix.Width)
                    break;

                var start = x;
                while (x < matrix.Width && matrix[x, y])
                    x++;

                var run = x - start;
                var rx = left + start * cellW;
                var ry = pageHeightPt - top - (y + 1) * cellH;
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2:F3} {3:F3} re f\n",
                    rx, ry, run * cellW, cellH);
            }
        }
    }

    private static void AppendText(StringBuilder sb, IReadOnlyList<RenderedTextBlock> textBlocks, double pageHeightPt)
    {
        foreach (var block in textBlocks)
        {
            var x = MmToPt(block.Xmm);
            var y = pageHeightPt - MmToPt(block.Ymm) - block.FontSizePt;
            sb.AppendFormat(CultureInfo.InvariantCulture, "BT /{0} {1:F2} Tf {2:F3} {3:F3} Td ({4}) Tj ET\n",
                block.Bold ? "F2" : "F1",
                block.FontSizePt,
                x,
                y,
                EscapePdfText(ToPdfAscii(block.Text)));
        }
    }

    private static void AppendRotation(StringBuilder sb, int rotation, double widthPt, double heightPt)
    {
        if (rotation == 0)
            return;

        var radians = rotation * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var cx = widthPt / 2.0;
        var cy = heightPt / 2.0;
        var e = cx - cos * cx + sin * cy;
        var f = cy - sin * cx - cos * cy;
        sb.AppendFormat(CultureInfo.InvariantCulture, "q {0:F6} {1:F6} {2:F6} {3:F6} {4:F3} {5:F3} cm\n",
            cos, sin, -sin, cos, e, f);
    }

    private static string EscapePdfText(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static string ToPdfAscii(string text)
    {
        var chars = text.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').ToArray();
        return new string(chars);
    }

    private static double MmToPt(double mm) => mm * 72.0 / 25.4;

    private static void WriteAscii(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes);
    }
}

internal sealed record RenderedTextBlock(
    string Text,
    double Xmm,
    double Ymm,
    double FontSizePt,
    bool Bold);
