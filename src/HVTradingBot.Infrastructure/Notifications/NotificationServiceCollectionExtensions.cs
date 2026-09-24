using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HVTradingBot.Infrastructure.Notifications;

public static class NotificationServiceCollectionExtensions
{
    /// <summary>Email settings storage and the SMTP sender (API: Settings page and test email; worker: delivery).</summary>
    public static IServiceCollection AddEmailSettings(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(d => d.ServiceType == typeof(EmailSettingsStore)))
        {
            return services; // already registered (core and worker both call this)
        }

        services.AddOptions<EmailNotificationOptions>().Bind(configuration.GetSection(EmailNotificationOptions.SectionName));
        services.AddSingleton<EmailSettingsStore>();
        services.AddSingleton<IEmailSettingsProvider>(sp => sp.GetRequiredService<EmailSettingsStore>());
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }

    /// <summary>
    /// Trade notifications for the worker: decisions and closed trades are queued and emailed in the background, so
    /// SMTP latency or failures never affect trading. Whether anything is sent is decided by the current settings.
    /// </summary>
    public static IServiceCollection AddTradeDecisionNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEmailSettings(configuration);
        services.AddSingleton<TradeDecisionNotificationQueue>();
        services.AddSingleton<ITradeDecisionNotifier, QueuedTradeDecisionNotifier>();
        services.AddSingleton<IHostedService>(sp =>
        {
            var store = sp.GetRequiredService<EmailSettingsStore>();
            return new EmailNotificationDispatcher(
                sp.GetRequiredService<TradeDecisionNotificationQueue>(),
                sp.GetRequiredService<IEmailSender>(),
                store,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EmailNotificationDispatcher>>(),
                store.RecordAttemptAsync);
        });
        return services;
    }
}
