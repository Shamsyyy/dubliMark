using System.Text.Json.Serialization;

namespace DoubleMark.Core.Print;

public sealed record PrintTextBlock
{
    public string Text { get; init; } = "";
    public double Xmm { get; init; }
    public double Ymm { get; init; }
    public double FontSizePt { get; init; } = 6;
    public bool Bold { get; init; }
    public TextBlockDirection Orientation { get; init; } = TextBlockDirection.LeftToRight;
}

public sealed record PrintTemplate
{
    public string Name { get; init; } = "";
    public double LabelWidthMm { get; init; }
    public double LabelHeightMm { get; init; }
    public double DataMatrixWidthMm { get; init; }
    public double DataMatrixHeightMm { get; init; }
    public double DataMatrixXmm { get; init; }
    public double DataMatrixYmm { get; init; }
    public double MarginMm { get; init; }
    public int RotationDegrees { get; init; }
    public int DefaultCopies { get; init; } = 1;
    public List<PrintTextBlock> TextBlocks { get; init; } = new();

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Без названия" : Name;
}

public sealed record PrintTemplateSet
{
    public List<PrintTemplate> Templates { get; init; } = new();
}
