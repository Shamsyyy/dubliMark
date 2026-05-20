using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DoubleMark.Core.Parsing;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Views;

namespace DoubleMark.Desktop.Services;

public static class ScanHistoryPreviewService
{
    private static readonly Gs1Parser Parser = new();
    private static readonly MarkRenderService RenderService = new();

    public static ImageSource? TryCreatePreview(ScanHistoryItem item, PrintTemplateService templateService)
    {
        if (string.IsNullOrEmpty(item.RawPayload))
            return TryLoadPngFromExportFolder(item);

        var parse = Parser.Parse(item.RawPayload);
        if (!parse.IsValid || parse.Code == null)
            return TryLoadPngFromExportFolder(item);

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
            LoggingService.Warn("ScanHistory", "Preview render failed: " + ex.Message);
            return TryLoadPngFromExportFolder(item);
        }
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
