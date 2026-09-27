using CyberBilling.Client.Networking;
using CyberBilling.Shared.Networking;
using System.Windows;

namespace CyberBilling.Client;

public partial class MainWindow :
    Window
{
    private readonly TcpBillingClient
        _billingClient = new();

    private LockScreenWindow?
        _lockScreenWindow;

    public MainWindow()
    {
        InitializeComponent();

        MachineNameTextBox.Text =
            Environment.MachineName;

        _billingClient
            .ConnectionStateChanged +=
            OnConnectionStateChanged;

        _billingClient
            .WorkstationCommandReceived +=
            OnWorkstationCommandReceived;

        _billingClient
            .AdminLoginResultReceived +=
            OnAdminLoginResultReceived;

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

                        ShowLockScreen(
                            "Đã kết nối với máy tính tiền");

                        ShowLockScreen(
                            "Mất kết nối - đang thử kết nối lại...");

                        Hide();

                        break;

                    case ClientConnectionState
                        .Connecting:

                    case ClientConnectionState
                        .Reconnecting:

                        ConnectButton.Content =
                            "Đang kết nối...";

                        ShowLockScreen(
                            "Mất kết nối - đang thử kết nối lại...");

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
            .AdminLoginResultReceived -=
            OnAdminLoginResultReceived;

        _billingClient
            .ConnectionStateChanged -=
            OnConnectionStateChanged;

        _billingClient
            .WorkstationCommandReceived -=
            OnWorkstationCommandReceived;

        if (_lockScreenWindow is not null)
        {
            _lockScreenWindow
                .AdminLoginRequested -=
                OnAdminLoginRequested;

            _lockScreenWindow.AllowClose();

            _lockScreenWindow.Close();
        }

        await _billingClient
            .DisposeAsync();
    }

    private void OnWorkstationCommandReceived(
    WorkstationCommandType command)
    {
        Dispatcher.Invoke(
            () =>
            {
                if (command ==
                    WorkstationCommandType
                        .LockScreen)
                {
                    ShowLockScreen(
                        "Đã kết nối với máy tính tiền");

                    return;
                }

                if (command ==
                    WorkstationCommandType
                        .UnlockScreen)
                {
                    HideLockScreen();
                }
            });
    }

    private void ShowLockScreen(
        string connectionText)
    {
        if (_lockScreenWindow is null)
        {
            _lockScreenWindow =
                new LockScreenWindow();

            _lockScreenWindow
                .AdminLoginRequested +=
                OnAdminLoginRequested;
        }

        _lockScreenWindow
            .SetConnectionText(
                connectionText);

        if (!_lockScreenWindow.IsVisible)
        {
            _lockScreenWindow.Show();
        }

        _lockScreenWindow.WindowState =
            WindowState.Maximized;

        _lockScreenWindow.Topmost =
            true;

        _lockScreenWindow.Activate();
    }

    private void HideLockScreen()
    {
        if (_lockScreenWindow is null)
        {
            return;
        }

        _lockScreenWindow.Hide();
    }

    private void OnAdminLoginRequested(
        string username,
        string password)
    {
        _billingClient
            .RequestAdminLogin(
                username,
                password);
    }

    private void OnAdminLoginResultReceived(
        AdminLoginResultPayload result)
    {
        Dispatcher.Invoke(
            () =>
            {
                if (_lockScreenWindow is null)
                {
                    return;
                }

                _lockScreenWindow
                    .SetAdminLoginResult(
                        result.Success,
                        result.Message);

                if (result.Success)
                {
                    HideLockScreen();
                }
            });
    }
}