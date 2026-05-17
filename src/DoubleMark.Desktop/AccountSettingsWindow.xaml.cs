using System.Windows;
using DoubleMark.Desktop.Services.Account;

namespace DoubleMark.Desktop;

public partial class AccountSettingsWindow : Window
{
    public ProfileUpdate? Result { get; private set; }
    public event RoutedEventHandler? ResetPasswordRequested;

    public AccountSettingsWindow(AccountProfile? profile)
    {
        InitializeComponent();
        OrganizationText.Text = profile?.Organization ?? "";
        InnText.Text = profile?.Inn ?? "";
        PhoneText.Text = profile?.Phone ?? "";
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Result = new ProfileUpdate(
            EmptyToNull(OrganizationText.Text),
            EmptyToNull(InnText.Text),
            EmptyToNull(PhoneText.Text));
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnResetPasswordClick(object sender, RoutedEventArgs e) =>
        ResetPasswordRequested?.Invoke(sender, e);

    private static string? EmptyToNull(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
