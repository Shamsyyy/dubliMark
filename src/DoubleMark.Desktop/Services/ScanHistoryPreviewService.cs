using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Views;
using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;

namespace DoubleMark.Desktop.Services;

public static class ScanHistoryPreviewService
{
    private const int DataMatrixPreviewPx = 96;
    private static readonly Gs1Parser Parser = new();
    private static readonly MarkRenderService RenderService = new();

    /// <summary>DataMatrix ЧЗ для миниатюры в истории (локально и облако).</summary>
    public static ImageSource? TryCreatePreview(ScanHistoryItem item, PrintTemplateService templateService)
    {
        if (string.IsNullOrEmpty(item.RawPayload))
            return TryLoadPngFromExportFolder(item);

        var parse = Parser.Parse(item.RawPayload);
        if (!parse.IsValid || parse.Code == null)
            return TryLoadPngFromExportFolder(item);

        try
        {
            var matrixPreview = TryCreateDataMatrixImage(parse, item.RawPayload);
            if (matrixPreview != null)
                return matrixPreview;
        }
        catch (Exception ex)
        {
            LoggingService.Warn("ScanHistory", "DataMatrix preview failed: " + ex.Message);
        }

        try
        {
            var templateName = item.Template is "—" or ""
                ? null
                : item.Template;
            var template = templateService.ResolveTemplate(templateName);

            var render = RenderService.Render(new MarkRenderRequest
            {
                RawPayload = item.RawPayload,
                ParseResult = parse,
                Source = item.Source,
                Template = template
            });

            return CreateFrozenBitmap(render.PngBytes);
        }
        catch (Exception ex)
        {
            LoggingService.Warn("ScanHistory", "Label preview render failed: " + ex.Message);
            return TryLoadPngFromExportFolder(item);
        }
    }

    private static ImageSource? TryCreateDataMatrixImage(ParseResult parse, string rawPayload)
    {
        var normalized = parse.Code!.RawData
                         ?? Gs1BarcodeEncoding.NormalizeForParse(rawPayload).Payload;
        if (string.IsNullOrEmpty(normalized))
            return null;

        var writer = new DataMatrixWriter();
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.CHARACTER_SET] = "ISO-8859-1",
            [EncodeHintType.GS1_FORMAT] = true,
            [EncodeHintType.MARGIN] = 1
        };
        var matrix = writer.encode(
            normalized,
            BarcodeFormat.DATA_MATRIX,
            DataMatrixPreviewPx,
            DataMatrixPreviewPx,
            hints);

        return CreateFrozenBitmapFromMatrix(matrix);
    }

    private static ImageSource CreateFrozenBitmapFromMatrix(BitMatrix matrix)
    {
        var width = matrix.Width;
        var height = matrix.Height;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * stride + x * 4;
                var value = matrix[x, y] ? (byte)0 : (byte)255;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource? TryLoadPngFromExportFolder(ScanHistoryItem item)
    {
        if (string.IsNullOrWhiteSpace(item.SavedFolder) || item.SavedFolder == "—")
            return null;

        try
        {
            if (!Directory.Exists(item.SavedFolder))
                return null;

            var png = Directory.EnumerateFiles(item.SavedFolder, "*.png")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault(path =>
                    !string.IsNullOrWhiteSpace(item.Serial)
                    && item.Serial != "—"
                    && Path.GetFileName(path).Contains(item.Serial, StringComparison.OrdinalIgnoreCase));

            png ??= Directory.EnumerateFiles(item.SavedFolder, "*.png")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            return png == null ? null : LoadBitmapFromFile(png);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource CreateFrozenBitmap(byte[] pngBytes)
    {
        using var ms = new MemoryStream(pngBytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource? LoadBitmapFromFile(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
