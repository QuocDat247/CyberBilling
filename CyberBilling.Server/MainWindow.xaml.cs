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
using CyberBilling.Server.Persistence;

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

    private readonly CyberBillingDatabase
        _database = new();

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
            /*
             * DB phải được khởi tạo và
             * phục hồi trước khi mở TCP Server.
             */
            _database.Initialize();

            LoadPersistedState();

            await _billingServer
                .StartAsync();

            _billingTimer.Start();

            RefreshBillingValues();

            RefreshServerStatus();

            ListeningText.Text =
                $"ĐANG HOẠT ĐỘNG :{TcpBillingServer.DefaultPort}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Không thể khởi động máy tính tiền",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Close();
        }
    }

    private void LoadPersistedState()
    {
        BillingSettingsSnapshot settings =
            _database.LoadSettings();

        _currentHourlyRate =
            settings.HourlyRate;

        _minimumCharge =
            settings.MinimumCharge;

        HourlyRateTextBox.Text =
            _currentHourlyRate.ToString(
                "0",
                CultureInfo.InvariantCulture);

        MinimumChargeTextBox.Text =
            _minimumCharge.ToString(
                "0",
                CultureInfo.InvariantCulture);

        foreach (WorkstationSnapshot saved
                 in _database
                     .LoadWorkstations())
        {
            var row =
                new WorkstationRow(
                    saved.WorkstationNumber,
                    saved.MachineId,
                    saved.MachineName,
                    WorkstationConnectionState
                        .ConnectionLost);

            /*
             * Đây chỉ là trạng thái trong lúc
             * Server vừa mở và đang chờ Client
             * tự reconnect.
             *
             * Không tô đỏ ngay tại startup.
             */
            row.Status =
                "Chờ kết nối";

            row.MachineNameBackground =
                Brushes.Transparent;

            _workstations.Add(
                row);
        }

        foreach (ActiveSessionSnapshot session
                 in _database
                     .LoadActiveSessions())
        {
            WorkstationRow? row =
                _workstations
                    .FirstOrDefault(
                        workstation =>
                            workstation.MachineId
                            == session.MachineId);

            if (row is null)
            {
                continue;
            }

            RestoreActiveSession(
                row,
                session);
        }
    }

    private static void RestoreActiveSession(
        WorkstationRow row,
        ActiveSessionSnapshot session)
    {
        row.ActiveSessionId =
            session.Id;

        row.IsSessionActive =
            true;

        row.SessionMode =
            session.Mode;

        row.SessionStartedAt =
            session.StartedAt;

        row.SessionPausedAt =
            session.PausedAt;

        row.AccumulatedPausedSeconds =
            session.AccumulatedPausedSeconds;

        row.HourlyRate =
            session.HourlyRate;

        row.PrepaidAmount =
            session.PrepaidAmount;

        row.ServiceAmount =
            session.ServiceAmount;

        row.IsPrepaidExpired =
            false;

        row.StartTimeText =
            session.StartedAt.ToString(
                "HH:mm:ss");

        row.StartDateText =
            session.StartedAt.ToString(
                "dd/MM/yyyy");

        row.UsedTimeText =
            "00:00:00";

        if (session.Mode ==
            SessionBillingMode.Prepaid)
        {
            row.Status =
                "Trả trước";

            row.RemainingTimeText =
                "00:00:00";

            /*
             * Theo yêu cầu:
             * phiên trả trước không hiện số
             * tiền đã nạp trên bảng chính.
             */
            row.AmountText =
                "0 đ";

            return;
        }

        row.Status =
            "Đang sử dụng";

        row.RemainingTimeText =
            "999:99";

        row.AmountText =
            "0 đ";
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

        try
        {
            /*
             * Transaction này lưu Settings
             * và đổi HourlyRate của tất cả
             * session Active cùng lúc.
             */
            _database
                .SavePricingAndApplyToActiveSessions(
                    newHourlyRate,
                    newMinimumCharge);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Không thể lưu giá mới.\n\n"
                + ex.Message,
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        _currentHourlyRate =
            newHourlyRate;

        _minimumCharge =
            newMinimumCharge;

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
            "Cổng "
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

                if (row.IsSessionActive)
                {
                    if (info.State ==
                        WorkstationConnectionState.Online)
                    {
                        ResumeSessionBilling(
                            row);
                    }
                    else
                    {
                        PauseSessionBilling(
                            row);
                    }
                }

                /*
                 * MachineId giữ nguyên nên số máy
                 * sẽ sống qua restart Server.
                 */
                _database.UpsertWorkstation(
                    row.WorkstationNumber,
                    row.MachineId,
                    row.MachineName);

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

    private void WorkstationContextMenu_Opened(
    object sender,
    RoutedEventArgs e)
    {
        bool hasRow =
            WorkstationsGrid.SelectedItem
            is WorkstationRow;

        if (!hasRow)
        {
            StartPrepaidMenuItem.Visibility =
                Visibility.Collapsed;

            PayPrepaidMenuItem.Visibility =
                Visibility.Collapsed;

            BillPostpaidMenuItem.Visibility =
                Visibility.Collapsed;

            ServiceMenuItem.Visibility =
                Visibility.Collapsed;

            return;
        }

        WorkstationRow row =
            (WorkstationRow)
                WorkstationsGrid.SelectedItem;

        StartPrepaidMenuItem.Visibility =
            !row.IsSessionActive
            && row.ConnectionState ==
                WorkstationConnectionState.Online
                ? Visibility.Visible
                : Visibility.Collapsed;

        PayPrepaidMenuItem.Visibility =
            row.IsSessionActive
            && row.SessionMode ==
                SessionBillingMode.Prepaid
                ? Visibility.Visible
                : Visibility.Collapsed;

        BillPostpaidMenuItem.Visibility =
            row.IsSessionActive
            && row.SessionMode ==
                SessionBillingMode.Postpaid
                ? Visibility.Visible
                : Visibility.Collapsed;

        ServiceMenuItem.Visibility =
            row.IsSessionActive
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void
        StartPrepaidMenuItem_Click(
            object sender,
            RoutedEventArgs e)
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
            MessageBox.Show(
                "Máy trạm hiện không kết nối.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (row.IsSessionActive)
        {
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

        try
        {
            StartPrepaidSession(
                row,
                dialog.PrepaidAmount);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Không thể bắt đầu phiên trả trước.\n\n"
                + ex.Message,
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void PayPrepaidMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (WorkstationsGrid.SelectedItem
            is not WorkstationRow row)
        {
            return;
        }

        if (!row.IsSessionActive
            || row.SessionMode !=
                SessionBillingMode.Prepaid)
        {
            return;
        }

        MessageBoxResult confirmation =
            MessageBox.Show(
                "Kết thúc phiên trả trước của máy "
                + row.WorkstationNumberText
                + " - "
                + row.MachineName
                + "?",
                "Thanh toán",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            CompletePrepaidSession(
                row);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Không thể hoàn tất phiên.\n\n"
                + ex.Message,
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void BillPostpaidMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (WorkstationsGrid.SelectedItem
            is not WorkstationRow row)
        {
            return;
        }

        if (!row.IsSessionActive
            || row.SessionMode !=
                SessionBillingMode.Postpaid)
        {
            return;
        }

        OpenBillingDialog(
            row);
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

        if (!row.IsSessionActive)
        {
            try
            {
                StartPostpaidSession(
                    row);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể bắt đầu phiên.\n\n"
                    + ex.Message,
                    "CyberBilling",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        /*
         * Trả trước đã trả tiền từ đầu.
         * Double-click không mở BillingDialog.
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
                            && machine
                                .IsSessionActive
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

        if (result != true
            || dialog.Result ==
                BillingDialogResult.Cancelled)
        {
            return;
        }

        try
        {
            DateTime calculatedAt =
                dialog.CalculatedAt;

            List<SessionSettlement>
                settlements =
                    new();

            List<WorkstationRow>
                completedRows =
                    new();

            settlements.Add(
                CreateSettlement(
                    row,
                    dialog.PrimaryUsageAmount,
                    calculatedAt));

            completedRows.Add(
                row);

            if (dialog.LinkedMachine
                is not null)
            {
                settlements.Add(
                    CreateSettlement(
                        dialog.LinkedMachine,
                        dialog.LinkedUsageAmount,
                        calculatedAt));

                completedRows.Add(
                    dialog.LinkedMachine);
            }

            _database.CompletePayment(
                settlements,
                dialog.CalculatedAmount,
                dialog.PayableAmount,
                DateTime.Now);

            foreach (WorkstationRow
                     completedRow
                     in completedRows)
            {
                ClearSessionState(
                    completedRow);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Không thể lưu thanh toán.\n\n"
                + ex.Message,
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
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

        long sessionId =
            _database.CreateActiveSession(
                row.MachineId,
                SessionBillingMode.Postpaid,
                now,
                _currentHourlyRate,
                0m,
                0m);

        row.ActiveSessionId =
            sessionId;

        row.IsSessionActive =
            true;

        row.SessionMode =
            SessionBillingMode.Postpaid;

        row.SessionStartedAt =
            now;

        row.SessionPausedAt =
            null;

        row.AccumulatedPausedSeconds =
            0;

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

        long sessionId =
            _database.CreateActiveSession(
                row.MachineId,
                SessionBillingMode.Prepaid,
                now,
                _currentHourlyRate,
                prepaidAmount,
                0m);

        row.ActiveSessionId =
            sessionId;

        row.IsSessionActive =
            true;

        row.SessionMode =
            SessionBillingMode.Prepaid;

        row.SessionStartedAt =
            now;

        row.SessionPausedAt =
            null;

        row.AccumulatedPausedSeconds =
            0;

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
         * QUY TẮC MỚI:
         * trả trước vẫn giữ PrepaidAmount
         * nội bộ nhưng bảng chính chỉ hiện 0.
         */
        row.AmountText =
            "0 đ";

        row.StartDateText =
            now.ToString(
                "dd/MM/yyyy");
    }

    private void ClearSessionState(
        WorkstationRow row)
    {
        row.ActiveSessionId =
            null;

        row.IsSessionActive =
            false;

        row.SessionMode =
            SessionBillingMode.None;

        row.SessionStartedAt =
            null;

        row.SessionPausedAt =
            null;

        row.AccumulatedPausedSeconds =
            0;

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

        ApplyConnectionAppearance(
            row);
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
                row.GetBillableElapsed(
                    now);

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

        /*
         * Billing timer không được phép
         * ghi đè trạng thái kết nối.
         *
         * Offline:
         * - Mất kết nối
         * - Đã tắt
         *
         * phải được giữ nguyên.
         */
        if (row.ConnectionState ==
            WorkstationConnectionState.Online)
        {
            row.Status =
                "Đang sử dụng";
        }

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
        TimeSpan totalDuration =
            BillingCalculator
                .CalculatePrepaidDuration(
                    row.PrepaidAmount,
                    row.HourlyRate);

        TimeSpan remaining =
            totalDuration -
            elapsed;

        /*
         * Trả trước không hiển thị tiền
         * trên bảng chính.
         */
        row.AmountText =
            "0 đ";

        if (remaining <= TimeSpan.Zero)
        {
            row.RemainingTimeText =
                "00:00:00";

            row.IsPrepaidExpired =
                true;

            /*
             * Nếu máy đang mất kết nối hoặc
             * đã tắt thì trạng thái kết nối
             * phải được ưu tiên hiển thị.
             */
            if (row.ConnectionState ==
                WorkstationConnectionState.Online)
            {
                row.Status =
                    "Hết giờ";
            }

            return;
        }

        row.IsPrepaidExpired =
            false;

        if (row.ConnectionState ==
            WorkstationConnectionState.Online)
        {
            row.Status =
                "Trả trước";
        }

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

        if (row.ServiceAmount > 0m)
        {
            row.MachineNameBackground =
                new SolidColorBrush(
                    Color.FromRgb(
                        37,
                        99,
                        235));
        }
        else
        {
            row.MachineNameBackground =
                Brushes.Transparent;
        }

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

    private void MoneyTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            MoneyInputFormatter.Format(
                textBox);
        }
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

    private void PauseSessionBilling(
    WorkstationRow row)
    {
        if (!row.IsSessionActive
            || !row.ActiveSessionId.HasValue
            || row.SessionPausedAt.HasValue)
        {
            return;
        }

        DateTime now =
            DateTime.Now;

        _database.PauseActiveSession(
            row.ActiveSessionId.Value,
            now);

        row.SessionPausedAt =
            now;
    }

    private void ResumeSessionBilling(
        WorkstationRow row)
    {
        if (!row.IsSessionActive
            || !row.ActiveSessionId.HasValue
            || !row.SessionPausedAt.HasValue)
        {
            return;
        }

        DateTime now =
            DateTime.Now;

        TimeSpan pauseDuration =
            now -
            row.SessionPausedAt.Value;

        long addedSeconds =
            Math.Max(
                0,
                (long)Math.Floor(
                    pauseDuration
                        .TotalSeconds));

        _database.ResumeActiveSession(
            row.ActiveSessionId.Value,
            addedSeconds);

        row.AccumulatedPausedSeconds +=
            addedSeconds;

        row.SessionPausedAt =
            null;
    }

    private void CompletePrepaidSession(
        WorkstationRow row)
    {
        if (!row.ActiveSessionId.HasValue)
        {
            throw new InvalidOperationException(
                "Không tìm thấy phiên trả trước.");
        }

        DateTime now =
            DateTime.Now;

        TimeSpan elapsed =
            row.GetBillableElapsed(
                now);

        long billableSeconds =
            Math.Max(
                0,
                (long)Math.Floor(
                    elapsed.TotalSeconds));

        /*
         * Trả trước:
         * tiền giờ chính là số tiền khách
         * đã nạp từ đầu.
         */
        decimal usageAmount =
            row.PrepaidAmount;

        decimal calculatedAmount =
            usageAmount
            + row.ServiceAmount;

        decimal paidAmount =
            BillingCalculator
                .RoundPayableAmount(
                    calculatedAmount);

        var settlement =
            new SessionSettlement(
                row.ActiveSessionId.Value,
                billableSeconds,
                usageAmount,
                row.ServiceAmount);

        _database.CompletePayment(
            new[]
            {
            settlement
            },
            calculatedAmount,
            paidAmount,
            now);

        ClearSessionState(
            row);
    }

    private static SessionSettlement
    CreateSettlement(
        WorkstationRow row,
        decimal usageAmount,
        DateTime calculatedAt)
    {
        if (!row.ActiveSessionId.HasValue)
        {
            throw new InvalidOperationException(
                "Không tìm thấy phiên đang hoạt động.");
        }

        TimeSpan elapsed =
            row.GetBillableElapsed(
                calculatedAt);

        long billableSeconds =
            Math.Max(
                0,
                (long)Math.Floor(
                    elapsed.TotalSeconds));

        return new SessionSettlement(
            row.ActiveSessionId.Value,
            billableSeconds,
            usageAmount,
            row.ServiceAmount);
    }

    private void PaymentHistoryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            IReadOnlyList<
                PaymentHistorySnapshot>
                history =
                    _database
                        .LoadPaymentHistory();

            var window =
                new PaymentHistoryWindow(
                    history)
                {
                    Owner =
                        this
                };

            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Không thể đọc lịch sử thanh toán.\n\n"
                + ex.Message,
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ServiceManagementButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var window =
                new ServiceManagementWindow(
                    _database)
                {
                    Owner =
                        this
                };

            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Không thể mở quản lý dịch vụ.\n\n"
                + ex.Message,
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ServiceMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (WorkstationsGrid.SelectedItem
            is not WorkstationRow row)
        {
            return;
        }

        if (!row.IsSessionActive
            || !row.ActiveSessionId.HasValue)
        {
            MessageBox.Show(
                "Máy trạm chưa có phiên sử dụng.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        try
        {
            var dialog =
                new SessionServiceDialog(
                    _database,
                    row)
                {
                    Owner =
                        this
                };

            if (dialog.ShowDialog() == true)
            {
                ApplyConnectionAppearance(
                    row);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Không thể mở dịch vụ máy trạm.\n\n"
                + ex.Message,
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}