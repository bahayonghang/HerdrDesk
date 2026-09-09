using Microsoft.UI.Xaml.Controls;

namespace HerdDesk.App.Views;

public sealed partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        NameText.Text = ProductInfo.Name;
        VersionText.Text = ProductInfo.Version;
        StatementText.Text = ProductInfo.IndependentClientStatement;
    }
}
