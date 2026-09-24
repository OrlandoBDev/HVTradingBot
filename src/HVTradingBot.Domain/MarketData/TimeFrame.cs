namespace HVTradingBot.Domain.MarketData;

public enum TimeFrame
{
    M5,
    M15,
    H1,
    H4,
    D1
}

public static class TimeFrameExtensions
{
    public static TimeSpan Duration(this TimeFrame timeFrame) => timeFrame switch
    {
        TimeFrame.M5 => TimeSpan.FromMinutes(5),
        TimeFrame.M15 => TimeSpan.FromMinutes(15),
        TimeFrame.H1 => TimeSpan.FromHours(1),
        TimeFrame.H4 => TimeSpan.FromHours(4),
        TimeFrame.D1 => TimeSpan.FromDays(1),
        _ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, null)
    };

    /// <summary>Start of the bar (UTC) that contains <paramref name="timestampUtc"/>.</summary>
    public static DateTime BarStart(this TimeFrame timeFrame, DateTime timestampUtc)
    {
        var ticks = timeFrame.Duration().Ticks;
        return new DateTime(timestampUtc.Ticks - (timestampUtc.Ticks % ticks), DateTimeKind.Utc);
    }
}
