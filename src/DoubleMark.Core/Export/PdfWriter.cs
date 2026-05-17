using System.Globalization;
using System.Text;
using ZXing.Common;

namespace DoubleMark.Core.Export;

internal static class PdfWriter
{
    public static void Write(BitMatrix matrix, MarkExportPdfInfo info, string path)
    {
        var content = BuildContent(matrix, info);
        var contentBytes = Encoding.ASCII.GetBytes(content);
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {contentBytes.Length} >>\nstream\n{content}\nendstream"
        };

        using var stream = File.Create(path);
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
    }

    private static string BuildContent(BitMatrix matrix, MarkExportPdfInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine("1 1 1 rg 0 0 595 842 re f");
        sb.AppendLine("0 0 0 rg");

        const double left = 56;
        const double top = 600;
        const double size = 220;
        var cell = size / matrix.Width;

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
                var rx = left + start * cell;
                var ry = top - (y + 1) * cell;
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2:F3} {3:F3} re f\n",
                    rx, ry, run * cell, cell);
            }
        }

        var lines = new[]
        {
            "DoubleMark local export",
            "Timestamp: " + info.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            "Source: " + info.Source,
            "Type: " + info.CodeType,
            "GTIN: " + info.Gtin,
            "Serial: " + info.Serial,
            "GS count: " + info.GsCount.ToString(CultureInfo.InvariantCulture),
            "AI91: " + (info.HasAi91 ? "yes" : "no"),
            "AI92: " + (info.HasAi92 ? "yes, length " + info.Ai92Length.ToString(CultureInfo.InvariantCulture) : "no"),
            "AI93: " + (info.HasAi93 ? "yes" : "no")
        };

        sb.AppendLine("BT /F1 12 Tf 300 760 Td 14 TL");
        foreach (var line in lines)
            sb.AppendLine("(" + EscapePdfText(ToPdfAscii(line)) + ") Tj T*");
        sb.AppendLine("ET");

        return sb.ToString();
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

    private static void WriteAscii(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes);
    }
}
