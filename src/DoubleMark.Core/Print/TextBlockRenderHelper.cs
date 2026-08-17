namespace DoubleMark.Core.Print;

public static class TextBlockRenderHelper
{
    public static (double WidthMm, double HeightMm) MeasureBlockMm(
        string text,
        double fontSizePt,
        bool bold,
        LabelFontId fontId,
        TextBlockLayout layout,
        TextFlowDirection flow,
        int dpi = 300) =>
        VectorLabelFontRenderer.MeasureBlockMm(text, fontSizePt, bold, fontId, layout, flow, dpi);

    public static (double WidthMm, double HeightMm) MeasureBlockMm(
        string text,
        double fontSizePt,
        bool bold,
        TextBlockLayout layout,
        TextFlowDirection flow,
        int dpi = 300) =>
        MeasureBlockMm(text, fontSizePt, bold, LabelFontId.ArialIndustrial, layout, flow, dpi);

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
        return MeasureBlockMm(text, fontSizePt, bold, LabelFontId.ArialIndustrial, l, f, dpi);
    }

    public static (double WidthMm, double HeightMm) MeasureBlockMm(
        string text,
        double fontSizePt,
        bool bold,
        LabelFontId fontId,
        TextBlockLayout? layout,
        TextFlowDirection? flow,
        TextBlockDirection? legacyOrientation = null,
        int dpi = 300)
    {
        var (l, f) = TextBlockStyleHelper.GetStyle(layout, flow, legacyOrientation);
        return MeasureBlockMm(text, fontSizePt, bold, fontId, l, f, dpi);
    }

    public static byte[] RenderSnippetPng(
        string text,
        double fontSizePt,
        bool bold,
        LabelFontId fontId,
        TextBlockLayout layout,
        TextFlowDirection flow,
        int dpi = 300) =>
        VectorLabelFontRenderer.RenderSnippetPng(text, fontSizePt, bold, fontId, layout, flow, dpi);

    public static byte[] RenderSnippetPng(
        string text,
        double fontSizePt,
        bool bold,
        TextBlockLayout layout,
        TextFlowDirection flow,
        int dpi = 300) =>
        RenderSnippetPng(text, fontSizePt, bold, LabelFontId.ArialIndustrial, layout, flow, dpi);

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
        int topPx) =>
        VectorLabelFontRenderer.PaintBlock(
            rgb, canvasWidth, canvasHeight, text, fontSizePt, bold, fontId, layout, flow, dpi, leftPx, topPx);

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
        int topPx) =>
        PaintBlock(rgb, canvasWidth, canvasHeight, text, fontSizePt, bold, LabelFontId.ArialIndustrial, layout, flow, dpi, leftPx, topPx);

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
        PaintBlock(rgb, canvasWidth, canvasHeight, text, fontSizePt, bold, block.FontId, layout, flow, dpi, leftPx, topPx);
    }
}

public static class PrintTextBlockStyleExtensions
{
    public static (TextBlockLayout Layout, TextFlowDirection Flow) GetStyle(this PrintTextBlock block) =>
        TextBlockStyleHelper.GetStyle(block.Layout, block.Flow, block.Orientation);
}
