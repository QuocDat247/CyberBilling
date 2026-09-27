using System.Windows;
using CyberBilling.Client.Networking;

namespace CyberBilling.Client;

public partial class MainWindow :
    Window
{
    private readonly TcpBillingClient
        _billingClient = new();

    public MainWindow()
    {
        InitializeComponent();

        MachineNameTextBox.Text =
            Environment.MachineName;

        _billingClient
            .ConnectionStateChanged +=
            OnConnectionStateChanged;

        Closed +=
            OnClosed;
    }

    private async void ConnectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        string serverAddress =
            ServerAddressTextBox.Text.Trim();

        string machineName =
            MachineNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                serverAddress)
            || string.IsNullOrWhiteSpace(
                machineName))
        {
            MessageBox.Show(
                "Vui lòng nhập IP máy chủ "
                + "và tên máy trạm.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        ConnectButton.IsEnabled =
            false;

        ServerAddressTextBox.IsEnabled =
            false;

        MachineNameTextBox.IsEnabled =
            false;

        await _billingClient
            .StartAsync(
                serverAddress,
                machineName);
    }

    private void OnConnectionStateChanged(
        object? sender,
        ClientConnectionStateChangedEventArgs e)
    {
        Dispatcher.Invoke(
            () =>
            {
                ConnectionStatusText.Text =
                    e.Message;

                switch (e.State)
                {
                    case ClientConnectionState
                        .Connected:

                        ConnectButton.Content =
                            "Đã kết nối";

                        break;

                    case ClientConnectionState
                        .Connecting:

                    case ClientConnectionState
                        .Reconnecting:

                        ConnectButton.Content =
                            "Đang kết nối...";

                        break;

                    case ClientConnectionState
                        .Stopped:

                        ConnectButton.Content =
                            "Kết nối";

                        break;
                }
            });
    }

    private async void OnClosed(
        object? sender,
        EventArgs e)
    {
        _billingClient
            .ConnectionStateChanged -=
            OnConnectionStateChanged;

        await _billingClient
            .DisposeAsync();
    }
}