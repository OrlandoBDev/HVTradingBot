using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HVTradingBot.Infrastructure.Settings;

public static class DataProtectionSetup
{
    /// <summary>
    /// Shared key ring for the API (encrypts the Deriv token) and the worker (decrypts it). Keys are kept on disk,
    /// outside PostgreSQL, so a database backup alone does not reveal the token.
    /// Default: ~/Library/Application Support/HVTradingBot/keys on macOS; /keys volume in Docker.
    /// </summary>
    public static IServiceCollection AddHvDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var keysPath = configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath))
        {
            keysPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
                "HVTradingBot", "keys");
        }

        Directory.CreateDirectory(keysPath);
        services.AddDataProtection()
            .SetApplicationName("HVTradingBot")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        return services;
    }
}
