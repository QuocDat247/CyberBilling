using System.Windows;

namespace CyberBilling.Server.Dialogs;

public partial class AdminPasswordSetupDialog :
    Window
{
    public AdminPasswordSetupDialog()
    {
        InitializeComponent();
    }

    public string EnteredPassword
    {
        get;
        private set;
    } =
        string.Empty;

    private void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        string password =
            PasswordBox.Password;

        if (password.Length < 4)
        {
            MessageBox.Show(
                "Mật khẩu phải có ít nhất 4 ký tự.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (password !=
            ConfirmPasswordBox.Password)
        {
            MessageBox.Show(
                "Hai mật khẩu không giống nhau.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        EnteredPassword =
            password;

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