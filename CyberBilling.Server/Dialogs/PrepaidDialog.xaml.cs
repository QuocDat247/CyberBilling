using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CyberBilling.Server.Billing;

namespace CyberBilling.Server.Dialogs;

public partial class PrepaidDialog :
    Window
{
    private readonly decimal
        _hourlyRate;

    public PrepaidDialog(
        string workstationName,
        decimal hourlyRate)
    {
        _hourlyRate =
            hourlyRate;

        InitializeComponent();

        MachineText.Text =
            workstationName;

        HourlyRateText.Text =
            "Giá hiện tại: "
            + BillingCalculator
                .FormatMoney(
                    hourlyRate)
            + "/giờ";

        AmountTextBox.Focus();
    }

    public decimal PrepaidAmount
    {
        get;
        private set;
    }

    private void AmountTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (!TryReadAmount(
                out decimal amount))
        {
            DurationText.Text =
                "--:--:--";

            return;
        }

        TimeSpan duration =
            BillingCalculator
                .CalculatePrepaidDuration(
                    amount,
                    _hourlyRate);

        DurationText.Text =
            BillingCalculator
                .FormatCountdownTime(
                    duration);
    }

    private void StartButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryReadAmount(
                out decimal amount))
        {
            MessageBox.Show(
                "Số tiền không hợp lệ.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        /*
         * Giữ tiền mặt gọn theo
         * các mốc 1.000 đồng.
         */
        if (amount < 1000m
            || amount % 1000m != 0m)
        {
            MessageBox.Show(
                "Số tiền trả trước phải là "
                + "bội số của 1.000 đồng.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        PrepaidAmount =
            amount;

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

    private bool TryReadAmount(
        out decimal amount)
    {
        string raw =
            AmountTextBox
                .Text
                .Trim()
                .Replace(".", string.Empty)
                .Replace(",", string.Empty);

        return decimal.TryParse(
                   raw,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out amount)
               && amount > 0;
    }
}