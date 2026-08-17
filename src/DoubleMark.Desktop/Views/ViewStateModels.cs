using System.Windows.Media;
using DoubleMark.Core.Print;

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
    public string PrintModeLabel { get; init; } = "Ручная";
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
    public string SyncStatusText { get; init; } = "";
    public bool IsSignedIn { get; init; }
    public ImageSource? PreviewImage { get; init; }
    public double LabelWidthMm { get; init; }
    public double LabelHeightMm { get; init; }
    public double DataMatrixWidthMm { get; init; }
    public double DataMatrixHeightMm { get; init; }
    public double DataMatrixXmm { get; init; }
    public double DataMatrixYmm { get; init; }
    public IReadOnlyList<TemplateTextBlockViewItem> TextBlocks { get; init; } = Array.Empty<TemplateTextBlockViewItem>();
}

public sealed record TemplateTextBlockViewItem(
    string Text,
    double Xmm,
    double Ymm,
    double FontSizePt,
    bool Bold = false,
    string? PreviewText = null,
    TextBlockLayout Layout = TextBlockLayout.Horizontal,
    TextFlowDirection Flow = TextFlowDirection.Right,
    TextBlockDirection? Orientation = null,
    bool Enabled = true,
    LabelFontId FontId = LabelFontId.ArialIndustrial);

public sealed record TemplateViewItem(
    string Name,
    double LabelWidthMm,
    double LabelHeightMm,
    double DataMatrixWidthMm,
    double DataMatrixHeightMm,
    double DataMatrixXmm,
    double DataMatrixYmm,
    int TextBlockCount,
    bool IsActive,
    bool IsDefault = false,
    string? Description = null,
    string? UpdatedAtText = null);

public sealed record PdfPrintPageResultItem(
    int PageNumber,
    bool Success,
    string? Gtin,
    string? Serial,
    string? Error,
    string? StatusLabel = null);

public sealed record PdfPrintViewState
{
    public string PdfPath { get; init; } = "";
    public IReadOnlyList<string> Printers { get; init; } = Array.Empty<string>();
    public string? SelectedPrinter { get; init; }
    public IReadOnlyList<string> Templates { get; init; } = Array.Empty<string>();
    public string? SelectedTemplate { get; init; }
    public string TemplateSize { get; init; } = "—";
    public string DataMatrixSize { get; init; } = "—";
    public string Status { get; init; } = "";
    public string Summary { get; init; } = "—";
    public string PageCountText { get; init; } = "";
    public ImageSource? PreviewImage { get; init; }
    public bool IsBusy { get; init; }
    public double ProgressPercent { get; init; }
    public bool CanPrint { get; init; }
    public bool HasBatchRecords { get; init; }
    public int ProblemCount { get; init; }
    public int TotalRecordCount { get; init; }
    public IReadOnlyList<PdfPrintPageResultItem> PageResults { get; init; } = Array.Empty<PdfPrintPageResultItem>();
    public IReadOnlyList<PdfPrintHistoryItem> HistoryItems { get; init; } = Array.Empty<PdfPrintHistoryItem>();
}

public sealed record PdfPrintHistoryItem(
    string JobId,
    string Title,
    string Subtitle,
    int ProblemCount,
    bool PdfMissing);

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
    public string Ai01 { get; init; } = "—";
    public string Ai21 { get; init; } = "—";
    public string Ai91 { get; init; } = "—";
    public string Ai92 { get; init; } = "—";
    public string PrintMode { get; init; } = "—";
    public string Printer { get; init; } = "—";
    public string Template { get; init; } = "—";
    public string LastPrintStatus { get; init; } = "—";
    public string AvailableComPorts { get; init; } = "—";
    public string RawEscaped { get; init; } = "—";
    public string RawHex { get; init; } = "—";
    public string RawKeySummary { get; init; } = "Нет HID-сессии";
    public string Warning { get; init; } = "";
}

public sealed record ScanHistoryItem
{
    public string? CloudId { get; init; }
    public DateTime Timestamp { get; init; }
    public string Status { get; init; } = "Ожидание";
    public UiStatusKind StatusKind { get; init; } = UiStatusKind.Neutral;
    public string Gtin { get; init; } = "—";
    public string Serial { get; init; } = "—";
    public string Ai91 { get; init; } = "—";
    public string Ai92 { get; init; } = "—";
    public string Ai93 { get; init; } = "—";
    public bool HasAi01 { get; init; }
    public bool HasAi21 { get; init; }
    public bool HasAi91Flag { get; init; }
    public bool HasAi92Flag { get; init; }
    public string GsCount { get; init; } = "—";
    public string Source { get; init; } = "—";
    public string CodeType { get; init; } = "—";
    public string RawEscaped { get; init; } = "—";
    public string RawPayload { get; init; } = "";
    public string MaskedPreview { get; init; } = "—";
    public string NormalizedEscaped { get; init; } = "—";
    public string RawHex { get; init; } = "—";
    public string Error { get; init; } = "";
    public string SavedFolder { get; init; } = "—";
    public string Template { get; init; } = "—";
    public string Printer { get; init; } = "—";
    public string PrintStatus { get; init; } = "—";
    public ImageSource? PreviewImage { get; init; }

    public string AiFlagsSummary =>
        $"01 {(HasAi01 ? "✓" : "—")}  21 {(HasAi21 ? "✓" : "—")}  91 {(HasAi91Flag ? "✓" : "—")}  92 {(HasAi92Flag ? "✓" : "—")}";
}
