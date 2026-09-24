using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MimeKit;

namespace HVTradingBot.Infrastructure.Notifications;

public static class NotificationServiceCollectionExtensions
{
    public static IServiceCollection AddTradeDecisionNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<EmailNotificationOptions>()
            .Bind(configuration.GetSection(EmailNotificationOptions.SectionName))
            .Validate(x => !x.Enabled || !string.IsNullOrWhiteSpace(x.SmtpHost),
                "Notifications:Email:SmtpHost is required when email notifications are enabled.")
            .Validate(x => !x.Enabled || x.SmtpPort is > 0 and <= 65535,
                "Notifications:Email:SmtpPort must be a valid port.")
            .Validate(x => !x.Enabled || (!string.IsNullOrWhiteSpace(x.Username) && !string.IsNullOrWhiteSpace(x.Password)),
                "Notifications:Email:Username and Password (Gmail App Password) are required when email notifications are enabled.")
            .Validate(x => !x.Enabled || MailboxAddress.TryParse(x.EffectiveFromAddress, out _),
                "Notifications:Email:FromAddress (or Username) must be a valid email address.")
            .Validate(x => !x.Enabled || (x.ToAddresses.Length > 0 && x.ToAddresses.All(a => MailboxAddress.TryParse(a, out _))),
                "Notifications:Email:ToAddresses must contain at least one valid email address.")
            .ValidateOnStart();

        var enabled = configuration.GetValue<bool>($"{EmailNotificationOptions.SectionName}:Enabled");
        if (!enabled)
        {
            services.TryAddSingleton<ITradeDecisionNotifier, NullTradeDecisionNotifier>();
            return services;
        }

        services.TryAddSingleton<TradeDecisionNotificationQueue>();
        services.TryAddSingleton<IEmailSender, SmtpEmailSender>();
        services.TryAddSingleton<ITradeDecisionNotifier, QueuedTradeDecisionNotifier>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, EmailNotificationDispatcher>());

        return services;
    }
}
