using Ospreya.App.Core.Configuration;
using Ospreya.App.Core.Docker;
using Ospreya.App.Core.Health;
using Ospreya.App.Core.IO;
using Ospreya.App.Core.Processes;
using Ospreya.App.Core.Startup;
using Ospreya.App.Pages;
using Ospreya.App.Services;
using Ospreya.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Storage;
using IFileSystem = Ospreya.App.Core.IO.IFileSystem; // not Microsoft.Maui.Storage.IFileSystem

namespace Ospreya.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios.WillTerminate(_ =>
            IPlatformApplication.Current?.Services.GetService<QuitHandler>()?.OnQuit())));

#if DEBUG
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#endif

        var services = builder.Services;

        // App.Core: all the logic, shared with the unit tests.
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IPortProbe, TcpPortProbe>();
        services.AddSingleton<DockerLocator>();
        services.AddSingleton<RepositoryEnvironment>();
        services.AddSingleton<ConflictDetector>();
        services.AddSingleton(_ => new HttpClient());
        services.AddSingleton<HealthClient>();
        services.AddSingleton<HealthMonitor>();
        services.AddSingleton<StartupPipeline>();

        // App shell.
        services.AddSingleton(Preferences.Default);
        services.AddSingleton<AppSettings>();
        services.AddSingleton<AppSession>();
        services.AddSingleton<QuitHandler>();
        services.AddSingleton<Navigator>();

        services.AddSingleton<StartupViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddTransient<LogsViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddTransient<StartupPage>();
        services.AddTransient<DashboardPage>();
        services.AddTransient<LogsPage>();
        services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
