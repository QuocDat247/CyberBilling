using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CyberBilling.Client;

public partial class LockScreenWindow :
    Window
{
    private readonly DispatcherTimer
        _clockTimer;

    private bool _allowClose;

    public LockScreenWindow()
    {
        InitializeComponent();

        LoadWallpaper();

        UpdateClock();

        _clockTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromSeconds(1)
            };

        _clockTimer.Tick +=
            (_, _) =>
                UpdateClock();

        _clockTimer.Start();
    }

    public void SetConnectionText(
        string text)
    {
        ConnectionText.Text =
            text;
    }

    public void AllowClose()
    {
        _allowClose =
            true;

        _clockTimer.Stop();
    }

    protected override void OnClosing(
        CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel =
                true;

            return;
        }

        base.OnClosing(
            e);
    }

    private void UpdateClock()
    {
        DateTime now =
            DateTime.Now;

        ClockText.Text =
            now.ToString(
                "HH:mm");

        CultureInfo culture =
            CultureInfo.GetCultureInfo(
                "vi-VN");

        DateText.Text =
            now.ToString(
                "dddd, dd/MM/yyyy",
                culture);
    }

    private void LoadWallpaper()
    {
        string? path =
            WindowsWallpaperProvider
                .TryGetWallpaperPath();

        if (string.IsNullOrWhiteSpace(
                path)
            || !File.Exists(
                path))
        {
            return;
        }

        try
        {
            var bitmap =
                new BitmapImage();

            bitmap.BeginInit();

            bitmap.CacheOption =
                BitmapCacheOption.OnLoad;

            bitmap.UriSource =
                new Uri(
                    path,
                    UriKind.Absolute);

            bitmap.EndInit();

            bitmap.Freeze();

            WallpaperImage.Source =
                bitmap;
        }
        catch
        {
            /*
             * Nếu Windows không cho đọc
             * wallpaper thì dùng nền mặc định.
             */
        }
    }

    public event Action<
        string,
        string>?
        AdminLoginRequested;

    private void AdminLoginButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        string username =
            AdminUsernameTextBox
                .Text
                .Trim();

        string password =
            AdminPasswordBox
                .Password;

        if (string.IsNullOrWhiteSpace(
                username)
            || string.IsNullOrWhiteSpace(
                password))
        {
            AdminLoginMessageText.Text =
                "Vui lòng nhập tài khoản và mật khẩu.";

            return;
        }

        AdminLoginButton.IsEnabled =
            false;

        AdminLoginMessageText.Text =
            "Đang kiểm tra...";

        AdminLoginRequested?
            .Invoke(
                username,
                password);
    }

    public void SetAdminLoginResult(
        bool success,
        string message)
    {
        AdminLoginMessageText.Text =
            message;

        AdminLoginButton.IsEnabled =
            true;

        if (!success)
        {
            AdminPasswordBox.Clear();

            AdminPasswordBox.Focus();
        }
    }
}