using System.Windows;

namespace DoubleMark.Desktop;

public partial class RegisterWindow : Window
{
    public string Email => EmailText.Text.Trim();
    public string Password => PasswordText.Password;
    public string CompanyName => CompanyText.Text.Trim();
    public string Inn => InnText.Text.Trim();
    public string Phone => PhoneText.Text.Trim();
    public bool PersonalDataConsent => ConsentCheck.IsChecked == true;
    public bool AcceptOffer => OfferCheck.IsChecked == true;

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

        if (Password.Length < 8)
        {
            StatusText.Text = "Пароль должен быть не короче 8 символов.";
            return;
        }

        if (!AcceptOffer)
        {
            StatusText.Text = "Подтвердите принятие оферты.";
            return;
        }

        if (!PersonalDataConsent)
        {
            StatusText.Text = "Нужно согласие на обработку персональных данных.";
            return;
        }

        DialogResult = true;
        Close();
    }
}
