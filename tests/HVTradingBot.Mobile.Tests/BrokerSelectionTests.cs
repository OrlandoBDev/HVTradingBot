using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure;
using HVTradingBot.Infrastructure.Brokers.Deriv;
using HVTradingBot.Infrastructure.Brokers.Mt5;
using HVTradingBot.Infrastructure.Settings;
using HVTradingBot.Infrastructure.Configuration;
using HVTradingBot.Infrastructure.MarketData;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Mobile.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Mobile.Tests;

/// <summary>The trading engine uses MT5 for Forex once it is switched on in Settings, Deriv otherwise.</summary>
public sealed class BrokerSelectionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hvtradingbot-broker-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task Mt5_replaces_deriv_once_switched_on()
    {
        var configuration = MobileConfiguration.Build(new MobileSettings(_folder) { MarketData = MarketDataProvider.Deriv, Broker = BrokerProvider.Deriv });

        Assert.IsType<DerivBroker>(await BrokerAsync(configuration, enableMt5: false));
        Assert.IsType<Mt5Broker>(await BrokerAsync(configuration, enableMt5: true));
    }

    private static async Task<IExecutionBroker> BrokerAsync(IConfigurationRoot configuration, bool enableMt5)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.ClearProviders());
        services.AddTradingCore(configuration).AddLiveTrading(configuration);
        await using var provider = services.BuildServiceProvider();
        await provider.MigrateDatabaseAsync(CancellationToken.None);
        await provider.GetRequiredService<Mt5SettingsStore>().SaveAsync(enableMt5, "metaapi-token-0123456789abcdef", "acc-1", "london", "", 0.007m,
            "tester", CancellationToken.None);
        return provider.GetRequiredService<IExecutionBroker>();
    }
}
