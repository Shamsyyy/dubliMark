using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DoubleMark.Desktop.Views;

public partial class DiagnosticsView : UserControl
{
    public event RoutedEventHandler? HidDiagnosticsRequested;
    public event RoutedEventHandler? ResetSettingsRequested;

    public DiagnosticsView() => InitializeComponent();

    public void UpdateState(DiagnosticsViewState state)
    {
        DiagnosticsStatusText.Text = state.Status;
        ApplyBadge(state.StatusKind);
        DiagnosticsModeText.Text = state.Mode;
        DiagnosticsScannerText.Text = state.Scanner;
        DiagnosticsLastCheckText.Text = state.LastCheck;
        DiagnosticsGsCountText.Text = state.GsCount;
        DiagnosticsAi91Text.Text = state.Ai91;
        DiagnosticsAi92Text.Text = state.Ai92;
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
}
