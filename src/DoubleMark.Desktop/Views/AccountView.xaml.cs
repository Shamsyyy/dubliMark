using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Services.Account;
using DoubleMark.Desktop.Services.Update;

namespace DoubleMark.Desktop.Views;

public partial class AccountView : UserControl
{
    public event RoutedEventHandler? ProfileSettingsRequested;
    public event RoutedEventHandler? SignOutRequested;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? OpenAccountSiteRequested;
    public event RoutedEventHandler? OpenPricingRequested;
    public event RoutedEventHandler? ResetPasswordRequested;
    public event RoutedEventHandler? CheckUpdatesRequested;
    public event RoutedEventHandler? DownloadUpdateRequested;
    public event RoutedEventHandler? OpenDownloadsPageRequested;
    public event EventHandler<bool>? AutoCheckUpdatesChanged;

    public AccountView()
    {
        InitializeComponent();
        UpdateAppReleaseInfo();
    }

    public void UpdateState(AccountSnapshot snapshot)
    {
        AccountTitleText.Text = snapshot.Profile?.Organization ?? "Аккаунт DoubleMark";
        AccountEmailText.Text = snapshot.User?.Email ?? "Войдите в аккаунт DoubleMark";
        InitialsText.Text = BuildInitials(snapshot.Profile?.Organization, snapshot.User?.Email);
        AccountStatusText.Text = snapshot.Error ?? snapshot.Subscription.DisplayStatus;
        AccountStatusText.Foreground = snapshot.Subscription.IsActive
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("WarningBrush");

        OrganizationText.Text = Empty(snapshot.Profile?.Organization);
        InnText.Text = Empty(snapshot.Profile?.Inn);
        PhoneText.Text = Empty(snapshot.Profile?.Phone);
        RoleText.Text = Empty(snapshot.Profile?.Role);
        PlanText.Text = Empty(snapshot.Subscription.Subscription?.PlanId);
        SubscriptionStatusText.Text = snapshot.Subscription.DisplayStatus;
        PeriodStartText.Text = Date(snapshot.Subscription.Subscription?.CurrentPeriodStart);
        PeriodEndText.Text = Date(snapshot.Subscription.EndsAt);
        DevicesLimitText.Text = $"{snapshot.Devices.Count} / {snapshot.Subscription.Subscription?.DevicesLimit ?? 1}";

        RenderPayments(snapshot.Payments);
        RenderDevices(snapshot.Devices);
        UpdateAppReleaseInfo();
    }

    public void UpdateAppReleaseInfo()
    {
        var release = AppReleaseInfoProvider.Current;
        var check = UpdateService.Instance.LastCheck;
        AppVersionText.Text = release.VersionLabel;
        AppBuildIdText.Text = string.IsNullOrWhiteSpace(release.BuildId) ? "—" : release.BuildId;
        AppUpdatedAtText.Text = release.BuiltAtLabel;
        AppLatestVersionText.Text = check?.Manifest?.Version ?? "—";
        AppInstallPathText.Text = string.IsNullOrWhiteSpace(release.InstallPath) ? "—" : release.InstallPath;
        if (!string.IsNullOrWhiteSpace(release.InstallWarning))
        {
            AppInstallWarningText.Text = release.InstallWarning;
            AppInstallWarningText.Visibility = Visibility.Visible;
        }
        else
        {
            AppInstallWarningText.Text = "";
            AppInstallWarningText.Visibility = Visibility.Collapsed;
        }

        DownloadUpdateButton.IsEnabled = check?.Status == UpdateCheckStatus.UpdateAvailable;
    }

    public void SetUpdateStatus(string status) => AppUpdateStatusText.Text = status;

    public void SetAutoCheckUpdates(bool enabled)
    {
        AutoCheckUpdatesCheckBox.IsChecked = enabled;
    }

    private void OnCheckUpdatesClick(object sender, RoutedEventArgs e) =>
        CheckUpdatesRequested?.Invoke(sender, e);

    private void OnDownloadUpdateClick(object sender, RoutedEventArgs e) =>
        DownloadUpdateRequested?.Invoke(sender, e);

    private void OnOpenDownloadsPageClick(object sender, RoutedEventArgs e) =>
        OpenDownloadsPageRequested?.Invoke(sender, e);

    private void OnAutoCheckUpdatesChanged(object sender, RoutedEventArgs e) =>
        AutoCheckUpdatesChanged?.Invoke(sender, AutoCheckUpdatesCheckBox.IsChecked == true);

    private void RenderPayments(IReadOnlyList<AccountPayment> payments)
    {
        PaymentsPanel.Children.Clear();
        if (payments.Count == 0)
        {
            PaymentsPanel.Children.Add(Muted("Платежей нет"));
            return;
        }

        foreach (var payment in payments.Take(6))
            PaymentsPanel.Children.Add(Muted($"{Date(payment.CreatedAt)} · {Empty(payment.Plan)} · {payment.Amount:0.##} {payment.Currency} · {Empty(payment.Status)}"));
    }

    private void RenderDevices(IReadOnlyList<AccountDevice> devices)
    {
        DevicesPanel.Children.Clear();
        if (devices.Count == 0)
        {
            DevicesPanel.Children.Add(Muted("Устройств нет"));
            return;
        }

        foreach (var device in devices.Take(6))
            DevicesPanel.Children.Add(Muted($"{device.DeviceName} · {device.Platform} · {Date(device.LastSeenAt)}"));
    }

    private void OnProfileSettingsClick(object sender, RoutedEventArgs e) =>
        ProfileSettingsRequested?.Invoke(sender, e);

    private void OnSignOutClick(object sender, RoutedEventArgs e) =>
        SignOutRequested?.Invoke(sender, e);

    private void OnRefreshClick(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(sender, e);

    private void OnOpenAccountSiteClick(object sender, RoutedEventArgs e) =>
        OpenAccountSiteRequested?.Invoke(sender, e);

    private void OnOpenPricingClick(object sender, RoutedEventArgs e) =>
        OpenPricingRequested?.Invoke(sender, e);

    private void OnResetPasswordClick(object sender, RoutedEventArgs e) =>
        ResetPasswordRequested?.Invoke(sender, e);

    private TextBlock Muted(string text) =>
        new()
        {
            Text = text,
            Style = (Style)FindResource("MutedText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };

    private static string Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string Date(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy") ?? "—";

    private static string BuildInitials(string? organization, string? email)
    {
        var source = !string.IsNullOrWhiteSpace(organization) ? organization : email;
        if (string.IsNullOrWhiteSpace(source))
            return "DM";

        var letters = source.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part[0])
            .Take(2)
            .ToArray();
        return letters.Length == 0 ? "DM" : new string(letters).ToUpperInvariant();
    }
}
