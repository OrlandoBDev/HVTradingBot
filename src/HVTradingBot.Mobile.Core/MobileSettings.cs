using HVTradingBot.Infrastructure.Configuration;
using HVTradingBot.Infrastructure.MarketData;

namespace HVTradingBot.Mobile.Core;

/// <summary>Where the app keeps its data and which market data and broker the engine uses.</summary>
public sealed record MobileSettings(string DataDirectory)
{
    /// <summary>Deriv (real prices, default) or Simulated (offline synthetic prices; never mixed in one database).</summary>
    public MarketDataProvider MarketData { get; init; } = MarketDataProvider.Deriv;

    /// <summary>Deriv (demo account, default) or Paper (local simulated fills on the same prices).</summary>
    public BrokerProvider Broker { get; init; } = BrokerProvider.Deriv;

    /// <summary>Extra configuration (e.g. "Risk:MaxOpenPositions"), applied last.</summary>
    public IReadOnlyDictionary<string, string?> Overrides { get; init; } = new Dictionary<string, string?>();

    public string DatabasePath => Path.Combine(DataDirectory, "trading.db");

    /// <summary>Data Protection keys that encrypt the Deriv token and SMTP password in the database.</summary>
    public string KeysDirectory => Path.Combine(DataDirectory, "keys");
}
