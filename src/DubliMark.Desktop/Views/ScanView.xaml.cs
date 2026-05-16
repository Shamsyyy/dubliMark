using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DubliMark.Desktop.Views;

public partial class ScanView : UserControl
{
    private bool _updating;

    public event RoutedEventHandler? ConnectRequested;
    public event RoutedEventHandler? RefreshPortsRequested;
    public event RoutedEventHandler? SetupScannerRequested;
    public event RoutedEventHandler? HidDiagnosticsRequested;
    public event RoutedEventHandler? LoadImageRequested;
    public event RoutedEventHandler? PasteImageRequested;
    public event EventHandler<string>? ModeSelectionRequested;

    public ScanView()
    {
        InitializeComponent();
        ScanModeCombo.ItemsSource = new[] { "COM-порт", "HID" };
    }

    public string? SelectedPort => ScanPortsCombo.SelectedItem as string;

    public void UpdateState(ScanViewState state)
    {
        _updating = true;
        try
        {
            ScanModeCombo.SelectedItem = state.Mode == "HID" ? "HID" : "COM-порт";
            ScanPortsCombo.ItemsSource = state.Ports;
            ScanPortsCombo.SelectedItem = state.SelectedPort;
            if (ScanPortsCombo.SelectedIndex < 0 && state.Ports.Count > 0)
                ScanPortsCombo.SelectedIndex = 0;

            ScanPortHintText.Text = state.PortHint;
            ScanPortHintText.Visibility = string.IsNullOrWhiteSpace(state.PortHint)
                ? Visibility.Collapsed
                : Visibility.Visible;

            ScanStatusText.Text = state.ScannerStatus;
            ApplyBadge(ScanStatusBadge, ScanStatusText, state.ScannerStatusKind);
            ScanValidationText.Text = state.ValidationStatus;
            ApplyBadge(ScanValidationBadge, ScanValidationText, state.ValidationKind);

            ScanSourceText.Text = state.Source;
            ScanGsCountText.Text = state.GsCount;
            ScanCodeTypeText.Text = state.CodeType;
            ScanWaitText.Text = state.WaitText;
            ScanErrorText.Text = state.ErrorText;

            ScanGtinText.Text = state.Gtin;
            ScanSerialText.Text = state.Serial;
            ScanAi91Text.Text = state.Ai91;
            ScanAi92Text.Text = state.Ai92;
            ScanAi93Text.Text = "AI 93: " + state.Ai93;
            ScanRawEscapedText.Text = state.RawEscaped;
            ScanNormalizedEscapedText.Text = state.NormalizedEscaped;
            ScanRawHexText.Text = state.RawHex;

            ScanPreviewImage.Source = state.PreviewImage;
            ScanPreviewPlaceholder.Visibility = state.PreviewImage == null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        finally
        {
            _updating = false;
        }
    }

    private void ApplyBadge(Border border, TextBlock text, UiStatusKind kind)
    {
        var (backgroundKey, foregroundKey) = kind switch
        {
            UiStatusKind.Success => ("SuccessBadgeBackgroundBrush", "SuccessBrush"),
            UiStatusKind.Warning => ("WarningBadgeBackgroundBrush", "WarningBrush"),
            UiStatusKind.Error => ("DangerBadgeBackgroundBrush", "DangerBrush"),
            _ => ("NeutralBadgeBackgroundBrush", "MutedTextBrush")
        };

        border.Background = (Brush)FindResource(backgroundKey);
        border.BorderBrush = (Brush)FindResource(foregroundKey);
        text.Foreground = (Brush)FindResource(foregroundKey);
    }

    private void OnConnectProxyClick(object sender, RoutedEventArgs e) =>
        ConnectRequested?.Invoke(sender, e);

    private void OnRefreshPortsProxyClick(object sender, RoutedEventArgs e) =>
        RefreshPortsRequested?.Invoke(sender, e);

    private void OnSetupScannerProxyClick(object sender, RoutedEventArgs e) =>
        SetupScannerRequested?.Invoke(sender, e);

    private void OnHidDiagnosticsProxyClick(object sender, RoutedEventArgs e) =>
        HidDiagnosticsRequested?.Invoke(sender, e);

    private void OnLoadImageProxyClick(object sender, RoutedEventArgs e) =>
        LoadImageRequested?.Invoke(sender, e);

    private void OnPasteImageProxyClick(object sender, RoutedEventArgs e) =>
        PasteImageRequested?.Invoke(sender, e);

    private void OnModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating)
            return;

        if (ScanModeCombo.SelectedItem is string mode)
            ModeSelectionRequested?.Invoke(this, mode);
    }
}
