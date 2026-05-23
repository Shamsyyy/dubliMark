using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using DoubleMark.Core.Print;

namespace DoubleMark.Desktop.Services;

public static class TemplatePreviewRenderer
{
    private const char Gs = (char)0x1D;
    private static readonly Gs1Parser Parser = new();
    private static readonly MarkRenderService RenderService = new();
    private static readonly string DemoRaw =
        $"010460000000000221PREVIEW01{Gs}91DEMO1{Gs}92DEMOPREVIEWCODE";

    public static ImageSource? TryRender(
        PrintTemplate template,
        bool showDate = true,
        bool showShipment = false,
        bool showOrder = false,
        string? shipmentNumber = null,
        string? orderNumber = null,
        ParseResult? scanResult = null,
        string? rawPayload = null,
        string source = "Preview",
        int dpi = 150)
    {
        ParseResult parse;
        string raw;

        if (scanResult is { IsValid: true, Code: not null } && !string.IsNullOrEmpty(rawPayload))
        {
            parse = scanResult;
            raw = rawPayload;
        }
        else
        {
            parse = Parser.Parse(DemoRaw);
            if (!parse.IsValid || parse.Code == null)
                return null;
            raw = DemoRaw;
        }

        try
        {
            var render = RenderService.Render(new MarkRenderRequest
            {
                RawPayload = raw,
                ParseResult = parse,
                Template = template,
                Source = source,
                Timestamp = DateTimeOffset.Now,
                Dpi = dpi,
                ShowDate = showDate,
                ShowShipment = showShipment,
                ShowOrder = showOrder,
                ShipmentNumber = shipmentNumber,
                OrderNumber = orderNumber
            });

            return CreateFrozenBitmap(render.PngBytes);
        }
        catch (Exception ex)
        {
            LoggingService.Warn("Templates", "Preview render failed: " + ex.Message);
            return null;
        }
    }

    public static byte[]? TryRenderPngBytes(
        PrintTemplate template,
        bool showDate = true,
        bool showShipment = false,
        bool showOrder = false,
        string? shipmentNumber = null,
        string? orderNumber = null,
        ParseResult? scanResult = null,
        string? rawPayload = null,
        string source = "Preview",
        int dpi = 200)
    {
        ParseResult parse;
        string raw;

        if (scanResult is { IsValid: true, Code: not null } && !string.IsNullOrEmpty(rawPayload))
        {
            parse = scanResult;
            raw = rawPayload;
        }
        else
        {
            parse = Parser.Parse(DemoRaw);
            if (!parse.IsValid || parse.Code == null)
                return null;
            raw = DemoRaw;
        }

        try
        {
            return RenderService.Render(new MarkRenderRequest
            {
                RawPayload = raw,
                ParseResult = parse,
                Template = template,
                Source = source,
                Timestamp = DateTimeOffset.Now,
                Dpi = dpi,
                ShowDate = showDate,
                ShowShipment = showShipment,
                ShowOrder = showOrder,
                ShipmentNumber = shipmentNumber,
                OrderNumber = orderNumber
            }).PngBytes;
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
}
