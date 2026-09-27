using System.Windows;
using System.Windows.Controls;
using CyberBilling.Server.Billing;
using CyberBilling.Server.Persistence;

namespace CyberBilling.Server.Dialogs;

public partial class ServiceManagementWindow :
    Window
{
    private readonly CyberBillingDatabase
        _database;

    private IReadOnlyList<ServiceSnapshot>
        _services =
            Array.Empty<ServiceSnapshot>();

    public ServiceManagementWindow(
        CyberBillingDatabase database)
    {
        _database =
            database;

        InitializeComponent();

        LoadServices();
    }

    private void LoadServices()
    {
        _services =
            _database.LoadServices();

        RefreshGrid();
    }

    private void RefreshGrid()
    {
        string category =
            GetSelectedCategory();

        IEnumerable<ServiceSnapshot>
            filtered =
                _services;

        if (category != "*")
        {
            filtered =
                filtered.Where(
                    service =>
                        service.Category ==
                        category);
        }

        ServicesGrid.ItemsSource =
            filtered
                .Select(
                    service =>
                        new ServiceRow(
                            service.Id,
                            service.Name,
                            service.Category,
                            service.Unit,
                            service.Price,
                            BillingCalculator
                                .FormatMoney(
                                    service.Price)))
                .ToList();
    }

    private string GetSelectedCategory()
    {
        if (CategoryTabs.SelectedItem
            is TabItem tab
            && tab.Tag is string category)
        {
            return category;
        }

        return "*";
    }

    private void
        CategoryTabs_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(
                e.Source,
                CategoryTabs))
        {
            return;
        }

        RefreshGrid();
    }

    private void AddButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new ServiceEditorDialog
            {
                Owner =
                    this
            };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _database.AddService(
            dialog.EnteredName,
            dialog.EnteredCategory,
            dialog.EnteredUnit,
            dialog.EnteredPrice);

        LoadServices();
    }

    private void EditButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ServicesGrid.SelectedItem
            is not ServiceRow selected)
        {
            MessageBox.Show(
                "Vui lòng chọn dịch vụ cần sửa.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var service =
            new ServiceSnapshot(
                selected.Id,
                selected.Name,
                selected.Category,
                selected.Unit,
                selected.Price);

        var dialog =
            new ServiceEditorDialog(
                service)
            {
                Owner =
                    this
            };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _database.UpdateService(
            selected.Id,
            dialog.EnteredName,
            dialog.EnteredCategory,
            dialog.EnteredUnit,
            dialog.EnteredPrice);

        LoadServices();
    }

    private void DeleteButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ServicesGrid.SelectedItem
            is not ServiceRow selected)
        {
            MessageBox.Show(
                "Vui lòng chọn dịch vụ cần xóa.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        MessageBoxResult result =
            MessageBox.Show(
                "Xóa dịch vụ \""
                + selected.Name
                + "\"?",
                "CyberBilling",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (result !=
            MessageBoxResult.Yes)
        {
            return;
        }

        _database.DeleteService(
            selected.Id);

        LoadServices();
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private sealed record ServiceRow(
        long Id,
        string Name,
        string Category,
        string Unit,
        decimal Price,
        string PriceText);
}