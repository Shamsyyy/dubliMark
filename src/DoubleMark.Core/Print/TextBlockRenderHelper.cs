namespace DoubleMark.Core.Print;

public enum TextBlockDirection
{
    LeftToRight = 0,
    TopToBottom = 1,
    RightToLeft = 2,
    BottomToTop = 3
}

public static class TextBlockRenderHelper
{
    public static bool IsVertical(TextBlockDirection direction) =>
        direction is TextBlockDirection.TopToBottom or TextBlockDirection.BottomToTop;

    public static (double WidthMm, double HeightMm) MeasureBlockMm(
        string text,
        double fontSizePt,
        bool bold,
        TextBlockDirection direction,
        int dpi = 300)
    {
        var runPx = TextRenderMetrics.MeasureTextRunPx(text, fontSizePt, bold, dpi);
        var glyphW = TextRenderMetrics.MeasureGlyphWidthPx(fontSizePt, bold, dpi);
        var glyphH = TextRenderMetrics.MeasureGlyphHeightPx(fontSizePt, dpi);

        return IsVertical(direction)
            ? (glyphW * 25.4 / dpi, runPx * 25.4 / dpi)
            : (runPx * 25.4 / dpi, glyphH * 25.4 / dpi);
    }

    public static byte[] RenderSnippetPng(
        string text,
        double fontSizePt,
        bool bold,
        TextBlockDirection direction,
        int dpi = 300)
    {
        var (widthMm, heightMm) = MeasureBlockMm(text, fontSizePt, bold, direction, dpi);
        var widthPx = Math.Max(1, (int)Math.Ceiling(widthMm * dpi / 25.4));
        var heightPx = Math.Max(1, (int)Math.Ceiling(heightMm * dpi / 25.4));
        var rgb = Enumerable.Repeat((byte)255, widthPx * heightPx * 3).ToArray();
        PaintBlock(rgb, widthPx, heightPx, text, fontSizePt, bold, direction, dpi, 0, 0);
        return LabelPngWriter.EncodeRgb(widthPx, heightPx, rgb, dpi);
    }

    public static void PaintBlock(
        byte[] rgb,
        int canvasWidth,
        int canvasHeight,
        string text,
        double fontSizePt,
        bool bold,
        TextBlockDirection direction,
        int dpi,
        int leftPx,
        int topPx)
    {
        var scale = TextRenderMetrics.GetScaleFactor(fontSizePt, dpi);
        var chars = text.ToUpperInvariant().Select(TextRenderMetrics.NormalizeChar).ToList();
        if (chars.Count == 0)
            return;

        switch (direction)
        {
            case TextBlockDirection.RightToLeft:
            {
                var x = leftPx + TextRenderMetrics.MeasureTextRunPx(text, fontSizePt, bold, dpi);
                foreach (var ch in chars)
                {
                    x -= TinyFont.AdvanceWidthPx(ch, scale, bold);
                    PaintGlyph(rgb, canvasWidth, canvasHeight, x, topPx, ch, scale, bold);
                }

                break;
            }
            case TextBlockDirection.BottomToTop:
            {
                var y = topPx + TextRenderMetrics.MeasureTextRunPx(text, fontSizePt, bold, dpi);
                foreach (var ch in chars)
                {
                    y -= TinyFont.AdvanceWidthPx(ch, scale, bold);
                    PaintGlyph(rgb, canvasWidth, canvasHeight, leftPx, y, ch, scale, bold);
                }

                break;
            }
            case TextBlockDirection.TopToBottom:
            {
                var y = topPx;
                foreach (var ch in chars)
                {
                    PaintGlyph(rgb, canvasWidth, canvasHeight, leftPx, y, ch, scale, bold);
                    y += TinyFont.AdvanceWidthPx(ch, scale, bold);
                    if (y >= canvasHeight)
                        break;
                }

                break;
            }
            default:
            {
                var x = leftPx;
                foreach (var ch in chars)
                {
                    PaintGlyph(rgb, canvasWidth, canvasHeight, x, topPx, ch, scale, bold);
                    x += TinyFont.AdvanceWidthPx(ch, scale, bold);
                    if (x >= canvasWidth)
                        break;
                }

                break;
            }
        }
    }

    private static void PaintGlyph(byte[] rgb, int width, int height, int x, int y, char ch, double scale, bool bold)
    {
        var glyph = TinyFont.GetGlyph(ch);
        for (var row = 0; row < glyph.Length; row++)
        {
            for (var col = 0; col < glyph[row].Length; col++)
            {
                if (glyph[row][col] != '1')
                    continue;

                var px = x + (int)Math.Round(col * scale);
                var py = y + (int)Math.Round(row * scale);
                var w = Math.Max(1, (int)Math.Round(scale + (bold ? 1 : 0)));
                var h = Math.Max(1, (int)Math.Round(scale));
                FillRect(rgb, width, height, px, py, w, h);
            }
        }
    }

    private static void FillRect(byte[] rgb, int width, int height, int x, int y, int w, int h)
    {
        for (var py = y; py < y + h; py++)
        {
            if (py < 0 || py >= height)
                continue;

            for (var px = x; px < x + w; px++)
            {
                if (px < 0 || px >= width)
                    continue;

                var idx = (py * width + px) * 3;
                rgb[idx] = 0;
                rgb[idx + 1] = 0;
                rgb[idx + 2] = 0;
            }
        }
    }
}
