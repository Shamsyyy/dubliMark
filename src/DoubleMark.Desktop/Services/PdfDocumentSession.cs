using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;

namespace DoubleMark.Desktop.Services;

public enum PdfRenderProfile
{
    Fast,
    Thorough
}

public sealed class PdfDocumentSession : IDisposable
{
    private const int FastRenderWidthPx = 1400;
    private const int ThoroughRenderWidthPx = 2800;

    private readonly IDocReader _docReader;

    private PdfDocumentSession(byte[] pdfBytes, int renderWidthPx)
    {
        _docReader = DocLib.Instance.GetDocReader(
            pdfBytes,
            new PageDimensions(renderWidthPx, renderWidthPx * 2));
    }

    public int PageCount => _docReader.GetPageCount();

    public static PdfDocumentSession OpenFile(string pdfPath, PdfRenderProfile profile)
    {
        var bytes = File.ReadAllBytes(pdfPath);
        return OpenBytes(bytes, profile);
    }

    public static async Task<PdfDocumentSession> OpenFileAsync(string pdfPath, PdfRenderProfile profile, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken).ConfigureAwait(false);
        return OpenBytes(bytes, profile);
    }

    public static PdfDocumentSession OpenBytes(byte[] pdfBytes, PdfRenderProfile profile) =>
        new(pdfBytes, profile == PdfRenderProfile.Fast ? FastRenderWidthPx : ThoroughRenderWidthPx);

    public BitmapSource RenderPage(int pageIndexZeroBased)
    {
        using var pageReader = _docReader.GetPageReader(pageIndexZeroBased);
        var width = pageReader.GetPageWidth();
        var height = pageReader.GetPageHeight();
        var pixels = pageReader.GetImage();
        var stride = width * 4;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose() => _docReader.Dispose();
}
