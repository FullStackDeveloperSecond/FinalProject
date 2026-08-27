using System.Globalization;

namespace DoSelect.Domain.Orders;

public static class OrderNumber
{
    public const int MaximumDailySequence = 9_999;

    public static string Create(DateOnly businessDate, int sequence)
    {
        if (sequence is < 1 or > MaximumDailySequence)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"DS{businessDate:yyyyMMdd}{sequence:0000}");
    }

    public static string DailyPrefix(DateOnly businessDate) =>
        string.Create(CultureInfo.InvariantCulture, $"DS{businessDate:yyyyMMdd}");
}
