using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DoubleMark.Desktop.Views;

public partial class DiagnosticsView : UserControl
{
    public event RoutedEventHandler? HidDiagnosticsRequested;
    public event RoutedEventHandler? ResetSettingsRequested;
    public event RoutedEventHandler? OpenLogsRequested;
    public event RoutedEventHandler? CopyDiagnosticsRequested;
    public event RoutedEventHandler? RefreshComPortsRequested;
    public event RoutedEventHandler? RefreshPrintersRequested;

    public DiagnosticsView() => InitializeComponent();

    public void UpdateState(DiagnosticsViewState state)
    {
        DiagnosticsStatusText.Text = state.Status;
        ApplyBadge(state.StatusKind);
        DiagnosticsModeText.Text = state.Mode;
        DiagnosticsScannerText.Text = state.Scanner;
        DiagnosticsLastCheckText.Text = state.LastCheck;
        DiagnosticsGsCountText.Text = state.GsCount;
        DiagnosticsAi01Text.Text = state.Ai01;
        DiagnosticsAi21Text.Text = state.Ai21;
        DiagnosticsAi91Text.Text = state.Ai91;
        DiagnosticsAi92Text.Text = state.Ai92;
        DiagnosticsPrintStatusText.Text =
            $"Печать: {state.PrintMode} · принтер: {state.Printer} · шаблон: {state.Template} · {state.LastPrintStatus}";
        DiagnosticsComPortsText.Text = "COM-порты: " + state.AvailableComPorts;
        DiagnosticsRawEscapedText.Text = state.RawEscaped;
        DiagnosticsRawHexText.Text = state.RawHex;
        DiagnosticsWarningText.Text = string.IsNullOrWhiteSpace(state.Warning) ? "Критичных предупреждений нет" : state.Warning;
        DiagnosticsRawKeyText.Text = state.RawKeySummary;
    }

    private void ApplyBadge(UiStatusKind kind)
    {
        var brush = (Brush)FindResource(kind switch
        {
            UiStatusKind.Success => "SuccessBrush",
            UiStatusKind.Warning => "WarningBrush",
            UiStatusKind.Error => "DangerBrush",
            _ => "MutedTextBrush"
        });
        DiagnosticsStatusBadge.BorderBrush = brush;
        DiagnosticsStatusBadge.Background = (Brush)FindResource(kind switch
        {
            UiStatusKind.Success => "SuccessBadgeBackgroundBrush",
            UiStatusKind.Warning => "WarningBadgeBackgroundBrush",
            UiStatusKind.Error => "DangerBadgeBackgroundBrush",
            _ => "NeutralBadgeBackgroundBrush"
        });
        DiagnosticsStatusText.Foreground = brush;
    }

    private void OnHidDiagnosticsProxyClick(object sender, RoutedEventArgs e) =>
        HidDiagnosticsRequested?.Invoke(sender, e);

    private void OnResetProxyClick(object sender, RoutedEventArgs e) =>
        ResetSettingsRequested?.Invoke(sender, e);

    private void OnOpenLogsProxyClick(object sender, RoutedEventArgs e) =>
        OpenLogsRequested?.Invoke(sender, e);

    private void OnCopyDiagnosticsProxyClick(object sender, RoutedEventArgs e) =>
        CopyDiagnosticsRequested?.Invoke(sender, e);

    private void OnRefreshComPortsProxyClick(object sender, RoutedEventArgs e) =>
        RefreshComPortsRequested?.Invoke(sender, e);

    private void OnRefreshPrintersProxyClick(object sender, RoutedEventArgs e) =>
        RefreshPrintersRequested?.Invoke(sender, e);
}
