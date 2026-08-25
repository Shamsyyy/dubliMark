using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DoubleMark.Desktop.Views;

public partial class LoginView : UserControl
{
    private bool _passwordVisible;

    public event EventHandler<(string Email, string Password)>? SignInRequested;
    public event RoutedEventHandler? RegisterRequested;
    public event RoutedEventHandler? ResetPasswordRequested;

    public LoginView() => InitializeComponent();

    public void SetStatus(string text, bool isLoading = false, bool canSignIn = true)
    {
        StatusText.Text = text;
        SignInButton.IsEnabled = !isLoading && canSignIn;
    }

    public void SetLoading(bool isLoading, bool canSignIn = true) =>
        SignInButton.IsEnabled = !isLoading && canSignIn;

    private string GetPassword() =>
        _passwordVisible ? PasswordVisibleText.Text : PasswordText.Password;

    private void OnSignInClick(object sender, RoutedEventArgs e) =>
        SignInRequested?.Invoke(this, (EmailText.Text.Trim(), GetPassword()));

    private void OnRegisterClick(object sender, RoutedEventArgs e) =>
        RegisterRequested?.Invoke(sender, e);

    private void OnResetPasswordClick(object sender, RoutedEventArgs e) =>
        ResetPasswordRequested?.Invoke(sender, e);

    private void OnTogglePasswordClick(object sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        if (_passwordVisible)
        {
            PasswordVisibleText.Text = PasswordText.Password;
            PasswordText.Visibility = Visibility.Collapsed;
            PasswordVisibleText.Visibility = Visibility.Visible;
            TogglePasswordButton.Content = "Скрыть";
            TogglePasswordButton.ToolTip = "Скрыть пароль";
            PasswordVisibleText.Focus();
            PasswordVisibleText.CaretIndex = PasswordVisibleText.Text.Length;
        }
        else
        {
            PasswordText.Password = PasswordVisibleText.Text;
            PasswordVisibleText.Visibility = Visibility.Collapsed;
            PasswordText.Visibility = Visibility.Visible;
            TogglePasswordButton.Content = "Показать";
            TogglePasswordButton.ToolTip = "Показать пароль";
            PasswordText.Focus();
        }
    }

    private void OnLoginFieldKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        if (ReferenceEquals(sender, EmailText))
        {
            if (_passwordVisible)
                PasswordVisibleText.Focus();
            else
                PasswordText.Focus();
            return;
        }

        OnSignInClick(SignInButton, new RoutedEventArgs());
    }
}
