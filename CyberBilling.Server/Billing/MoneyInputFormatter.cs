using System.Globalization;
using System.Windows.Controls;

namespace CyberBilling.Server.Billing;

public static class MoneyInputFormatter
{
    private static readonly CultureInfo
        VietnameseCulture =
            CultureInfo.GetCultureInfo(
                "vi-VN");

    public static void Format(
        TextBox textBox)
    {
        string digits =
            new(
                textBox.Text
                    .Where(char.IsDigit)
                    .ToArray());

        if (string.IsNullOrEmpty(digits))
        {
            return;
        }

        if (!decimal.TryParse(
                digits,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out decimal value))
        {
            return;
        }

        string formatted =
            value.ToString(
                "N0",
                VietnameseCulture);

        if (textBox.Text == formatted)
        {
            return;
        }

        textBox.Text =
            formatted;

        textBox.CaretIndex =
            textBox.Text.Length;
    }

    public static bool TryParse(
        string text,
        out decimal value)
    {
        string digits =
            new(
                text
                    .Where(char.IsDigit)
                    .ToArray());

        return decimal.TryParse(
            digits,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }
}