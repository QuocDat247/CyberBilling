using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CyberBilling.Server.Billing;
using CyberBilling.Server.Dialogs;
using CyberBilling.Server.Models;
using CyberBilling.Server.Networking;

namespace CyberBilling.Server;

public partial class MainWindow :
    Window
{
    private decimal _currentHourlyRate =
        6000m;

    private decimal _minimumCharge =
        1000m;

    private readonly TcpBillingServer
        _billingServer = new();

    private readonly ObservableCollection<
        WorkstationRow>
        _workstations = new();

    private readonly DispatcherTimer
        _billingTimer;

    public MainWindow()
    {
        InitializeComponent();

        WorkstationsGrid.ItemsSource =
            _workstations;

        _billingServer
            .WorkstationConnectionChanged +=
            OnWorkstationConnectionChanged;

        _billingTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromSeconds(1)
            };

        _billingTimer.Tick +=
            OnBillingTimerTick;

        Loaded +=
            OnLoaded;

        Closed +=
            OnClosed;
    }

    private async void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await _billingServer
                .StartAsync();

            _billingTimer.Start();

            RefreshServerStatus();

            ListeningText.Text =
                $"ONLINE :{TcpBillingServer.DefaultPort}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Không thể khởi động Server",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Close();
        }
    }

    private void
        ApplyHourlyRateButton_Click(
            object sender,
            RoutedEventArgs e)
    {
        string rawHourlyRate =
            HourlyRateTextBox
                .Text
                .Trim()
                .Replace(".", string.Empty)
                .Replace(",", string.Empty);

        string rawMinimumCharge =
            MinimumChargeTextBox
                .Text
                .Trim()
                .Replace(".", string.Empty)
                .Replace(",", string.Empty);

        if (!decimal.TryParse(
                rawHourlyRate,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out decimal newHourlyRate)
            || newHourlyRate <= 0)
        {
            MessageBox.Show(
                "Giá giờ không hợp lệ.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (!decimal.TryParse(
                rawMinimumCharge,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out decimal newMinimumCharge)
            || newMinimumCharge < 0)
        {
            MessageBox.Show(
                "Tiền tối thiểu không hợp lệ.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        _currentHourlyRate =
            newHourlyRate;

        _minimumCharge =
            newMinimumCharge;

        /*
         * Giá mới áp dụng ngay cho TẤT CẢ
         * phiên đang chạy:
         *
         * - trả sau
         * - trả trước
         */
        foreach (WorkstationRow row
                 in _workstations)
        {
            if (row.IsSessionActive)
            {
                row.HourlyRate =
                    newHourlyRate;
            }
        }

        HourlyRateTextBox.Text =
            newHourlyRate.ToString(
                "0",
                CultureInfo.InvariantCulture);

        MinimumChargeTextBox.Text =
            newMinimumCharge.ToString(
                "0",
                CultureInfo.InvariantCulture);

        RefreshServerStatus();

        RefreshBillingValues();

        MessageBox.Show(
            "Đã áp dụng:\n\n"
            + "Giá giờ: "
            + BillingCalculator
                .FormatMoney(
                    newHourlyRate)
            + "/giờ\n"
            + "Tiền tối thiểu: "
            + BillingCalculator
                .FormatMoney(
                    newMinimumCharge),
            "CyberBilling",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RefreshServerStatus()
    {
        ServerStatusText.Text =
            "Port "
            + TcpBillingServer.DefaultPort
            + " | Giá giờ: "
            + BillingCalculator
                .FormatMoney(
                    _currentHourlyRate)
            + "/giờ"
            + " | Tối thiểu: "
            + BillingCalculator
                .FormatMoney(
                    _minimumCharge);
    }

    private void
        OnWorkstationConnectionChanged(
            object? sender,
            WorkstationConnectionInfo info)
    {
        Dispatcher.Invoke(
            () =>
            {
                WorkstationRow? row =
                    _workstations
                        .FirstOrDefault(
                            workstation =>
                                workstation.MachineId
                                == info.MachineId);

                if (row is null)
                {
                    row =
                        new WorkstationRow(
                            GetNextWorkstationNumber(),
                            info.MachineId,
                            info.MachineName,
                            info.State);

                    _workstations.Add(
                        row);
                }

                row.MachineName =
                    info.MachineName;

                row.ConnectionState =
                    info.State;

                ApplyConnectionAppearance(
                    row);
            });
    }

    private void
        WorkstationsGrid_PreviewMouseRightButtonDown(
            object sender,
            MouseButtonEventArgs e)
    {
        DependencyObject? source =
            e.OriginalSource
                as DependencyObject;

        DataGridRow? row =
            FindVisualParent<DataGridRow>(
                source);

        if (row is null)
        {
            WorkstationsGrid.SelectedItem =
                null;

            return;
        }

        row.IsSelected =
            true;

        WorkstationsGrid.SelectedItem =
            row.Item;
    }

    private static T?
        FindVisualParent<T>(
            DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T parent)
            {
                return parent;
            }

            child =
                VisualTreeHelper
                    .GetParent(
                        child);
        }

        return null;
    }

    private void
        StartPrepaidMenuItem_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (WorkstationsGrid
                .SelectedItem
            is not WorkstationRow row)
        {
            return;
        }

        if (row.ConnectionState !=
            WorkstationConnectionState
                .Online)
        {
            MessageBox.Show(
                "Máy trạm hiện không kết nối.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (row.IsSessionActive)
        {
            MessageBox.Show(
                "Máy trạm đang có phiên sử dụng.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var dialog =
            new PrepaidDialog(
                row.WorkstationNumberText
                + " - "
                + row.MachineName,
                _currentHourlyRate)
            {
                Owner =
                    this
            };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        StartPrepaidSession(
            row,
            dialog.PrepaidAmount);
    }

    private void
        WorkstationsGrid_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        if (WorkstationsGrid.SelectedItem
            is not WorkstationRow row)
        {
            return;
        }

        if (row.ConnectionState !=
            WorkstationConnectionState
                .Online)
        {
            return;
        }

        /*
         * Máy chưa có phiên:
         * double-click = trả sau.
         */
        if (!row.IsSessionActive)
        {
            StartPostpaidSession(
                row);

            return;
        }

        /*
         * Trả trước đã thanh toán rồi.
         * Không mở dialog tính tiền.
         */
        if (row.SessionMode ==
            SessionBillingMode.Prepaid)
        {
            return;
        }

        OpenBillingDialog(
            row);
    }

    private void OpenBillingDialog(
        WorkstationRow row)
    {
        List<WorkstationRow>
            otherActiveMachines =
                _workstations
                    .Where(
                        machine =>
                            machine != row
                            && machine.IsSessionActive
                            && machine.SessionMode ==
                                SessionBillingMode
                                    .Postpaid)
                    .ToList();

        var dialog =
            new BillingDialog(
                row,
                otherActiveMachines,
                _minimumCharge)
            {
                Owner =
                    this
            };

        bool? result =
            dialog.ShowDialog();

        if (result != true)
        {
            return;
        }

        if (dialog.Result ==
            BillingDialogResult.Cancelled)
        {
            return;
        }

        CompleteSession(
            row);

        if (dialog.LinkedMachine
            is not null)
        {
            CompleteSession(
                dialog.LinkedMachine);
        }

        if (dialog.Result ==
            BillingDialogResult
                .PayAndShutdown)
        {
            MessageBox.Show(
                "Đã thanh toán.\n\n"
                + "Lệnh tắt máy sẽ được nối "
                + "vào Client ở milestone "
                + "điều khiển máy trạm.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void StartPostpaidSession(
        WorkstationRow row)
    {
        DateTime now =
            DateTime.Now;

        row.IsSessionActive =
            true;

        row.SessionMode =
            SessionBillingMode.Postpaid;

        row.SessionStartedAt =
            now;

        row.HourlyRate =
            _currentHourlyRate;

        row.PrepaidAmount =
            0m;

        row.IsPrepaidExpired =
            false;

        row.ServiceAmount =
            0m;

        row.Status =
            "Đang sử dụng";

        row.StartTimeText =
            now.ToString(
                "HH:mm:ss");

        row.UsedTimeText =
            "00:00:00";

        row.RemainingTimeText =
            "999:99";

        row.AmountText =
            BillingCalculator
                .FormatMoney(
                    _minimumCharge);

        row.StartDateText =
            now.ToString(
                "dd/MM/yyyy");
    }

    private void StartPrepaidSession(
        WorkstationRow row,
        decimal prepaidAmount)
    {
        DateTime now =
            DateTime.Now;

        TimeSpan duration =
            BillingCalculator
                .CalculatePrepaidDuration(
                    prepaidAmount,
                    _currentHourlyRate);

        row.IsSessionActive =
            true;

        row.SessionMode =
            SessionBillingMode.Prepaid;

        row.SessionStartedAt =
            now;

        row.HourlyRate =
            _currentHourlyRate;

        row.PrepaidAmount =
            prepaidAmount;

        row.IsPrepaidExpired =
            false;

        row.ServiceAmount =
            0m;

        row.Status =
            "Trả trước";

        row.StartTimeText =
            now.ToString(
                "HH:mm:ss");

        row.UsedTimeText =
            "00:00:00";

        row.RemainingTimeText =
            BillingCalculator
                .FormatCountdownTime(
                    duration);

        /*
         * Cột số tiền của phiên trả trước
         * hiển thị số tiền khách đã nạp.
         */
        row.AmountText =
            BillingCalculator
                .FormatMoney(
                    prepaidAmount);

        row.StartDateText =
            now.ToString(
                "dd/MM/yyyy");
    }

    private static void CompleteSession(
        WorkstationRow row)
    {
        row.IsSessionActive =
            false;

        row.SessionMode =
            SessionBillingMode.None;

        row.SessionStartedAt =
            null;

        row.PrepaidAmount =
            0m;

        row.IsPrepaidExpired =
            false;

        row.ServiceAmount =
            0m;

        row.StartTimeText =
            "--";

        row.UsedTimeText =
            "--";

        row.RemainingTimeText =
            "--";

        row.AmountText =
            "0 đ";

        row.StartDateText =
            "--";

        row.Status =
            row.ConnectionState ==
            WorkstationConnectionState
                .Online
                ? "Sẵn sàng"
                : "Đã tắt";
    }

    private void OnBillingTimerTick(
        object? sender,
        EventArgs e)
    {
        RefreshBillingValues();
    }

    private void RefreshBillingValues()
    {
        DateTime now =
            DateTime.Now;

        foreach (WorkstationRow row
                 in _workstations)
        {
            if (!row.IsSessionActive
                || row.SessionStartedAt
                    is null)
            {
                continue;
            }

            TimeSpan elapsed =
                now -
                row.SessionStartedAt.Value;

            if (elapsed < TimeSpan.Zero)
            {
                elapsed =
                    TimeSpan.Zero;
            }

            row.UsedTimeText =
                BillingCalculator
                    .FormatUsedTimeDetailed(
                        elapsed);

            if (row.SessionMode ==
                SessionBillingMode.Postpaid)
            {
                RefreshPostpaidRow(
                    row,
                    elapsed);

                continue;
            }

            if (row.SessionMode ==
                SessionBillingMode.Prepaid)
            {
                RefreshPrepaidRow(
                    row,
                    elapsed);
            }
        }
    }

    private void RefreshPostpaidRow(
        WorkstationRow row,
        TimeSpan elapsed)
    {
        decimal amount =
            BillingCalculator
                .CalculateUsageAmount(
                    elapsed,
                    row.HourlyRate,
                    _minimumCharge);

        row.Status =
            "Đang sử dụng";

        row.RemainingTimeText =
            "999:99";

        row.AmountText =
            BillingCalculator
                .FormatMoney(
                    amount);
    }

    private static void RefreshPrepaidRow(
        WorkstationRow row,
        TimeSpan elapsed)
    {
        /*
         * Vì HourlyRate của row được thay
         * ngay khi Admin đổi giá, tổng thời
         * gian trả trước cũng tự tính lại.
         */
        TimeSpan totalDuration =
            BillingCalculator
                .CalculatePrepaidDuration(
                    row.PrepaidAmount,
                    row.HourlyRate);

        TimeSpan remaining =
            totalDuration -
            elapsed;

        row.AmountText =
            BillingCalculator
                .FormatMoney(
                    row.PrepaidAmount);

        if (remaining <= TimeSpan.Zero)
        {
            row.RemainingTimeText =
                "00:00:00";

            row.Status =
                "Hết giờ";

            row.IsPrepaidExpired =
                true;

            return;
        }

        row.IsPrepaidExpired =
            false;

        row.Status =
            "Trả trước";

        row.RemainingTimeText =
            BillingCalculator
                .FormatCountdownTime(
                    remaining);
    }

    private void ApplyConnectionAppearance(
        WorkstationRow row)
    {
        if (row.ConnectionState ==
            WorkstationConnectionState
                .ConnectionLost)
        {
            row.Status =
                "Mất kết nối";

            row.MachineNameBackground =
                new SolidColorBrush(
                    Color.FromRgb(
                        180,
                        35,
                        24));

            return;
        }

        if (row.ConnectionState ==
            WorkstationConnectionState
                .GracefulShutdown)
        {
            row.Status =
                "Đã tắt";

            row.MachineNameBackground =
                new SolidColorBrush(
                    Color.FromRgb(
                        31,
                        138,
                        76));

            return;
        }

        row.MachineNameBackground =
            Brushes.Transparent;

        if (!row.IsSessionActive)
        {
            row.Status =
                "Sẵn sàng";

            return;
        }

        if (row.SessionMode ==
            SessionBillingMode.Prepaid)
        {
            row.Status =
                row.IsPrepaidExpired
                    ? "Hết giờ"
                    : "Trả trước";

            return;
        }

        row.Status =
            "Đang sử dụng";
    }

    private int GetNextWorkstationNumber()
    {
        if (_workstations.Count == 0)
        {
            return 1;
        }

        return _workstations
            .Max(
                workstation =>
                    workstation
                        .WorkstationNumber)
            + 1;
    }

    private async void OnClosed(
        object? sender,
        EventArgs e)
    {
        _billingTimer.Stop();

        _billingTimer.Tick -=
            OnBillingTimerTick;

        _billingServer
            .WorkstationConnectionChanged -=
            OnWorkstationConnectionChanged;

        await _billingServer
            .DisposeAsync();
    }
}