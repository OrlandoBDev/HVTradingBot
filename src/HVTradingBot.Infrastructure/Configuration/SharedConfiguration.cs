using Microsoft.Extensions.Configuration;

namespace HVTradingBot.Infrastructure.Configuration;

public static class SharedConfiguration
{
    public const string FileName = "hvtradingbot.shared.json";

    /// <summary>
    /// Adds the trading/risk configuration shared by the API and worker. Loaded from the application's base
    /// directory (where the build copies it) so it works under `dotnet run`, published output and Docker alike.
    /// In Development the repository's .env is read too, so an IDE run finds the database like ./run.sh does.
    /// Environment variables are re-added afterwards so they still take precedence.
    /// </summary>
    public static IConfigurationBuilder AddSharedTradingConfiguration(this IConfigurationBuilder builder, string environmentName)
    {
        builder
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, FileName), optional: false)
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, $"hvtradingbot.shared.{environmentName}.json"), optional: true);

        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            && DotEnvFile.Find(Directory.GetCurrentDirectory(), AppContext.BaseDirectory) is { } envFile)
        {
            builder.AddInMemoryCollection(DotEnvFile.ToSettings(DotEnvFile.Parse(File.ReadAllLines(envFile))));
        }

        return builder.AddEnvironmentVariables();
    }
}
