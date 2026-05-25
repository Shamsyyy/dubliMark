namespace DoubleMark.Core.Print;

public static class TemplateLayoutHelper
{
    public static readonly IReadOnlyList<(double Width, double Height, string Caption)> DmPresets =
    [
        (8, 8, "8×8"),
        (10, 10, "10×10"),
        (13, 13, "13×13"),
        (15, 15, "15×15"),
        (20, 30, "20×30"),
        (25, 25, "25×25")
    ];

    public static PrintTemplate ApplyDmSize(PrintTemplate template, double dmWidth, double dmHeight)
    {
        var updated = template with
        {
            DataMatrixWidthMm = dmWidth,
            DataMatrixHeightMm = dmHeight,
            DataMatrixXmm = Math.Max(0, (template.LabelWidthMm - dmWidth) / 2),
            DataMatrixYmm = Math.Max(0, (template.LabelHeightMm - dmHeight) / 2)
        };
        return RelayoutTextBlocks(ClampDataMatrixInLabel(updated));
    }

    public static PrintTemplate ClampDataMatrixInLabel(PrintTemplate template)
    {
        var labelW = Math.Max(0.1, template.LabelWidthMm);
        var labelH = Math.Max(0.1, template.LabelHeightMm);
        var dmWidth = Math.Min(Math.Max(0.1, template.DataMatrixWidthMm), labelW);
        var dmHeight = Math.Min(Math.Max(0.1, template.DataMatrixHeightMm), labelH);
        var maxX = Math.Max(0, labelW - dmWidth);
        var maxY = Math.Max(0, labelH - dmHeight);

        return template with
        {
            LabelWidthMm = labelW,
            LabelHeightMm = labelH,
            DataMatrixWidthMm = dmWidth,
            DataMatrixHeightMm = dmHeight,
            DataMatrixXmm = Math.Min(Math.Max(0, template.DataMatrixXmm), maxX),
            DataMatrixYmm = Math.Min(Math.Max(0, template.DataMatrixYmm), maxY)
        };
    }

    public static PrintTemplate NormalizeForRender(PrintTemplate template) =>
        ClampDataMatrixInLabel(template);

    public static PrintTemplate CreateFromDmPreset(
        string name,
        double dmWidth,
        double dmHeight,
        double? labelWidthMm = null,
        double? labelHeightMm = null)
    {
        var labelW = labelWidthMm ?? Math.Max(30, dmWidth + 10);
        var labelH = labelHeightMm ?? Math.Max(20, dmHeight + 6);
        var x = Math.Max(0, (labelW - dmWidth) / 2);
        var y = Math.Max(0, (labelH - dmHeight) / 2);

        var template = new PrintTemplate
        {
            Name = name,
            LabelWidthMm = labelW,
            LabelHeightMm = labelH,
            DataMatrixWidthMm = dmWidth,
            DataMatrixHeightMm = dmHeight,
            DataMatrixXmm = x,
            DataMatrixYmm = y,
            MarginMm = 1,
            RotationDegrees = 0,
            DefaultCopies = 1,
            TextBlocks =
            {
                new PrintTextBlock { Text = "GTIN {gtin}", Xmm = 0, Ymm = 0, FontSizePt = 4.5 },
                new PrintTextBlock { Text = "SN {serial}", Xmm = 0, Ymm = 0, FontSizePt = 4.5 },
                new PrintTextBlock { Text = "{date} {time}", Xmm = 0, Ymm = 0, FontSizePt = 4 }
            }
        };

        return RelayoutTextBlocks(template);
    }

    public static PrintTemplate RelayoutTextBlocks(PrintTemplate template)
    {
        if (template.TextBlocks.Count == 0)
            return template;

        var normalized = ClampDataMatrixInLabel(template);
        var occupied = new List<LayoutRect> { GetDataMatrixRect(normalized) };
        var relayouted = new List<PrintTextBlock>(normalized.TextBlocks.Count);

        foreach (var block in normalized.TextBlocks)
        {
            var placed = PlaceTextBlock(normalized, block, occupied, preserveManualPosition: true);
            relayouted.Add(placed);
            if (IsInsideLabel(normalized, placed))
                occupied.Add(GetTextRect(placed));
        }

        return normalized with { TextBlocks = relayouted };
    }

    /// <summary>Stacks text for print/preview using real glyph metrics (no overlap).</summary>
    public static PrintTemplate RelayoutTextBlocksForRender(PrintTemplate template, int dpi = 300)
    {
        if (template.TextBlocks.Count == 0)
            return template;

        var normalized = ClampDataMatrixInLabel(template);
        var occupied = new List<LayoutRect> { GetDataMatrixRect(normalized) };
        var relayouted = new List<PrintTextBlock>(normalized.TextBlocks.Count);
        const double margin = 0.8;
        const double gap = 0.35;

        var rightX = normalized.DataMatrixXmm + normalized.DataMatrixWidthMm + margin;
        var rightMaxW = Math.Max(1, normalized.LabelWidthMm - rightX - margin);
        var rightY = normalized.DataMatrixYmm + 0.3;

        var belowX = margin;
        var belowY = normalized.DataMatrixYmm + normalized.DataMatrixHeightMm + margin;
        var belowMaxW = Math.Max(1, normalized.LabelWidthMm - 2 * margin);

        foreach (var block in normalized.TextBlocks)
        {
            var placed = TryPlaceInColumn(
                normalized, block, occupied, dpi, rightX, ref rightY, rightMaxW, margin, gap)
                ?? TryPlaceInColumn(
                    normalized, block, occupied, dpi, belowX, ref belowY, belowMaxW, margin, gap);

            relayouted.Add(placed ?? block with { Xmm = -1, Ymm = -1 });
            if (placed != null && IsInsideLabel(normalized, placed, dpi))
                occupied.Add(GetTextRect(placed, dpi));
        }

        return normalized with { TextBlocks = relayouted };
    }

    private static PrintTextBlock? TryPlaceInColumn(
        PrintTemplate template,
        PrintTextBlock block,
        List<LayoutRect> occupied,
        int dpi,
        double x,
        ref double stackY,
        double maxWidthMm,
        double margin,
        double gap)
    {
        var textW = TextRenderMetrics.MeasureTextWidthMm(block.Text, block.FontSizePt, block.Bold, dpi);
        var textH = TextRenderMetrics.MeasureTextHeightMm(block.FontSizePt, dpi);
        if (textW > maxWidthMm + 0.05)
            return null;

        var y = stackY;
        while (y + textH <= template.LabelHeightMm + 0.05)
        {
            var candidate = block with { Xmm = RoundMm(x), Ymm = RoundMm(y) };
            var rect = GetTextRect(candidate, dpi);
            if (rect.X + rect.W <= template.LabelWidthMm + margin
                && !Intersects(GetDataMatrixRect(template), rect)
                && !occupied.Any(o => Intersects(o, rect)))
            {
                stackY = rect.Y + rect.H + gap;
                return candidate;
            }

            y += textH + gap;
        }

        return null;
    }

    public static string CreateUniqueName(IEnumerable<PrintTemplate> templates, string baseName)
    {
        var names = templates.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName))
            return baseName;

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!names.Contains(candidate))
                return candidate;
        }

        return baseName + " " + Guid.NewGuid().ToString("N")[..4];
    }

    public static IReadOnlyList<PrintTextBlock> BuildEffectiveTextBlocks(
        PrintTemplate template,
        bool showDate,
        bool showShipment,
        bool showOrder)
    {
        var normalized = ClampDataMatrixInLabel(template);
        var blocks = normalized.TextBlocks.Where(b => !IsExtraBlock(b, normalized.LabelHeightMm)).ToList();

        if (showDate && !blocks.Any(b => b.Text.Contains("{date}", StringComparison.Ordinal)))
            blocks.Add(new PrintTextBlock { Text = "{date} {time}", FontSizePt = 4 });

        if (showShipment && !blocks.Any(b => b.Text.Contains("{shipment}", StringComparison.Ordinal)))
            blocks.Add(new PrintTextBlock { Text = "OTGR {shipment}", FontSizePt = 4 });

        if (showOrder && !blocks.Any(b => b.Text.Contains("{order}", StringComparison.Ordinal)))
            blocks.Add(new PrintTextBlock { Text = "ORD {order}", FontSizePt = 4 });

        return blocks;
    }

    public static bool IntersectsDataMatrix(PrintTemplate template, PrintTextBlock block, int dpi = 300)
    {
        if (!IsInsideLabel(template, block, dpi))
            return false;

        return Intersects(GetDataMatrixRect(ClampDataMatrixInLabel(template)), GetTextRect(block, dpi));
    }

    public static bool IsInsideLabel(PrintTemplate template, PrintTextBlock block, int dpi = 300)
    {
        if (block.Xmm < 0 || block.Ymm < 0)
            return false;

        var rect = GetTextRect(block, dpi);
        return rect.X + rect.W <= template.LabelWidthMm + 0.05
               && rect.Y + rect.H <= template.LabelHeightMm + 0.05;
    }

    public static (double WidthMm, double HeightMm) MeasureTextBlockMm(PrintTextBlock block, int dpi = 300)
    {
        var (layout, flow) = block.GetStyle();
        return TextBlockRenderHelper.MeasureBlockMm(block.Text, block.FontSizePt, block.Bold, layout, flow, dpi);
    }

    private static PrintTextBlock PlaceDynamicBlock(
        PrintTemplate template,
        string text,
        double preferredY,
        List<LayoutRect> occupied)
    {
        var block = new PrintTextBlock
        {
            Text = text,
            Xmm = 0.8,
            Ymm = Math.Max(0, preferredY),
            FontSizePt = 4
        };
        return PlaceTextBlock(template, block, occupied, preserveManualPosition: false);
    }

    private static PrintTextBlock PlaceTextBlock(
        PrintTemplate template,
        PrintTextBlock block,
        IReadOnlyList<LayoutRect> occupied,
        bool preserveManualPosition)
    {
        if (preserveManualPosition && IsInsideLabel(template, block))
            return block;

        var textW = TextRenderMetrics.MeasureTextWidthMm(block.Text, block.FontSizePt, block.Bold);
        var textH = TextRenderMetrics.MeasureTextHeightMm(block.FontSizePt);
        var slots = new (double X, double Y)[]
        {
            (template.DataMatrixXmm + template.DataMatrixWidthMm + 0.8, template.DataMatrixYmm + 0.5),
            (0.8, template.DataMatrixYmm + template.DataMatrixHeightMm + 0.8),
            (0.8, Math.Max(0, template.DataMatrixYmm - textH - 0.5)),
            (0.8, Math.Max(0, template.LabelHeightMm - textH - 0.5)),
            (Math.Max(0, template.LabelWidthMm - textW - 0.5), Math.Max(0, template.LabelHeightMm - textH - 0.5)),
            (block.Xmm, block.Ymm)
        };

        foreach (var (x, y) in slots)
        {
            var candidate = block with { Xmm = RoundMm(x), Ymm = RoundMm(y) };
            var rect = GetTextRect(candidate);
            if (!FitsInLabel(template, rect))
                continue;
            if (Intersects(GetDataMatrixRect(template), rect))
                continue;
            if (occupied.Any(o => Intersects(o, rect)))
                continue;

            return candidate;
        }

        return block with { Xmm = -1, Ymm = -1 };
    }

    private readonly record struct LayoutRect(double X, double Y, double W, double H);

    private static LayoutRect GetDataMatrixRect(PrintTemplate template) =>
        new(template.DataMatrixXmm, template.DataMatrixYmm, template.DataMatrixWidthMm, template.DataMatrixHeightMm);

    private static LayoutRect GetTextRect(PrintTextBlock block, int dpi = 300)
    {
        var (layout, flow) = block.GetStyle();
        var (w, h) = TextBlockRenderHelper.MeasureBlockMm(block.Text, block.FontSizePt, block.Bold, layout, flow, dpi);
        return new(block.Xmm, block.Ymm, w, h);
    }

    private static bool FitsInLabel(PrintTemplate template, LayoutRect rect) =>
        rect.X >= 0 && rect.Y >= 0
        && rect.X + rect.W <= template.LabelWidthMm + 0.05
        && rect.Y + rect.H <= template.LabelHeightMm + 0.05;

    private static bool IsInsideLabel(PrintTextBlock block) => block.Xmm >= 0 && block.Ymm >= 0;

    private static bool Intersects(LayoutRect a, LayoutRect b) =>
        a.X < b.X + b.W && a.X + a.W > b.X && a.Y < b.Y + b.H && a.Y + a.H > b.Y;

    private static double EstimateTextHeightMm(PrintTextBlock block)
    {
        var (layout, flow) = block.GetStyle();
        return TextBlockRenderHelper.MeasureBlockMm(block.Text, block.FontSizePt, block.Bold, layout, flow).HeightMm;
    }

    private static double EstimateTextWidthMm(PrintTextBlock block)
    {
        var (layout, flow) = block.GetStyle();
        return TextBlockRenderHelper.MeasureBlockMm(block.Text, block.FontSizePt, block.Bold, layout, flow).WidthMm;
    }

    private static double RoundMm(double value) => Math.Round(value, 1);

    private static bool IsExtraBlock(PrintTextBlock block, double labelHeightMm) =>
        block.Text.Contains("{shipment}", StringComparison.Ordinal)
        || block.Text.Contains("{order}", StringComparison.Ordinal)
        || (block.Text.Contains("{date}", StringComparison.Ordinal) && block.Ymm >= labelHeightMm - 5);
}
