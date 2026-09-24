namespace HVTradingBot.Domain.Common;

public static class DecimalMath
{
    /// <summary>Square root for decimals. Precision is limited to double, which is sufficient for indicator values.</summary>
    public static decimal Sqrt(decimal value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Square root of a negative number.");
        }

        return (decimal)Math.Sqrt((double)value);
    }
}
