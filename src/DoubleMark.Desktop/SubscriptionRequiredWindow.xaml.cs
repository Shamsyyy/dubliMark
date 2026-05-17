using System.Windows;

namespace DoubleMark.Desktop;

public enum SubscriptionRequiredAction
{
    Close,
    OpenPricing,
    SwitchAccount
}

public partial class SubscriptionRequiredWindow : Window
{
    public SubscriptionRequiredAction SelectedAction { get; private set; } = SubscriptionRequiredAction.Close;

    public SubscriptionRequiredWindow(string featureName)
    {
        InitializeComponent();
        FeatureText.Text = featureName;
    }

    private void OnPricingClick(object sender, RoutedEventArgs e)
    {
        SelectedAction = SubscriptionRequiredAction.OpenPricing;
        DialogResult = true;
        Close();
    }

    private void OnOtherAccountClick(object sender, RoutedEventArgs e)
    {
        SelectedAction = SubscriptionRequiredAction.SwitchAccount;
        DialogResult = true;
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        SelectedAction = SubscriptionRequiredAction.Close;
        DialogResult = false;
        Close();
    }
}
