using CyberBilling.Client.Configuration;
using CyberBilling.Client.Networking;
using CyberBilling.Shared.Networking;
using CyberBilling.Shared.SystemIntegration;
using System.Windows;

namespace CyberBilling.Client;

public partial class MainWindow :
    Window
{
    private readonly TcpBillingClient
        _billingClient =
            new();

    private readonly ClientSettingsStore
        _settingsStore =
            new();

    private bool
        _isAccessUnlocked;

    private LockScreenWindow?
        _lockScreenWindow;

    /*
     * Chỉ lưu cấu hình sau khi kết nối
     * thành công lần đầu.
     *
     * Nhờ vậy nếu nhập nhầm IP thì lần
     * khởi động sau không tự khóa vào
     * một địa chỉ sai.
     */
    private bool
        _saveSettingsAfterConnect;

    /*
     * True khi Client khởi động từ cấu
     * hình đã lưu.
     *
     * Trường hợp này Client khóa màn hình
     * ngay cả khi Server đang chưa bật.
     */
    private bool
        _usingSavedSettings;

    private bool
        _hasConnectedSuccessfully;

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

        Loaded +=
            OnLoaded;

        Closed +=
            OnClosed;
    }

    private async void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        ClientSettings? settings =
            _settingsStore.Load();

        if (settings is null)
        {
            /*
             * Chưa cấu hình lần đầu.
             * Hiện cửa sổ để nhập IP máy
             * tính tiền.
             */
            return;
        }

        ServerAddressTextBox.Text =
            settings.ServerAddress;

        MachineNameTextBox.Text =
            settings.MachineName;

        _usingSavedSettings =
            true;

        /*
         * Chỉ có tác dụng sau khi publish
         * thành CyberBilling.Client.exe.
         *
         * Khi chạy bằng dotnet DLL trong
         * môi trường dev thì helper sẽ
         * tự bỏ qua.
         */
        WindowsStartupRegistration
            .TryRegisterCurrentExecutable(
                "CyberBilling.Client",
                "CyberBilling.Client.exe");

        ShowLockScreen(
            "Đang kết nối với máy tính tiền...");

        Hide();

        await StartConnectionAsync(
            settings.ServerAddress,
            settings.MachineName);
    }

    private async void ConnectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        string serverAddress =
            ServerAddressTextBox
                .Text
                .Trim();

        string machineName =
            MachineNameTextBox
                .Text
                .Trim();

        if (string.IsNullOrWhiteSpace(
                serverAddress)
            || string.IsNullOrWhiteSpace(
                machineName))
        {
            MessageBox.Show(
                "Vui lòng nhập IP máy tính tiền "
                + "và tên máy trạm.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        /*
         * Không lưu ngay.
         *
         * Chỉ khi Server thật sự xác nhận
         * kết nối thành công mới ghi file.
         */
        _saveSettingsAfterConnect =
            true;

        _usingSavedSettings =
            false;

        await StartConnectionAsync(
            serverAddress,
            machineName);
    }

    private async Task StartConnectionAsync(
        string serverAddress,
        string machineName)
    {
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

                        _hasConnectedSuccessfully =
                            true;

                        if (_saveSettingsAfterConnect)
                        {
                            ClientSettings settings =
                                new(
                                    ServerAddressTextBox
                                        .Text
                                        .Trim(),

                                    MachineNameTextBox
                                        .Text
                                        .Trim());

                            _settingsStore.Save(
                                settings);

                            _saveSettingsAfterConnect =
                                false;
                        }

                        WindowsStartupRegistration
                            .TryRegisterCurrentExecutable(
                                "CyberBilling.Client",
                                "CyberBilling.Client.exe");

                        /*
                         * Nếu trước đó máy đang khóa,
                         * tiếp tục giữ khóa trong lúc
                         * chờ Server đồng bộ trạng thái.
                         *
                         * Nếu khách đang chơi thì tuyệt
                         * đối không khóa lại chỉ vì Server
                         * vừa reconnect.
                         */
                        if (!_isAccessUnlocked)
                        {
                            ShowLockScreen(
                                "Đã kết nối với máy tính tiền");
                        }

                        Hide();

                        break;

                    case ClientConnectionState
                        .Connecting:

                        ConnectButton.Content =
                            "Đang kết nối...";

                        /*
                         * Máy đã mở Desktop thì không
                         * được tự khóa chỉ vì máy tính
                         * tiền đang khởi động.
                         */
                        if (!_isAccessUnlocked
                            && (_usingSavedSettings
                                || _hasConnectedSuccessfully))
                        {
                            ShowLockScreen(
                                "Đang kết nối với máy tính tiền...");

                            Hide();
                        }

                        break;

                    case ClientConnectionState
                        .Reconnecting:

                        ConnectButton.Content =
                            "Đang kết nối lại...";

                        /*
                         * Đây là quy tắc quan trọng:
                         *
                         * LOCKED  → vẫn LOCKED.
                         * UNLOCKED → vẫn UNLOCKED.
                         */
                        if (!_isAccessUnlocked
                            && (_usingSavedSettings
                                || _hasConnectedSuccessfully))
                        {
                            ShowLockScreen(
                                "Mất kết nối - "
                                + "đang thử kết nối lại...");

                            Hide();
                        }

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
        _isAccessUnlocked =
            false;

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

        if (!_lockScreenWindow
                .IsVisible)
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
        _isAccessUnlocked =
            true;

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