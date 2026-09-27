using System.Globalization;

namespace CyberBilling.Server.Billing;

public static class BillingCalculator
{
    public static decimal CalculateUsageAmount(
        TimeSpan elapsed,
        decimal hourlyRate,
        decimal minimumCharge)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        decimal rawAmount =
            (decimal)elapsed.TotalSeconds
            * hourlyRate
            / 3600m;

        decimal calculated =
            decimal.Floor(rawAmount);

        return Math.Max(
            calculated,
            minimumCharge);
    }

    public static decimal RoundPayableAmount(
        decimal amount)
    {
        return Math.Round(
                   amount / 1000m,
                   0,
                   MidpointRounding.AwayFromZero)
               * 1000m;
    }

    public static string FormatMoney(
        decimal amount)
    {
        CultureInfo culture =
            CultureInfo.GetCultureInfo(
                "vi-VN");

        return amount.ToString(
                   "N0",
                   culture)
               + " đ";
    }

    public static string FormatUsedTime(
        TimeSpan elapsed)
    {
        int totalHours =
            (int)elapsed.TotalHours;

        return
            $"{totalHours:00}:"
            + $"{elapsed.Minutes:00}";
    }

    public static string FormatUsedTimeDetailed(
        TimeSpan elapsed)
    {
        int totalHours =
            (int)elapsed.TotalHours;

        return
            $"{totalHours:00}:"
            + $"{elapsed.Minutes:00}:"
            + $"{elapsed.Seconds:00}";
    }

    public static TimeSpan CalculatePrepaidDuration(
        decimal prepaidAmount,
        decimal hourlyRate)
    {
        if (prepaidAmount <= 0
            || hourlyRate <= 0)
        {
            return TimeSpan.Zero;
        }

        decimal seconds =
            prepaidAmount
            * 3600m
            / hourlyRate;

        decimal roundedSeconds =
            Math.Round(
                seconds,
                0,
                MidpointRounding.AwayFromZero);

        return TimeSpan.FromSeconds(
            (double)roundedSeconds);
    }

    public static string FormatCountdownTime(
        TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration =
                TimeSpan.Zero;
        }

        int totalHours =
            (int)duration.TotalHours;

        return
            $"{totalHours:00}:"
            + $"{duration.Minutes:00}:"
            + $"{duration.Seconds:00}";
    }
}