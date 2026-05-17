using System.Windows;
using System.Windows.Controls;

namespace DoubleMark.Desktop.Views;

public partial class LoginView : UserControl
{
    public event EventHandler<(string Email, string Password)>? SignInRequested;
    public event RoutedEventHandler? RegisterRequested;
    public event RoutedEventHandler? ResetPasswordRequested;

    public LoginView() => InitializeComponent();

    public void SetStatus(string text, bool isLoading = false, bool canSignIn = true)
    {
        StatusText.Text = text;
        SignInButton.IsEnabled = !isLoading && canSignIn;
    }

    private void OnSignInClick(object sender, RoutedEventArgs e) =>
        SignInRequested?.Invoke(this, (EmailText.Text.Trim(), PasswordText.Password));

    private void OnRegisterClick(object sender, RoutedEventArgs e) =>
        RegisterRequested?.Invoke(sender, e);

    private void OnResetPasswordClick(object sender, RoutedEventArgs e) =>
        ResetPasswordRequested?.Invoke(sender, e);
}
