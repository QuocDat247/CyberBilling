using System.Windows;
using CyberBilling.Server.Billing;
using CyberBilling.Server.Persistence;

namespace CyberBilling.Server.Dialogs;

public partial class PaymentHistoryWindow :
    Window
{
    public PaymentHistoryWindow(
        IReadOnlyList<
            PaymentHistorySnapshot>
            history)
    {
        InitializeComponent();

        List<HistoryRow> rows =
            history
                .Select(
                    item =>
                        new HistoryRow(
                            "#"
                            + item.PaymentId
                                .ToString("000000"),
                            item.Machines,
                            item.Modes,
                            item.StartedAt.ToString(
                                "dd/MM/yyyy HH:mm:ss"),
                            item.EndedAt.ToString(
                                "dd/MM/yyyy HH:mm:ss"),
                            BillingCalculator
                                .FormatUsedTimeDetailed(
                                    TimeSpan.FromSeconds(
                                        item.BillableSeconds)),
                            BillingCalculator
                                .FormatMoney(
                                    item.UsageAmount),
                            BillingCalculator
                                .FormatMoney(
                                    item.ServiceAmount),
                            BillingCalculator
                                .FormatMoney(
                                    item.CalculatedAmount),
                            BillingCalculator
                                .FormatMoney(
                                    item.PaidAmount),
                            item.PaidAt.ToString(
                                "dd/MM/yyyy HH:mm:ss")))
                .ToList();

        HistoryGrid.ItemsSource =
            rows;

        decimal totalPaid =
            history.Sum(
                item =>
                    item.PaidAmount);

        SummaryText.Text =
            history.Count
            + " giao dịch"
            + " | Tổng thực thu: "
            + BillingCalculator
                .FormatMoney(
                    totalPaid);
    }

    private sealed record HistoryRow(
        string PaymentIdText,
        string Machines,
        string Modes,
        string StartedAtText,
        string EndedAtText,
        string DurationText,
        string UsageAmountText,
        string ServiceAmountText,
        string CalculatedAmountText,
        string PaidAmountText,
        string PaidAtText);
}