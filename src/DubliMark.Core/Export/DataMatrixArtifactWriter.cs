using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;

namespace DubliMark.Core.Export;

public sealed class DataMatrixArtifactWriter : IDataMatrixArtifactWriter
{
    private const int PngSize = 320;
    private const int PdfMatrixSize = 160;

    public void WritePng(string payload, string path)
    {
        var matrix = CreateMatrix(payload, PngSize);
        PngWriter.Write(matrix, path);
    }

    public void WritePdf(string payload, MarkExportPdfInfo info, string path)
    {
        var matrix = CreateMatrix(payload, PdfMatrixSize);
        PdfWriter.Write(matrix, info, path);
    }

    private static BitMatrix CreateMatrix(string payload, int size)
    {
        var writer = new DataMatrixWriter();
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.CHARACTER_SET] = "ISO-8859-1",
            [EncodeHintType.GS1_FORMAT] = true,
            [EncodeHintType.MARGIN] = 1
        };

        return writer.encode(payload, BarcodeFormat.DATA_MATRIX, size, size, hints);
    }
}
