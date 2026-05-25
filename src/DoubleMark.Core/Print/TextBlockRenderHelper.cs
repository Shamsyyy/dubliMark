namespace DoubleMark.Core.Print;

/// <summary>
/// Text flow on the label. Values 0–1 are legacy horizontal/vertical; 2–3 add reversed horizontal and vertical facing right.
/// </summary>
public enum TextBlockDirection
{
    /// <summary>Horizontal, left to right.</summary>
    LeftToRight = 0,

    /// <summary>Vertical, top to bottom, glyphs face left.</summary>
    TopToBottom = 1,

    /// <summary>Horizontal, right to left.</summary>
    RightToLeft = 2,

    /// <summary>Vertical, bottom to top, glyphs face right.</summary>
    BottomToTop = 3
}

public static class TextBlockDirectionHelper
{
    public static bool IsVertical(TextBlockDirection direction) =>
        direction is TextBlockDirection.TopToBottom or TextBlockDirection.BottomToTop;

    public static bool IsHorizontal(TextBlockDirection direction) => !IsVertical(direction);

    public static TextBlockDirection DefaultForLayout(bool vertical) =>
        vertical ? TextBlockDirection.TopToBottom : TextBlockDirection.LeftToRight;

    public static TextBlockDirection ToggleLayout(TextBlockDirection current) =>
        IsVertical(current) ? TextBlockDirection.LeftToRight : TextBlockDirection.TopToBottom;
}

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

    public static bool IsVertical(TextBlockDirection direction) =>
        TextBlockDirectionHelper.IsVertical(direction);

    public static (double WidthMm, double HeightMm) MeasureBlockMm(
        string text,
        double fontSizePt,
        bool bold,
        TextBlockDirection direction,
        int dpi = 300)
    {
        if (IsVertical(direction))
        {
            var runPx = MeasureVerticalRunPx(text, fontSizePt, bold, dpi);
            var glyphW = MeasureVerticalGlyphWidthPx(fontSizePt, dpi);
            return (glyphW * 25.4 / dpi, runPx * 25.4 / dpi);
        }

        var horizontalRunPx = TextRenderMetrics.MeasureTextRunPx(text, fontSizePt, bold, dpi);
        var glyphH = TextRenderMetrics.MeasureGlyphHeightPx(fontSizePt, dpi);
        return (horizontalRunPx * 25.4 / dpi, glyphH * 25.4 / dpi);
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
                    x -= TextRenderMetrics.AdvanceWidthPx(ch, scale, bold);
                    PaintGlyph(rgb, canvasWidth, canvasHeight, x, topPx, ch, scale, bold, GlyphRotation.None);
                }

                break;
            }
            case TextBlockDirection.BottomToTop:
            {
                var y = topPx + MeasureVerticalRunPx(text, fontSizePt, bold, dpi);
                foreach (var ch in chars)
                {
                    y -= AdvanceVerticalPx(ch, scale, bold);
                    PaintGlyph(rgb, canvasWidth, canvasHeight, leftPx, y, ch, scale, bold, GlyphRotation.Clockwise90);
                }

                break;
            }
            case TextBlockDirection.TopToBottom:
            {
                var y = topPx;
                foreach (var ch in chars)
                {
                    PaintGlyph(rgb, canvasWidth, canvasHeight, leftPx, y, ch, scale, bold, GlyphRotation.CounterClockwise90);
                    y += AdvanceVerticalPx(ch, scale, bold);
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
                    PaintGlyph(rgb, canvasWidth, canvasHeight, x, topPx, ch, scale, bold, GlyphRotation.None);
                    x += TextRenderMetrics.AdvanceWidthPx(ch, scale, bold);
                    if (x >= canvasWidth)
                        break;
                }

                break;
            }
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

    private static int MeasureVerticalGlyphWidthPx(double fontSizePt, int dpi)
    {
        var scale = TextRenderMetrics.GetScaleFactor(fontSizePt, dpi);
        return Math.Max(1, (int)Math.Ceiling(GlyphRows * scale));
    }

    private static int AdvanceVerticalPx(char ch, double scale, bool bold) =>
        TextRenderMetrics.AdvanceWidthPx(ch, scale, bold);

    private static void PaintGlyph(
        byte[] rgb,
        int width,
        int height,
        int x,
        int y,
        char ch,
        double scale,
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

                GetScaledRect(x, y, col, row, scale, bold, rotation, out var px0, out var py0, out var px1, out var py1);
                FillRect(rgb, width, height, px0, py0, Math.Max(1, px1 - px0), Math.Max(1, py1 - py0));
            }
        }
    }

    private static void GetScaledRect(
        int x,
        int y,
        int col,
        int row,
        double scale,
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
                px0 = x + (int)Math.Floor((GlyphRows - 1 - row) * scale);
                py0 = y + (int)Math.Floor(col * scale);
                px1 = x + (int)Math.Floor((GlyphRows - row) * scale);
                py1 = y + (int)Math.Floor((col + 1) * scale + (bold ? 1 : 0));
                return;
            case GlyphRotation.CounterClockwise90:
                px0 = x + (int)Math.Floor(row * scale);
                py0 = y + (int)Math.Floor((GlyphWidth - 1 - col) * scale);
                px1 = x + (int)Math.Floor((row + 1) * scale);
                py1 = y + (int)Math.Floor((GlyphWidth - col) * scale + (bold ? 1 : 0));
                return;
            default:
                px0 = x + (int)Math.Floor(col * scale);
                py0 = y + (int)Math.Floor(row * scale);
                px1 = x + (int)Math.Floor((col + 1) * scale + (bold ? 1 : 0));
                py1 = y + (int)Math.Floor((row + 1) * scale);
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
