using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DoubleMark.Core.Print;

namespace DoubleMark.Desktop.Services;

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

    public Task<PrintJobResult> PrintAsync(PrintJobRequest request, CancellationToken cancellationToken = default) =>
        RunOnUiThread(() => PrintSingle(request, cancellationToken));

    public Task<PrintJobResult> PrintBatchAsync(
        PrintBatchJobRequest request,
        CancellationToken cancellationToken = default) =>
        RunOnUiThread(() => PrintBatch(request, cancellationToken));

    private static PrintJobResult PrintSingle(PrintJobRequest request, CancellationToken cancellationToken)
    {
        if (request.Render.PngBytes.Length == 0)
            return new PrintJobResult { Success = false, Error = "Пустой макет печати" };

        try
        {
            var printDialog = CreatePrintDialog(request.Render.Template, request.PrinterName);
            if (!request.PrintWithoutConfirmation && printDialog.ShowDialog() != true)
                return new PrintJobResult { Success = false, Error = "Печать отменена" };

            var copies = Math.Max(1, request.Copies);
            for (var i = 0; i < copies; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                printDialog.PrintVisual(BuildPrintVisual(request.Render), "DoubleMark " + request.Render.Template.Name);
            }

            return new PrintJobResult { Success = true };
        }
        catch (Exception ex)
        {
            return new PrintJobResult { Success = false, Error = ex.Message };
        }
    }

    private static PrintJobResult PrintBatch(PrintBatchJobRequest request, CancellationToken cancellationToken)
    {
        if (request.Renders.Count == 0)
            return new PrintJobResult { Success = false, Error = "Нет этикеток для печати" };

        try
        {
            var printDialog = CreatePrintDialog(request.Renders[0].Template, request.PrinterName);
            if (!request.PrintWithoutConfirmation && printDialog.ShowDialog() != true)
                return new PrintJobResult { Success = false, Error = "Печать отменена" };

            var document = BuildFixedDocument(request.Renders, cancellationToken);
            printDialog.PrintDocument(document.DocumentPaginator, request.JobName);
            return new PrintJobResult { Success = true };
        }
        catch (Exception ex)
        {
            return new PrintJobResult { Success = false, Error = ex.Message };
        }
    }

    private static PrintDialog CreatePrintDialog(PrintTemplate template, string? printerName)
    {
        var printDialog = new PrintDialog();
        if (!string.IsNullOrWhiteSpace(printerName))
            printDialog.PrintQueue = new PrintQueue(new PrintServer(), printerName);

        printDialog.PrintTicket.PageMediaSize = new PageMediaSize(
            MmToDip(template.LabelWidthMm),
            MmToDip(template.LabelHeightMm));
        return printDialog;
    }

    private static FixedDocument BuildFixedDocument(IReadOnlyList<MarkRenderResult> renders, CancellationToken cancellationToken)
    {
        var document = new FixedDocument();
        foreach (var render in renders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var width = MmToDip(render.Template.LabelWidthMm);
            var height = MmToDip(render.Template.LabelHeightMm);
            var page = new FixedPage
            {
                Width = width,
                Height = height,
                Background = Brushes.White
            };

            var image = BuildPrintImage(render);
            page.Children.Add(image);

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(page);
            document.Pages.Add(pageContent);
        }

        return document;
    }

    private static FrameworkElement BuildPrintVisual(MarkRenderResult render)
    {
        var image = BuildPrintImage(render);
        var width = MmToDip(render.Template.LabelWidthMm);
        var height = MmToDip(render.Template.LabelHeightMm);
        image.Measure(new Size(width, height));
        image.Arrange(new Rect(0, 0, width, height));
        image.UpdateLayout();
        return image;
    }

    private static Image BuildPrintImage(MarkRenderResult render)
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
        return new Image
        {
            Source = bitmap,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill
        };
    }

    private static Task<PrintJobResult> RunOnUiThread(Func<PrintJobResult> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            return Task.FromResult(action());

        return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
    }

    private static double MmToDip(double mm) => mm * 96.0 / 25.4;
}
