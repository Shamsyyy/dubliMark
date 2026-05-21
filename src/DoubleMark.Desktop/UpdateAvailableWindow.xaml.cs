using System.Windows;
using System.Windows.Controls;
using DoubleMark.Desktop.Services.Update;

namespace DoubleMark.Desktop;

public enum UpdateDialogAction
{
    Later,
    UpdateNow,
    OpenWebsite
}

public partial class UpdateAvailableWindow : Window
{
    public event Func<Task>? DownloadRequested;

    public UpdateDialogAction SelectedAction { get; private set; } = UpdateDialogAction.Later;
    public UpdateManifest Manifest { get; }

    public UpdateAvailableWindow(UpdateManifest manifest, bool hideLater)
    {
        InitializeComponent();
        Manifest = manifest;
        TitleText.Text = manifest.DisplayTitle;
        VersionText.Text = "Доступна новая версия " + manifest.DisplayTitle;
        ReleaseDateText.Text = string.IsNullOrWhiteSpace(manifest.ReleaseDate)
            ? ""
            : "Дата релиза: " + manifest.ReleaseDate;

        NotesPanel.Children.Clear();
        if (manifest.Notes.Count == 0)
        {
            NotesPanel.Children.Add(new TextBlock
            {
                Text = "Список изменений не указан.",
                Style = (Style)FindResource("MutedText"),
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            foreach (var note in manifest.Notes)
            {
                NotesPanel.Children.Add(new TextBlock
                {
                    Text = "• " + note,
                    Style = (Style)FindResource("MutedText"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }
        }

        if (hideLater)
            LaterButton.Visibility = Visibility.Collapsed;
    }

    public void SetBusy(bool busy, string? status = null)
    {
        UpdateNowButton.IsEnabled = !busy;
        LaterButton.IsEnabled = !busy && LaterButton.Visibility == Visibility.Visible;
        DownloadProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(status))
            StatusText.Text = status;
    }

    public void SetProgress(double percent)
    {
        DownloadProgress.Value = Math.Clamp(percent, 0, 100);
        StatusText.Text = "Скачивание обновления: " + percent.ToString("0") + "%";
    }

    private async void OnUpdateNowClick(object sender, RoutedEventArgs e)
    {
        SelectedAction = UpdateDialogAction.UpdateNow;
        if (DownloadRequested != null)
            await DownloadRequested();
    }

    private void OnLaterClick(object sender, RoutedEventArgs e)
    {
        SelectedAction = UpdateDialogAction.Later;
        DialogResult = false;
        Close();
    }

    private void OnOpenSiteClick(object sender, RoutedEventArgs e)
    {
        SelectedAction = UpdateDialogAction.OpenWebsite;
        DialogResult = true;
        Close();
    }
}
