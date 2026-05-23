using System.Windows;

namespace DoubleMark.Desktop;

public partial class ConfirmDialogWindow : Window
{
    public ConfirmDialogWindow(string title, string message, string confirmText, string cancelText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
    }

    public static bool Show(
        Window owner,
        string title,
        string message,
        string confirmText = "Да",
        string cancelText = "Нет")
    {
        var dialog = new ConfirmDialogWindow(title, message, confirmText, cancelText)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
