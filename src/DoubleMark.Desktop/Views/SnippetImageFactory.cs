using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DoubleMark.Core.Print;

namespace DoubleMark.Desktop.Views;

internal static class SnippetImageFactory
{
    public static ImageSource Create(
        string text,
        double fontSizePt,
        bool bold,
        TextBlockLayout layout,
        TextFlowDirection flow,
        int dpi)
    {
        var png = TextBlockRenderHelper.RenderSnippetPng(text, fontSizePt, bold, layout, flow, dpi);
        using var ms = new MemoryStream(png);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
