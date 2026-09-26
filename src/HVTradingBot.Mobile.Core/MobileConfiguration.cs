using System.Reflection;
using HVTradingBot.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace HVTradingBot.Mobile.Core;

/// <summary>
/// The same trading and risk configuration as the API and worker (hvtradingbot.shared.json), pointed at a SQLite
/// database in the app's private storage.
/// </summary>
public static class MobileConfiguration
{
    public static IConfigurationRoot Build(MobileSettings settings)
    {
        Directory.CreateDirectory(settings.DataDirectory);
        Directory.CreateDirectory(settings.KeysDirectory);

        var values = new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{DatabaseSetup.ConnectionStringName}"] = $"Data Source={settings.DatabasePath}",
            [DatabaseSetup.ProviderKey] = nameof(DatabaseProvider.Sqlite),
            ["DataProtection:KeysPath"] = settings.KeysDirectory,
            ["MarketData:Provider"] = settings.MarketData.ToString(),
            ["Broker:Provider"] = settings.Broker.ToString()
        };
        foreach (var (key, value) in settings.Overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder()
            .AddJsonStream(Resource("hvtradingbot.shared.json"))
            .AddJsonStream(Resource("hvtradingbot.mobile.json"))
            .AddInMemoryCollection(values)
            .Build();
    }

    private static Stream Resource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Embedded configuration {name} is missing.");
}
