using HVTradingBot.Application.Notifications;

namespace HVTradingBot.Application.Abstractions;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
