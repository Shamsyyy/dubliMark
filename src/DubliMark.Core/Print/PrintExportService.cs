using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DubliMark.Core.Print;

public sealed class PrintExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string DefaultPrintRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DubliMark", "prints");

    public PrintExportResult Save(PrintExportRequest request)
    {
        try
        {
            var root = string.IsNullOrWhiteSpace(request.PrintRoot) ? DefaultPrintRoot : request.PrintRoot!;
            var dayDir = Path.Combine(root, request.Render.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dayDir);

            var baseName = MakeUniqueBaseName(dayDir, request.Render);
            var files = new PrintFileSet
            {
                PdfPath = Path.Combine(dayDir, baseName + ".pdf"),
                PngPath = Path.Combine(dayDir, baseName + ".png"),
                JsonPath = Path.Combine(dayDir, baseName + ".json")
            };

            File.WriteAllBytes(files.PdfPath, request.Render.PdfBytes);
            File.WriteAllBytes(files.PngPath, request.Render.PngBytes);
            File.WriteAllText(files.JsonPath, BuildJson(request), Encoding.UTF8);

            return new PrintExportResult
            {
                Success = true,
                DirectoryPath = dayDir,
                Files = files
            };
        }
        catch (Exception ex)
        {
            return new PrintExportResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static string BuildJson(PrintExportRequest request)
    {
        var render = request.Render;
        var dto = new
        {
            timestamp = render.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            printerName = request.PrinterName,
            templateName = render.Template.Name,
            labelWidthMm = render.Template.LabelWidthMm,
            labelHeightMm = render.Template.LabelHeightMm,
            dataMatrixSizeMm = Math.Max(render.Template.DataMatrixWidthMm, render.Template.DataMatrixHeightMm),
            copies = request.Copies,
            source = render.Source,
            rawPayloadEscaped = render.RawPayloadEscaped,
            normalizedPayloadEscaped = render.NormalizedPayloadEscaped,
            rawHex = render.RawHex,
            gsCount = render.GsCount,
            hasAi01 = render.HasAi01,
            hasAi21 = render.HasAi21,
            hasAi91 = render.HasAi91,
            hasAi92 = render.HasAi92,
            hasAi93 = render.HasAi93,
            codeType = render.CodeType,
            gtin = render.Gtin,
            serial = render.Serial,
            ai91 = render.Ai91,
            ai92 = render.Ai92,
            ai93 = render.Ai93,
            printed = request.Printed,
            printError = request.PrintError
        };

        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    private static string MakeUniqueBaseName(string directory, MarkRenderResult render)
    {
        var prefix = render.Timestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var baseName = $"{prefix}_{SanitizeFileNamePart(render.Gtin)}_{SanitizeFileNamePart(render.Serial)}";
        var candidate = baseName;
        var index = 1;

        while (File.Exists(Path.Combine(directory, candidate + ".json"))
               || File.Exists(Path.Combine(directory, candidate + ".pdf"))
               || File.Exists(Path.Combine(directory, candidate + ".png")))
        {
            candidate = $"{baseName}_{index:000}";
            index++;
        }

        return candidate;
    }

    private static string SanitizeFileNamePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (char.IsControl(ch) || invalid.Contains(ch))
                sb.Append('_');
            else
                sb.Append(ch);
        }

        var safe = sb.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(safe))
            safe = "EMPTY";
        return safe.Length <= 80 ? safe : safe[..80];
    }
}
