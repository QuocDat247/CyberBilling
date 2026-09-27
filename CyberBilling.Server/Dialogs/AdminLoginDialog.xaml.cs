using System.Windows;

namespace CyberBilling.Server.Dialogs;

public partial class AdminLoginDialog :
    Window
{
    public AdminLoginDialog()
    {
        InitializeComponent();

        Loaded +=
            (_, _) =>
                PasswordBox.Focus();
    }

    public string EnteredPassword =>
        PasswordBox.Password;

    private void LoginButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult =
            true;
    }

    private void CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult =
            false;
    }
}