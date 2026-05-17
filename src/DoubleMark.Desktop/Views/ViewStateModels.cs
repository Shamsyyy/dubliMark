using System.Windows.Media;

namespace DoubleMark.Desktop.Views;

public enum UiStatusKind
{
    Neutral,
    Success,
    Warning,
    Error
}

public sealed record ScanViewState
{
    public string ScannerStatus { get; init; } = "Ожидание";
    public UiStatusKind ScannerStatusKind { get; init; } = UiStatusKind.Neutral;
    public string Mode { get; init; } = "Не выбран";
    public string? SelectedPort { get; init; }
    public IReadOnlyList<string> Ports { get; init; } = Array.Empty<string>();
    public string PortHint { get; init; } = "";
    public string WaitText { get; init; } = "Ожидание сканирования...";
    public string ValidationStatus { get; init; } = "Ожидание";
    public UiStatusKind ValidationKind { get; init; } = UiStatusKind.Neutral;
    public string Gtin { get; init; } = "—";
    public string Serial { get; init; } = "—";
    public string Ai91 { get; init; } = "—";
    public string Ai92 { get; init; } = "—";
    public string Ai93 { get; init; } = "—";
    public string GsCount { get; init; } = "—";
    public string Source { get; init; } = "—";
    public string CodeType { get; init; } = "—";
    public string RawEscaped { get; init; } = "—";
    public string RawPayload { get; init; } = "";
    public string NormalizedEscaped { get; init; } = "—";
    public string RawHex { get; init; } = "—";
    public string ErrorText { get; init; } = "";
    public ImageSource? PreviewImage { get; init; }
}

public sealed record PrintViewState
{
    public bool AutoPrintEnabled { get; init; }
    public IReadOnlyList<string> Printers { get; init; } = Array.Empty<string>();
    public string? SelectedPrinter { get; init; }
    public IReadOnlyList<string> Templates { get; init; } = Array.Empty<string>();
    public string? SelectedTemplate { get; init; }
    public int Copies { get; init; } = 1;
    public string PrintFolder { get; init; } = "";
    public string LastPrintStatus { get; init; } = "";
    public string QueueStatus { get; init; } = "Нет активных заданий";
    public string TemplateSize { get; init; } = "—";
    public string DataMatrixSize { get; init; } = "—";
    public double LabelWidthMm { get; init; }
    public double LabelHeightMm { get; init; }
    public double DataMatrixWidthMm { get; init; }
    public double DataMatrixHeightMm { get; init; }
    public double DataMatrixXmm { get; init; }
    public double DataMatrixYmm { get; init; }
    public ImageSource? PreviewImage { get; init; }
}

public sealed record TemplateViewState
{
    public IReadOnlyList<TemplateViewItem> Templates { get; init; } = Array.Empty<TemplateViewItem>();
    public string ActiveTemplateName { get; init; } = "";
}

public sealed record TemplateViewItem(
    string Name,
    double LabelWidthMm,
    double LabelHeightMm,
    double DataMatrixWidthMm,
    double DataMatrixHeightMm,
    double DataMatrixXmm,
    double DataMatrixYmm,
    int TextBlockCount,
    bool IsActive);

public sealed record ExportViewState
{
    public bool AutoSaveEnabled { get; init; }
    public string ExportFolder { get; init; } = "";
    public string LastSavedPath { get; init; } = "—";
    public string Status { get; init; } = "";
    public UiStatusKind StatusKind { get; init; } = UiStatusKind.Neutral;
    public IReadOnlyList<string> RecentFiles { get; init; } = Array.Empty<string>();
}

public sealed record DiagnosticsViewState
{
    public string Mode { get; init; } = "—";
    public string Scanner { get; init; } = "—";
    public string Status { get; init; } = "Не подключен";
    public UiStatusKind StatusKind { get; init; } = UiStatusKind.Neutral;
    public string LastCheck { get; init; } = "—";
    public string GsCount { get; init; } = "—";
    public string Ai91 { get; init; } = "—";
    public string Ai92 { get; init; } = "—";
    public string RawKeySummary { get; init; } = "Нет HID-сессии";
    public string Warning { get; init; } = "";
}

public sealed record ScanHistoryItem
{
    public DateTime Timestamp { get; init; }
    public string Status { get; init; } = "Ожидание";
    public UiStatusKind StatusKind { get; init; } = UiStatusKind.Neutral;
    public string Gtin { get; init; } = "—";
    public string Serial { get; init; } = "—";
    public string Ai91 { get; init; } = "—";
    public string Ai92 { get; init; } = "—";
    public string Ai93 { get; init; } = "—";
    public string GsCount { get; init; } = "—";
    public string Source { get; init; } = "—";
    public string CodeType { get; init; } = "—";
    public string RawEscaped { get; init; } = "—";
    public string RawPayload { get; init; } = "";
    public string NormalizedEscaped { get; init; } = "—";
    public string RawHex { get; init; } = "—";
    public string Error { get; init; } = "";
    public string SavedFolder { get; init; } = "—";
    public string Template { get; init; } = "—";
    public string Printer { get; init; } = "—";
    public string PrintStatus { get; init; } = "—";
    public ImageSource? PreviewImage { get; init; }
}
