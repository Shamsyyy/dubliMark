using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DubliMark.Desktop.Views;

public partial class ExportView : UserControl
{
    private bool _updating;

    public event RoutedEventHandler? ChooseExportFolderRequested;
    public event RoutedEventHandler? OpenExportFolderRequested;
    public event EventHandler<bool>? AutoSaveChanged;

    public ExportView() => InitializeComponent();

    public void UpdateState(ExportViewState state)
    {
        _updating = true;
        try
        {
            ExportAutoToggle.IsChecked = state.AutoSaveEnabled;
            ExportAutoText.Text = state.AutoSaveEnabled ? "Вкл." : "Выкл.";
            ExportFolderText.Text = state.ExportFolder;
            ExportLastPathText.Text = state.LastSavedPath;
            ExportStatusText.Text = state.Status;
            ApplyStatus(state.StatusKind);
            RenderRecentFiles(state.RecentFiles);
        }
        finally
        {
            _updating = false;
        }
    }

    private void ApplyStatus(UiStatusKind kind)
    {
        var key = kind switch
        {
            UiStatusKind.Success => "SuccessBrush",
            UiStatusKind.Warning => "WarningBrush",
            UiStatusKind.Error => "DangerBrush",
            _ => "MutedTextBrush"
        };
        var brush = (Brush)FindResource(key);
        ExportStatusBadge.BorderBrush = brush;
        ExportStatusBadge.Background = (Brush)FindResource(kind switch
        {
            UiStatusKind.Success => "SuccessBadgeBackgroundBrush",
            UiStatusKind.Warning => "WarningBadgeBackgroundBrush",
            UiStatusKind.Error => "DangerBadgeBackgroundBrush",
            _ => "NeutralBadgeBackgroundBrush"
        });
        ExportStatusText.Foreground = brush;
    }

    private void RenderRecentFiles(IReadOnlyList<string> files)
    {
        RecentExportFilesPanel.Children.Clear();
        if (files.Count == 0)
        {
            RecentExportFilesPanel.Children.Add(new TextBlock
            {
                Text = "Файлов пока нет",
                Style = (Style)FindResource("MutedText")
            });
            return;
        }

        foreach (var file in files.Take(10))
        {
            RecentExportFilesPanel.Children.Add(new Border
            {
                Style = (Style)FindResource("SoftSurface"),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new TextBlock
                {
                    Text = file,
                    Foreground = (Brush)FindResource("TextBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            });
        }
    }

    private void OnChooseExportProxyClick(object sender, RoutedEventArgs e) =>
        ChooseExportFolderRequested?.Invoke(sender, e);

    private void OnOpenExportProxyClick(object sender, RoutedEventArgs e) =>
        OpenExportFolderRequested?.Invoke(sender, e);

    private void OnAutoSaveToggleChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating)
            AutoSaveChanged?.Invoke(this, ExportAutoToggle.IsChecked == true);
    }
}
