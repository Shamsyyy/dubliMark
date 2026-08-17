using System.Windows;

namespace DoubleMark.Desktop;

public partial class RegisterWindow : Window
{
    public string Email => EmailText.Text.Trim();
    public string Password => PasswordText.Password;
    public string CompanyName => CompanyText.Text.Trim();

    public RegisterWindow()
    {
        InitializeComponent();
    }

    public void SetBusy(bool busy, string? message = null)
    {
        RegisterButton.IsEnabled = !busy;
        if (message != null)
            StatusText.Text = message;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnRegisterClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            StatusText.Text = "Введите email и пароль.";
            return;
        }

        if (Password.Length < 6)
        {
            StatusText.Text = "Пароль должен быть не короче 6 символов.";
            return;
        }

        DialogResult = true;
        Close();
    }
}
