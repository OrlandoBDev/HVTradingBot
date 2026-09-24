using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.UnitTests;

public sealed class CandleTests
{
    [Fact]
    public void Spread_IsAskMinusBid()
    {
        var candle = new Candle("EUR_USD", TimeFrame.FiveMinutes, DateTimeOffset.UtcNow,
            1.10m, 1.11m, 1.09m, 1.105m, null, 1.1048m, 1.1050m);

        Assert.Equal(0.0002m, candle.Spread);
    }
}
