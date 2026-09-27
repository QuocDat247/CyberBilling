using System.Windows;
using System.Windows.Controls;
using CyberBilling.Server.Billing;
using CyberBilling.Server.Models;

namespace CyberBilling.Server.Dialogs;

public partial class BillingDialog :
    Window
{
    private readonly decimal
        _minimumCharge;

    private readonly WorkstationRow
        _primaryMachine;

    private readonly IReadOnlyList<
        WorkstationRow>
        _availableMachines;

    public BillingDialog(
        WorkstationRow primaryMachine,
        IReadOnlyList<WorkstationRow>
            availableMachines,
        decimal minimumCharge)
    {
        InitializeComponent();

        _primaryMachine =
            primaryMachine;

        _minimumCharge =
            minimumCharge;

        _availableMachines =
            availableMachines;

        LinkedMachineComboBox
            .DisplayMemberPath =
            nameof(
                MachineOption.DisplayName);

        LinkedMachineComboBox
            .Items.Add(
                new MachineOption(
                    null,
                    "(Không thanh toán thêm máy khác)"));

        foreach (WorkstationRow machine
                 in _availableMachines)
        {
            LinkedMachineComboBox
                .Items.Add(
                    new MachineOption(
                        machine,
                        $"{machine.WorkstationNumberText}"
                        + " - "
                        + machine.MachineName));
        }

        LinkedMachineComboBox
            .SelectedIndex =
            0;

        RefreshValues();
    }

    public BillingDialogResult Result
    {
        get;
        private set;
    } =
        BillingDialogResult.Cancelled;

    public WorkstationRow?
        LinkedMachine
    {
        get;
        private set;
    }

    private void
        LinkedMachineComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (LinkedMachineComboBox
                .SelectedItem
            is MachineOption option)
        {
            LinkedMachine =
                option.Machine;
        }
        else
        {
            LinkedMachine =
                null;
        }

        RefreshValues();
    }

    private void RefreshValues()
    {
        DateTime now =
            DateTime.Now;

        MachineNameText.Text =
            $"{_primaryMachine.WorkstationNumberText}"
            + " - "
            + _primaryMachine.MachineName;

        decimal primaryUsage =
            CalculateMachineUsage(
                _primaryMachine,
                now);

        UsedTimeText.Text =
            GetUsedTime(
                _primaryMachine,
                now);

        UsageAmountText.Text =
            BillingCalculator
                .FormatMoney(
                    primaryUsage);

        ServiceAmountText.Text =
            BillingCalculator
                .FormatMoney(
                    _primaryMachine
                        .ServiceAmount);

        decimal linkedUsage =
            0m;

        decimal linkedServices =
            0m;

        if (LinkedMachine is not null)
        {
            linkedUsage =
                CalculateMachineUsage(
                    LinkedMachine,
                    now);

            linkedServices =
                LinkedMachine
                    .ServiceAmount;
        }

        LinkedUsageAmountText.Text =
            BillingCalculator
                .FormatMoney(
                    linkedUsage);

        LinkedServiceAmountText.Text =
            BillingCalculator
                .FormatMoney(
                    linkedServices);

        decimal calculated =
            primaryUsage
            + _primaryMachine.ServiceAmount
            + linkedUsage
            + linkedServices;

        decimal payable =
            BillingCalculator
                .RoundPayableAmount(
                    calculated);

        CalculatedAmountText.Text =
            BillingCalculator
                .FormatMoney(
                    calculated);

        PayableAmountText.Text =
            BillingCalculator
                .FormatMoney(
                    payable);

        LargePayableAmountText.Text =
            BillingCalculator
                .FormatMoney(
                    payable);
    }

    private decimal CalculateMachineUsage(
        WorkstationRow machine,
        DateTime now)
    {
        if (!machine.IsSessionActive
            || machine.SessionStartedAt
                is null)
        {
            return 0m;
        }

        return BillingCalculator
            .CalculateUsageAmount(
                now -
                machine
                    .SessionStartedAt
                    .Value,
                machine.HourlyRate,
                _minimumCharge);
    }

    private static string GetUsedTime(
        WorkstationRow machine,
        DateTime now)
    {
        if (!machine.IsSessionActive
            || machine.SessionStartedAt
                is null)
        {
            return "--";
        }

        return BillingCalculator
            .FormatUsedTime(
                now -
                machine
                    .SessionStartedAt
                    .Value);
    }

    private void PayButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Result =
            BillingDialogResult.Pay;

        DialogResult =
            true;
    }

    private void PayAndShutdownButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Result =
            BillingDialogResult
                .PayAndShutdown;

        DialogResult =
            true;
    }

    private void CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Result =
            BillingDialogResult.Cancelled;

        DialogResult =
            false;
    }

    private sealed record MachineOption(
        WorkstationRow? Machine,
        string DisplayName);
}

public enum BillingDialogResult
{
    Cancelled = 0,

    Pay = 1,

    PayAndShutdown = 2
}