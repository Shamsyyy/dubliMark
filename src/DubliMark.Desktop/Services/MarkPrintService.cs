using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DubliMark.Core.Print;

namespace DubliMark.Desktop.Services;

public sealed class MarkPrintService : IMarkPrintService
{
    public MarkPrintService(Window owner)
    {
    }

    public static IReadOnlyList<string> GetInstalledPrinters()
    {
        try
        {
            using var server = new LocalPrintServer();
            return server.GetPrintQueues()
                .Select(q => q.FullName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public Task<PrintJobResult> PrintAsync(PrintJobRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Render.PngBytes.Length == 0)
            return Task.FromResult(new PrintJobResult { Success = false, Error = "Пустой макет печати" });

        try
        {
            var printDialog = new PrintDialog();
            if (!string.IsNullOrWhiteSpace(request.PrinterName))
                printDialog.PrintQueue = new PrintQueue(new PrintServer(), request.PrinterName);

            printDialog.PrintTicket.PageMediaSize = new PageMediaSize(
                MmToDip(request.Render.Template.LabelWidthMm),
                MmToDip(request.Render.Template.LabelHeightMm));

            if (!request.PrintWithoutConfirmation && printDialog.ShowDialog() != true)
                return Task.FromResult(new PrintJobResult { Success = false, Error = "Печать отменена" });

            var visual = BuildPrintVisual(request.Render);
            for (var i = 0; i < Math.Max(1, request.Copies); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                printDialog.PrintVisual(visual, "DubliMark " + request.Render.Template.Name);
            }

            return Task.FromResult(new PrintJobResult { Success = true });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new PrintJobResult { Success = false, Error = ex.Message });
        }
    }

    private static FrameworkElement BuildPrintVisual(MarkRenderResult render)
    {
        var bitmap = new BitmapImage();
        using (var ms = new MemoryStream(render.PngBytes))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
        }

        var width = MmToDip(render.Template.LabelWidthMm);
        var height = MmToDip(render.Template.LabelHeightMm);
        var image = new Image
        {
            Source = bitmap,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill
        };
        image.Measure(new Size(width, height));
        image.Arrange(new Rect(0, 0, width, height));
        image.UpdateLayout();
        return image;
    }

    private static double MmToDip(double mm) => mm * 96.0 / 25.4;
}
