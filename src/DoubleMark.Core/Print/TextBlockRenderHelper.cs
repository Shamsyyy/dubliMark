namespace DoubleMark.Core.Print;

public static class TextBlockRenderHelper
{
    private const int GlyphRows = 7;
    private const int GlyphWidth = 3;

    private enum GlyphRotation
    {
        None,
        Clockwise90,
        CounterClockwise90
    }

    private enum TextPaintMode
    {
        HorizontalLtr,
        HorizontalRtl,
        VerticalTtbFaceLeft,
        VerticalTtbFaceRight,
        VerticalBttFaceLeft,
        VerticalBttFaceRight
    }

    public static (double WidthMm, double HeightMm) MeasureBlockMm(
        string text,
        double fontSizePt,
        bool bold,
        TextBlockLayout layout,
        TextFlowDirection flow,
        int dpi = 300)
    {
        var mode = ResolvePaintMode(layout, flow);
        if (IsVerticalRun(mode))
        {
            var runPx = MeasureVerticalRunPx(text, fontSizePt, bold, dpi);
            var glyphW = MeasureVerticalGlyphWidthPx(fontSizePt, dpi);
            return (glyphW * 25.4 / dpi, runPx * 25.4 / dpi);
        }

        var horizontalRunPx = TextRenderMetrics.MeasureTextRunPx(text, fontSizePt, bold, dpi);
        var glyphH = TextRenderMetrics.MeasureGlyphHeightPx(fontSizePt, dpi);
        return (horizontalRunPx * 25.4 / dpi, glyphH * 25.4 / dpi);
    }

    public static (double WidthMm, double HeightMm) MeasureBlockMm(
        string text,
        double fontSizePt,
        bool bold,
        TextBlockLayout? layout,
        TextFlowDirection? flow,
        TextBlockDirection? legacyOrientation = null,
        int dpi = 300)
    {
        var (l, f) = TextBlockStyleHelper.GetStyle(layout, flow, legacyOrientation);
        return MeasureBlockMm(text, fontSizePt, bold, l, f, dpi);
    }

    public static byte[] RenderSnippetPng(
        string text,
        double fontSizePt,
        bool bold,
        TextBlockLayout layout,
        TextFlowDirection flow,
        int dpi = 300)
    {
        var (widthMm, heightMm) = MeasureBlockMm(text, fontSizePt, bold, layout, flow, dpi);
        var widthPx = Math.Max(1, (int)Math.Ceiling(widthMm * dpi / 25.4));
        var heightPx = Math.Max(1, (int)Math.Ceiling(heightMm * dpi / 25.4));
        var rgb = Enumerable.Repeat((byte)255, widthPx * heightPx * 3).ToArray();
        PaintBlock(rgb, widthPx, heightPx, text, fontSizePt, bold, layout, flow, dpi, 0, 0);
        return LabelPngWriter.EncodeRgb(widthPx, heightPx, rgb, dpi);
    }

    public static void PaintBlock(
        byte[] rgb,
        int canvasWidth,
        int canvasHeight,
        string text,
        double fontSizePt,
        bool bold,
        TextBlockLayout layout,
        TextFlowDirection flow,
        int dpi,
        int leftPx,
        int topPx)
    {
        var scaleFactor = TextRenderMetrics.GetScaleFactor(fontSizePt, dpi);
        var paintScale = TextRenderMetrics.GetScale(fontSizePt, dpi);
        var chars = text.ToUpperInvariant().Select(TextRenderMetrics.NormalizeChar).ToList();
        if (chars.Count == 0)
            return;

        switch (ResolvePaintMode(layout, flow))
        {
            case TextPaintMode.HorizontalRtl:
            {
                var x = leftPx + TextRenderMetrics.MeasureTextRunPx(text, fontSizePt, bold, dpi);
                foreach (var ch in chars)
                {
                    x -= TextRenderMetrics.AdvanceWidthPx(ch, scaleFactor, bold);
                    PaintGlyph(rgb, canvasWidth, canvasHeight, x, topPx, ch, paintScale, bold, GlyphRotation.None);
                }

                break;
            }
            case TextPaintMode.VerticalBttFaceRight:
                PaintVerticalRun(rgb, canvasWidth, canvasHeight, chars, leftPx, topPx, scaleFactor, paintScale, bold,
                    bottomToTop: true, GlyphRotation.Clockwise90, text, fontSizePt, dpi);
                break;
            case TextPaintMode.VerticalBttFaceLeft:
                PaintVerticalRun(rgb, canvasWidth, canvasHeight, chars, leftPx, topPx, scaleFactor, paintScale, bold,
                    bottomToTop: true, GlyphRotation.CounterClockwise90, text, fontSizePt, dpi);
                break;
            case TextPaintMode.VerticalTtbFaceRight:
                PaintVerticalRun(rgb, canvasWidth, canvasHeight, chars, leftPx, topPx, scaleFactor, paintScale, bold,
                    bottomToTop: false, GlyphRotation.Clockwise90, text, fontSizePt, dpi);
                break;
            case TextPaintMode.VerticalTtbFaceLeft:
                PaintVerticalRun(rgb, canvasWidth, canvasHeight, chars, leftPx, topPx, scaleFactor, paintScale, bold,
                    bottomToTop: false, GlyphRotation.CounterClockwise90, text, fontSizePt, dpi);
                break;
            default:
            {
                var x = leftPx;
                foreach (var ch in chars)
                {
                    PaintGlyph(rgb, canvasWidth, canvasHeight, x, topPx, ch, paintScale, bold, GlyphRotation.None);
                    x += TextRenderMetrics.AdvanceWidthPx(ch, scaleFactor, bold);
                    if (x >= canvasWidth)
                        break;
                }

                break;
            }
        }
    }

    public static void PaintBlock(
        byte[] rgb,
        int canvasWidth,
        int canvasHeight,
        string text,
        double fontSizePt,
        bool bold,
        PrintTextBlock block,
        int dpi,
        int leftPx,
        int topPx)
    {
        var (layout, flow) = block.GetStyle();
        PaintBlock(rgb, canvasWidth, canvasHeight, text, fontSizePt, bold, layout, flow, dpi, leftPx, topPx);
    }

    private static TextPaintMode ResolvePaintMode(TextBlockLayout layout, TextFlowDirection flow)
    {
        if (layout == TextBlockLayout.Horizontal)
        {
            return flow switch
            {
                TextFlowDirection.Left => TextPaintMode.HorizontalRtl,
                TextFlowDirection.Up => TextPaintMode.VerticalBttFaceRight,
                TextFlowDirection.Down => TextPaintMode.VerticalTtbFaceLeft,
                _ => TextPaintMode.HorizontalLtr
            };
        }

        return flow switch
        {
            TextFlowDirection.Up => TextPaintMode.VerticalBttFaceRight,
            TextFlowDirection.Right => TextPaintMode.VerticalTtbFaceRight,
            TextFlowDirection.Left => TextPaintMode.VerticalBttFaceLeft,
            _ => TextPaintMode.VerticalTtbFaceLeft
        };
    }

    private static bool IsVerticalRun(TextPaintMode mode) =>
        mode is not TextPaintMode.HorizontalLtr and not TextPaintMode.HorizontalRtl;

    private static void PaintVerticalRun(
        byte[] rgb,
        int canvasWidth,
        int canvasHeight,
        IReadOnlyList<char> chars,
        int leftPx,
        int topPx,
        double scaleFactor,
        int paintScale,
        bool bold,
        bool bottomToTop,
        GlyphRotation rotation,
        string text,
        double fontSizePt,
        int dpi)
    {
        if (bottomToTop)
        {
            var y = topPx + MeasureVerticalRunPx(text, fontSizePt, bold, dpi);
            foreach (var ch in chars)
            {
                y -= AdvanceVerticalPx(ch, scaleFactor, bold);
                PaintGlyph(rgb, canvasWidth, canvasHeight, leftPx, y, ch, paintScale, bold, rotation);
            }

            return;
        }

        var cursorY = topPx;
        foreach (var ch in chars)
        {
            PaintGlyph(rgb, canvasWidth, canvasHeight, leftPx, cursorY, ch, paintScale, bold, rotation);
            cursorY += AdvanceVerticalPx(ch, scaleFactor, bold);
            if (cursorY >= canvasHeight)
                break;
        }
    }

    private static int MeasureVerticalRunPx(string text, double fontSizePt, bool bold, int dpi)
    {
        var scale = TextRenderMetrics.GetScaleFactor(fontSizePt, dpi);
        var heightPx = 0;
        foreach (var rawCh in text.ToUpperInvariant())
            heightPx += AdvanceVerticalPx(TextRenderMetrics.NormalizeChar(rawCh), scale, bold);
        return Math.Max(1, heightPx);
    }

    private static int MeasureVerticalGlyphWidthPx(double fontSizePt, int dpi) =>
        Math.Max(1, GlyphRows * TextRenderMetrics.GetScale(fontSizePt, dpi));

    private static int AdvanceVerticalPx(char ch, double scale, bool bold) =>
        TextRenderMetrics.AdvanceWidthPx(ch, scale, bold);

    private static void PaintGlyph(
        byte[] rgb,
        int width,
        int height,
        int x,
        int y,
        char ch,
        int paintScale,
        bool bold,
        GlyphRotation rotation)
    {
        var glyph = TinyFont.GetGlyph(ch);
        for (var row = 0; row < glyph.Length; row++)
        {
            for (var col = 0; col < glyph[row].Length; col++)
            {
                if (glyph[row][col] != '1')
                    continue;

                GetScaledRect(x, y, col, row, paintScale, bold, rotation, out var px0, out var py0, out var px1, out var py1);
                FillRect(rgb, width, height, px0, py0, Math.Max(1, px1 - px0), Math.Max(1, py1 - py0));
            }
        }
    }

    private static void GetScaledRect(
        int x,
        int y,
        int col,
        int row,
        int paintScale,
        bool bold,
        GlyphRotation rotation,
        out int px0,
        out int py0,
        out int px1,
        out int py1)
    {
        switch (rotation)
        {
            case GlyphRotation.Clockwise90:
                px0 = x + (GlyphRows - 1 - row) * paintScale;
                py0 = y + col * paintScale;
                px1 = x + (GlyphRows - row) * paintScale;
                py1 = y + (col + 1) * paintScale + (bold ? 1 : 0);
                return;
            case GlyphRotation.CounterClockwise90:
                px0 = x + row * paintScale;
                py0 = y + (GlyphWidth - 1 - col) * paintScale;
                px1 = x + (row + 1) * paintScale;
                py1 = y + (GlyphWidth - col) * paintScale + (bold ? 1 : 0);
                return;
            default:
                px0 = x + col * paintScale;
                py0 = y + row * paintScale;
                px1 = x + (col + 1) * paintScale + (bold ? 1 : 0);
                py1 = y + (row + 1) * paintScale;
                return;
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

public static class PrintTextBlockStyleExtensions
{
    public static (TextBlockLayout Layout, TextFlowDirection Flow) GetStyle(this PrintTextBlock block) =>
        TextBlockStyleHelper.GetStyle(block.Layout, block.Flow, block.Orientation);
}
