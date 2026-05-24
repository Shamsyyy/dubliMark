using System.Windows;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Services.Update;

namespace DoubleMark.Desktop;

public partial class MainWindow
{
    private bool _updateCheckInProgress;
    private bool _mandatoryUpdateDismissedForSession;

    private UpdateService UpdateService => UpdateService.Instance;

    private bool IsMandatoryUpdateBlocking =>
        !_mandatoryUpdateDismissedForSession &&
        UpdateService.RequiresMandatoryUpdate(UpdateService.LastCheck?.Manifest);

    private async void OnAccountCheckUpdatesRequested(object? sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(showUpToDateToast: true, showDialogOnAvailable: true);

    private async void OnAccountDownloadUpdateRequested(object? sender, RoutedEventArgs e) =>
        await DownloadAndInstallUpdateAsync(UpdateService.LastCheck?.Manifest);

    private void OnAccountOpenDownloadsPageRequested(object? sender, RoutedEventArgs e) =>
        OpenUrl(UpdateService.DownloadsPageUrl);

    private void OnAccountAutoCheckUpdatesChanged(object? sender, bool enabled)
    {
        _settings.AutoCheckUpdates = enabled;
        _settings.Save();
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (!_settings.AutoCheckUpdates)
            return;

        await CheckForUpdatesAsync(showUpToDateToast: false, showDialogOnAvailable: false);

        if (UpdateService.LastCheck?.Status == UpdateCheckStatus.UpdateAvailable)
            ShowToast("Доступна новая версия DoubleMark", ToastKind.Warning);

        if (IsMandatoryUpdateBlocking)
            ShowMandatoryUpdateDialog();
    }

    private async Task CheckForUpdatesAsync(bool showUpToDateToast, bool showDialogOnAvailable)
    {
        if (_updateCheckInProgress)
            return;

        _updateCheckInProgress = true;
        try
        {
            _accountView?.SetUpdateStatus("Проверяем обновления...");
            var result = await UpdateService.CheckForUpdatesAsync();
            RefreshAccountUpdateUi();

            switch (result.Status)
            {
                case UpdateCheckStatus.UpToDate:
                    if (showUpToDateToast)
                        ShowToast(result.UserMessage ?? "У вас последняя версия.", ToastKind.Success);
                    break;
                case UpdateCheckStatus.UpdateAvailable:
                    if (showDialogOnAvailable && result.Manifest != null)
                        ShowUpdateDialog(result.Manifest);
                    break;
                case UpdateCheckStatus.Failed:
                    if (showUpToDateToast)
                        ShowToast(result.UserMessage ?? "Обновления сейчас недоступны.", ToastKind.Warning);
                    break;
            }
        }
        finally
        {
            _updateCheckInProgress = false;
        }
    }

    private void ShowMandatoryUpdateDialog()
    {
        var manifest = UpdateService.LastCheck?.Manifest;
        if (manifest == null)
            return;

        ShowUpdateDialog(manifest, mandatory: true);
    }

    private void ShowUpdateDialog(UpdateManifest manifest, bool mandatory = false)
    {
        var hideLater = mandatory || UpdateService.RequiresMandatoryUpdate(manifest);
        var window = new UpdateAvailableWindow(manifest, hideLater) { Owner = this };
        window.DownloadRequested += () => DownloadAndInstallUpdateAsync(manifest, window);

        var result = window.ShowDialog();
        if (window.SelectedAction == UpdateDialogAction.OpenWebsite)
            OpenUrl(UpdateService.DownloadsPageUrl);

        if (result != true && mandatory)
            _mandatoryUpdateDismissedForSession = true;
    }

    private async Task DownloadAndInstallUpdateAsync(UpdateManifest? manifest, UpdateAvailableWindow? progressDialog = null)
    {
        if (manifest == null)
        {
            ShowToast("Сначала проверьте обновления.", ToastKind.Warning);
            return;
        }

        progressDialog?.SetBusy(true, "Скачиваем установщик...");
        _accountView?.SetUpdateStatus("Скачиваем обновление...");
        IProgress<double>? progress = progressDialog == null
            ? null
            : new Progress<double>(p => progressDialog.SetProgress(p));

        var download = await UpdateService.DownloadUpdateAsync(manifest, progress);
        RefreshAccountUpdateUi();

        if (download.Status != UpdateDownloadStatus.Success || string.IsNullOrWhiteSpace(download.FilePath))
        {
            var message = download.UserMessage ?? download.Status switch
            {
                UpdateDownloadStatus.SignatureInvalid =>
                    "Обновление не прошло проверку цифровой подписи. Скачайте установщик вручную с doublemark.ru.",
                UpdateDownloadStatus.HashMismatch =>
                    "Файл обновления повреждён. Скачайте установщик вручную с doublemark.ru.",
                _ => "Не удалось скачать обновление."
            };
            progressDialog?.SetBusy(false, message);
            ShowToast(message, ToastKind.Error);
            return;
        }

        try
        {
            progressDialog?.SetBusy(true, "Запускаем установщик...");
            ShowToast(
                "Сейчас закроется DoubleMark и откроется установщик. Дождитесь окончания установки и запустите программу снова.",
                ToastKind.Warning);
            UpdateService.StartInstallerAndExit(download.FilePath);
        }
        catch (Exception ex)
        {
            LoggingService.Error("Update", "Installer launch failed", ex);
            progressDialog?.SetBusy(false, "Не удалось запустить установщик.");
            ShowToast("Не удалось запустить установщик.", ToastKind.Error);
        }
    }

    private bool EnsureAppVersionAllowed(string featureName)
    {
        if (!IsMandatoryUpdateBlocking)
            return true;

        ShowToast("Требуется обновление DoubleMark для продолжения работы.", ToastKind.Warning);
        ShowMandatoryUpdateDialog();
        return false;
    }

    private void RefreshAccountUpdateUi()
    {
        _accountView?.UpdateAppReleaseInfo();
        _accountView?.SetUpdateStatus(BuildUpdateStatusText());
    }

    private string BuildUpdateStatusText()
    {
        var check = UpdateService.LastCheck;
        if (check == null)
            return "Проверка обновлений ещё не выполнялась.";

        return check.Status switch
        {
            UpdateCheckStatus.UpToDate => check.UserMessage ?? "У вас последняя версия.",
            UpdateCheckStatus.UpdateAvailable =>
                "Доступна версия " + (check.Manifest?.Version ?? "—") + ".",
            UpdateCheckStatus.Failed => check.UserMessage ?? "Обновления сейчас недоступны.",
            _ => "—"
        };
    }

}
