using System.Windows;
using System.Windows.Input;
using CyberBilling.Server.Models;

namespace CyberBilling.Server.Dialogs;

public partial class ChangeWorkstationDialog :
    Window
{
    public ChangeWorkstationDialog(
        WorkstationRow source,
        IReadOnlyList<WorkstationRow> targets)
    {
        InitializeComponent();

        SourceMachineText.Text =
            "Máy hiện tại: "
            + source.WorkstationNumberText
            + " - "
            + source.MachineName;

        TargetsGrid.ItemsSource =
            targets
                .Select(
                    target =>
                        new TargetRow(
                            target,
                            target.WorkstationNumberText,
                            target.MachineName,
                            target.Status,
                            target.IsSessionActive
                                ? "Hoán đổi"
                                : "Chuyển sang"))
                .ToList();
    }

    public WorkstationRow?
        SelectedWorkstation
    {
        get;
        private set;
    }

    private void ConfirmButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void TargetsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (TargetsGrid.SelectedItem
            is not TargetRow selected)
        {
            MessageBox.Show(
                "Vui lòng chọn máy trạm đích.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        SelectedWorkstation =
            selected.Workstation;

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

    private sealed record TargetRow(
        WorkstationRow Workstation,
        string NumberText,
        string MachineName,
        string Status,
        string ActionText);
}