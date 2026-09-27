using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CyberBilling.Server.Billing;
using CyberBilling.Server.Models;
using CyberBilling.Server.Persistence;

namespace CyberBilling.Server.Dialogs;

public partial class SessionServiceDialog :
    Window
{
    private readonly CyberBillingDatabase
        _database;

    private readonly WorkstationRow
        _workstation;

    private IReadOnlyList<ServiceSnapshot>
        _catalog =
            Array.Empty<ServiceSnapshot>();

    private readonly Dictionary<long, int>
        _quantities =
            new();

    private readonly Dictionary<
        long,
        ServiceSnapshot>
        _serviceInformation =
            new();

    public SessionServiceDialog(
        CyberBillingDatabase database,
        WorkstationRow workstation)
    {
        _database =
            database;

        _workstation =
            workstation;

        InitializeComponent();

        if (!_workstation
                .ActiveSessionId
                .HasValue)
        {
            throw new InvalidOperationException(
                "Máy trạm không có phiên sử dụng.");
        }

        MachineText.Text =
            "Máy "
            + _workstation
                .WorkstationNumberText
            + " - "
            + _workstation
                .MachineName;

        LoadData();
    }

    private void LoadData()
    {
        _catalog =
            _database.LoadServices();

        foreach (ServiceSnapshot service
                 in _catalog)
        {
            _serviceInformation[
                service.Id] =
                service;
        }

        IReadOnlyList<
            SessionServiceSnapshot>
            currentServices =
                _database
                    .LoadSessionServices(
                        _workstation
                            .ActiveSessionId!
                            .Value);

        foreach (SessionServiceSnapshot item
                 in currentServices)
        {
            _quantities[
                item.ServiceId] =
                item.Quantity;

            /*
             * Dịch vụ đã bị xóa khỏi danh mục
             * vẫn có thể hiện ở phía "đã chọn"
             * để người dùng xóa khỏi phiên.
             */
            _serviceInformation[
                item.ServiceId] =
                new ServiceSnapshot(
                    item.ServiceId,
                    item.Name,
                    item.Category,
                    item.Unit,
                    item.Price);
        }

        RefreshCatalog();

        RefreshSelectedServices();
    }

    private void RefreshCatalog()
    {
        string category =
            GetSelectedCategory();

        IEnumerable<ServiceSnapshot>
            filtered =
                _catalog;

        if (category != "*")
        {
            filtered =
                filtered.Where(
                    service =>
                        service.Category ==
                        category);
        }

        CatalogGrid.ItemsSource =
            filtered
                .Select(
                    service =>
                        new CatalogRow(
                            service.Id,
                            service.Name,
                            service.Unit,
                            service.Price,
                            BillingCalculator
                                .FormatMoney(
                                    service.Price)))
                .ToList();
    }

    private void RefreshSelectedServices()
    {
        List<SelectedServiceRow> rows =
            new();

        decimal total =
            0m;

        foreach (
            KeyValuePair<long, int> item
            in _quantities
                .Where(
                    item =>
                        item.Value > 0))
        {
            if (!_serviceInformation
                    .TryGetValue(
                        item.Key,
                        out ServiceSnapshot?
                            service))
            {
                continue;
            }

            decimal itemTotal =
                service.Price
                * item.Value;

            total +=
                itemTotal;

            rows.Add(
                new SelectedServiceRow(
                    service.Id,
                    service.Name,
                    service.Price,
                    item.Value,
                    BillingCalculator
                        .FormatMoney(
                            service.Price),
                    BillingCalculator
                        .FormatMoney(
                            itemTotal)));
        }

        SelectedServicesGrid.ItemsSource =
            rows
                .OrderBy(
                    row =>
                        row.Name)
                .ToList();

        TotalText.Text =
            BillingCalculator
                .FormatMoney(
                    total);
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

        RefreshCatalog();
    }

    private void AddButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        AddSelectedCatalogItem();
    }

    private void
        CatalogGrid_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        AddSelectedCatalogItem();
    }

    private void AddSelectedCatalogItem()
    {
        if (CatalogGrid.SelectedItem
            is not CatalogRow selected)
        {
            return;
        }

        if (_quantities.TryGetValue(
                selected.Id,
                out int quantity))
        {
            _quantities[
                selected.Id] =
                quantity + 1;
        }
        else
        {
            _quantities[
                selected.Id] =
                1;
        }

        RefreshSelectedServices();
    }

    private void IncreaseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SelectedServicesGrid
                .SelectedItem
            is not SelectedServiceRow selected)
        {
            return;
        }

        _quantities[
            selected.Id] =
            selected.Quantity + 1;

        RefreshSelectedServices();
    }

    private void DecreaseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SelectedServicesGrid
                .SelectedItem
            is not SelectedServiceRow selected)
        {
            return;
        }

        if (selected.Quantity <= 1)
        {
            _quantities.Remove(
                selected.Id);
        }
        else
        {
            _quantities[
                selected.Id] =
                selected.Quantity - 1;
        }

        RefreshSelectedServices();
    }

    private void RemoveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SelectedServicesGrid
                .SelectedItem
            is not SelectedServiceRow selected)
        {
            return;
        }

        _quantities.Remove(
            selected.Id);

        RefreshSelectedServices();
    }

    private void AcceptButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_workstation
                .ActiveSessionId
                .HasValue)
        {
            MessageBox.Show(
                "Phiên sử dụng đã kết thúc.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        decimal amount =
            _database
                .SaveSessionServices(
                    _workstation
                        .ActiveSessionId
                        .Value,
                    _quantities
                        .Select(
                            item =>
                                new SessionServiceUpdate(
                                    item.Key,
                                    item.Value)));

        _workstation.ServiceAmount =
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

    private sealed record CatalogRow(
        long Id,
        string Name,
        string Unit,
        decimal Price,
        string PriceText);

    private sealed record SelectedServiceRow(
        long Id,
        string Name,
        decimal Price,
        int Quantity,
        string PriceText,
        string TotalText);
}