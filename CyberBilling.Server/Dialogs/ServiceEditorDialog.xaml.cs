using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CyberBilling.Server.Billing;
using CyberBilling.Server.Persistence;

namespace CyberBilling.Server.Dialogs;

public partial class ServiceEditorDialog :
    Window
{
    private static readonly string[]
        Categories =
        {
            "Thức ăn",
            "Thức uống",
            "Thuốc lá"
        };

    public ServiceEditorDialog(
        ServiceSnapshot? service = null)
    {
        InitializeComponent();

        CategoryComboBox.ItemsSource =
            Categories;

        CategoryComboBox.SelectedIndex =
            0;

        if (service is null)
        {
            UnitTextBox.Text =
                "Cái";

            return;
        }

        TitleText.Text =
            "SỬA DỊCH VỤ";

        NameTextBox.Text =
            service.Name;

        CategoryComboBox.SelectedItem =
            service.Category;

        UnitTextBox.Text =
            service.Unit;

        PriceTextBox.Text =
            service.Price.ToString(
                "0",
                CultureInfo.InvariantCulture);
    }

    public string EnteredName
    {
        get;
        private set;
    } =
        string.Empty;

    public string EnteredCategory
    {
        get;
        private set;
    } =
        string.Empty;

    public string EnteredUnit
    {
        get;
        private set;
    } =
        string.Empty;

    public decimal EnteredPrice
    {
        get;
        private set;
    }

    private void PriceTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        MoneyInputFormatter.Format(
            PriceTextBox);
    }

    private void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        string name =
            NameTextBox.Text.Trim();

        string unit =
            UnitTextBox.Text.Trim();

        string? category =
            CategoryComboBox
                .SelectedItem
                as string;

        if (string.IsNullOrWhiteSpace(
                name))
        {
            MessageBox.Show(
                "Vui lòng nhập tên dịch vụ.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (string.IsNullOrWhiteSpace(
                category))
        {
            MessageBox.Show(
                "Vui lòng chọn nhóm dịch vụ.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (string.IsNullOrWhiteSpace(
                unit))
        {
            MessageBox.Show(
                "Vui lòng nhập đơn vị.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (!MoneyInputFormatter.TryParse(
                PriceTextBox.Text,
                out decimal price)
            || price <= 0)
        {
            MessageBox.Show(
                "Giá bán không hợp lệ.",
                "CyberBilling",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        EnteredName =
            name;

        EnteredCategory =
            category;

        EnteredUnit =
            unit;

        EnteredPrice =
            price;

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
}