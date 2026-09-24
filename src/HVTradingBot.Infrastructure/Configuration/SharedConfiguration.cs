using Microsoft.Extensions.Configuration;

namespace HVTradingBot.Infrastructure.Configuration;

public static class SharedConfiguration
{
    public const string FileName = "hvtradingbot.shared.json";

    /// <summary>
    /// Adds the trading/risk configuration shared by the API and worker. Loaded from the application's base
    /// directory (where the build copies it) so it works under `dotnet run`, published output and Docker alike.
    /// Environment variables are re-added afterwards so they still take precedence.
    /// </summary>
    public static IConfigurationBuilder AddSharedTradingConfiguration(this IConfigurationBuilder builder, string environmentName) =>
        builder
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, FileName), optional: false)
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, $"hvtradingbot.shared.{environmentName}.json"), optional: true)
            .AddEnvironmentVariables();
}
