using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DoubleMark.Core.Print;

internal static class VectorLabelFontRenderer
{
    private const float LayoutPadPt = 1.0f;

    public static (double WidthMm, double HeightMm) MeasureBlockMm(
        string text,
        double fontSizePt,
        bool bold,
        LabelFontId fontId,
        TextBlockLayout layout,
        TextFlowDirection flow,
        int dpi = 300)
    {
        if (string.IsNullOrEmpty(text))
            return (0, 0);

        var layoutBox = BuildLayout(text, fontSizePt, bold, fontId, layout, flow);
        return (PtToMm(layoutBox.Width), PtToMm(layoutBox.Height));
    }

    public static byte[] RenderSnippetPng(
        string text,
        double fontSizePt,
        bool bold,
        LabelFontId fontId,
        TextBlockLayout layout,
        TextFlowDirection flow,
        int dpi = 300)
    {
        var layoutBox = BuildLayout(text, fontSizePt, bold, fontId, layout, flow);
        var widthPx = Math.Max(1, (int)Math.Ceiling(PtToPx(layoutBox.Width, dpi)));
        var heightPx = Math.Max(1, (int)Math.Ceiling(PtToPx(layoutBox.Height, dpi)));

        using var bitmap = new Bitmap(widthPx, heightPx, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        bitmap.SetResolution(dpi, dpi);
        using var graphics = CreateGraphics(bitmap, fontSizePt);
        DrawText(graphics, text, fontSizePt, bold, fontId, layout, flow, layoutBox);

        return BitmapToRgbPng(bitmap, dpi);
    }

    public static void PaintBlock(
        byte[] rgb,
        int canvasWidth,
        int canvasHeight,
        string text,
        double fontSizePt,
        bool bold,
        LabelFontId fontId,
        TextBlockLayout layout,
        TextFlowDirection flow,
        int dpi,
        int leftPx,
        int topPx)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var layoutBox = BuildLayout(text, fontSizePt, bold, fontId, layout, flow);
        var widthPx = Math.Max(1, (int)Math.Ceiling(PtToPx(layoutBox.Width, dpi)));
        var heightPx = Math.Max(1, (int)Math.Ceiling(PtToPx(layoutBox.Height, dpi)));

        using var bitmap = new Bitmap(widthPx, heightPx, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        bitmap.SetResolution(dpi, dpi);
        using var graphics = CreateGraphics(bitmap, fontSizePt);
        DrawText(graphics, text, fontSizePt, bold, fontId, layout, flow, layoutBox);

        BlitBitmap(rgb, canvasWidth, canvasHeight, bitmap, leftPx, topPx);
    }

    private static Graphics CreateGraphics(Bitmap bitmap, double fontSizePt)
    {
        var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.PageUnit = GraphicsUnit.Point;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = fontSizePt <= 4.5
            ? TextRenderingHint.SingleBitPerPixelGridFit
            : TextRenderingHint.AntiAliasGridFit;
        return graphics;
    }

    private static void DrawText(
        Graphics graphics,
        string text,
        double fontSizePt,
        bool bold,
        LabelFontId fontId,
        TextBlockLayout layout,
        TextFlowDirection flow,
        RectangleF layoutBox)
    {
        using var font = CreateFont(fontId, fontSizePt, bold);
        using var brush = new SolidBrush(Color.Black);
        using var format = CreateFormat();

        var mode = ResolvePaintMode(layout, flow);
        if (IsVerticalRun(mode))
        {
            var (rotateDeg, reverse) = GetVerticalTransform(mode);
            DrawRotatedRun(graphics, text, font, brush, format, rotateDeg, reverse, layoutBox);
            return;
        }

        if (mode == TextPaintMode.HorizontalRtl)
        {
            using var rtl = (StringFormat)format.Clone();
            rtl.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            graphics.DrawString(text, font, brush, LayoutPadPt, LayoutPadPt, rtl);
            return;
        }

        graphics.DrawString(text, font, brush, LayoutPadPt, LayoutPadPt, format);
    }

    private static void DrawRotatedRun(
        Graphics graphics,
        string text,
        Font font,
        Brush brush,
        StringFormat format,
        float rotateDeg,
        bool reverse,
        RectangleF layoutBox)
    {
        var content = reverse ? new string(text.Reverse().ToArray()) : text;
        using var path = CreateTextPath(content, font, format);
        using var rotate = new Matrix();
        rotate.Rotate(rotateDeg);
        path.Transform(rotate);

        var bounds = path.GetBounds();
        using var align = new Matrix();
        align.Translate(LayoutPadPt - bounds.X, LayoutPadPt - bounds.Y);
        path.Transform(align);

        graphics.FillPath(brush, path);
    }

    private static RectangleF BuildLayout(
        string text,
        double fontSizePt,
        bool bold,
        LabelFontId fontId,
        TextBlockLayout layout,
        TextFlowDirection flow)
    {
        using var font = CreateFont(fontId, fontSizePt, bold);
        using var format = CreateFormat();

        using var bitmap = new Bitmap(1, 1);
        using var graphics = CreateGraphics(bitmap, fontSizePt);

        var mode = ResolvePaintMode(layout, flow);
        if (IsVerticalRun(mode))
        {
            var (rotateDeg, reverse) = GetVerticalTransform(mode);
            var content = reverse ? new string(text.Reverse().ToArray()) : text;
            var bounds = MeasureRotatedBounds(graphics, content, font, format, rotateDeg);
            return new RectangleF(0, 0, bounds.Width + LayoutPadPt * 2, bounds.Height + LayoutPadPt * 2);
        }

        var size = graphics.MeasureString(text, font, new PointF(LayoutPadPt, LayoutPadPt), format);
        return new RectangleF(
            0,
            0,
            size.Width + LayoutPadPt,
            size.Height + LayoutPadPt);
    }

    private static RectangleF MeasureRotatedBounds(
        Graphics graphics,
        string text,
        Font font,
        StringFormat format,
        float rotateDeg)
    {
        using var path = CreateTextPath(text, font, format);
        using var rotate = new Matrix();
        rotate.Rotate(rotateDeg);
        path.Transform(rotate);
        return path.GetBounds();
    }

    private static GraphicsPath CreateTextPath(string text, Font font, StringFormat format)
    {
        var path = new GraphicsPath(FillMode.Winding);
        path.AddString(text, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, format);
        return path;
    }

    private static StringFormat CreateFormat()
    {
        var format = StringFormat.GenericTypographic;
        format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        format.Alignment = StringAlignment.Near;
        format.LineAlignment = StringAlignment.Near;
        return format;
    }

    private static Font CreateFont(LabelFontId fontId, double fontSizePt, bool bold)
    {
        var style = bold ? FontStyle.Bold : FontStyle.Regular;
        var size = (float)Math.Max(1.0, fontSizePt);
        var candidates = new[]
        {
            LabelFontRegistry.ResolveFamily(fontId),
            "Arial",
            "Verdana",
            "Segoe UI"
        };

        foreach (var familyName in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var family = new FontFamily(familyName);
                if (family.IsStyleAvailable(style))
                    return new Font(family, size, style, GraphicsUnit.Point);
            }
            catch (ArgumentException)
            {
                // try next family
            }
        }

        return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
    }

    private static (float RotateDeg, bool Reverse) GetVerticalTransform(TextPaintMode mode) => mode switch
    {
        TextPaintMode.VerticalTtbFaceRight => (90f, false),
        TextPaintMode.VerticalTtbFaceLeft => (-90f, false),
        TextPaintMode.VerticalBttFaceLeft => (90f, true),
        TextPaintMode.VerticalBttFaceRight => (-90f, true),
        _ => (-90f, false)
    };

    private static float PtToPx(float pt, int dpi) => pt * dpi / 72f;

    private static double PtToMm(float pt) => pt * 25.4 / 72.0;

    private static void BlitBitmap(byte[] rgb, int canvasWidth, int canvasHeight, Bitmap bitmap, int leftPx, int topPx)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            var targetY = topPx + y;
            if (targetY < 0 || targetY >= canvasHeight)
                continue;

            for (var x = 0; x < bitmap.Width; x++)
            {
                var targetX = leftPx + x;
                if (targetX < 0 || targetX >= canvasWidth)
                    continue;

                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R > 240 && pixel.G > 240 && pixel.B > 240)
                    continue;

                var idx = (targetY * canvasWidth + targetX) * 3;
                rgb[idx] = 0;
                rgb[idx + 1] = 0;
                rgb[idx + 2] = 0;
            }
        }
    }

    private static byte[] BitmapToRgbPng(Bitmap bitmap, int dpi)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var idx = (y * width + x) * 3;
                rgb[idx] = pixel.R;
                rgb[idx + 1] = pixel.G;
                rgb[idx + 2] = pixel.B;
            }
        }

        return LabelPngWriter.EncodeRgb(width, height, rgb, dpi);
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
            TextFlowDirection.Left => TextPaintMode.VerticalTtbFaceLeft,
            _ => TextPaintMode.VerticalTtbFaceLeft
        };
    }

    private static bool IsVerticalRun(TextPaintMode mode) =>
        mode is not TextPaintMode.HorizontalLtr and not TextPaintMode.HorizontalRtl;
}
