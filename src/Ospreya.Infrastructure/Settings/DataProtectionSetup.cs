using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ospreya.Infrastructure.Settings;

public static class DataProtectionSetup
{
    /// <summary>
    /// Shared key ring for the API (encrypts the Deriv token) and the worker (decrypts it). Keys are kept on disk,
    /// outside PostgreSQL, so a database backup alone does not reveal the token.
    /// Default: ~/Library/Application Support/Ospreya/keys on macOS; in Docker the same host folder is bind-mounted
    /// at /keys, so native and Docker runs share one key ring.
    /// </summary>
    public static IServiceCollection AddOspreyaDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var keysPath = configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath))
        {
            keysPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
                "Ospreya", "keys");
        }

        Directory.CreateDirectory(keysPath);
        services.AddDataProtection()
            .SetApplicationName("Ospreya")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        return services;
    }
}
