using System.Globalization;
using System.Text;

namespace HVTradingBot.Application.Notifications;

public static class TradeDecisionEmailFormatter
{
    public static EmailMessage Format(TradeDecisionNotification notification, string subjectPrefix)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var proposal = notification.Proposal;
        var direction = proposal is null ? string.Empty : $"{proposal.Direction.ToString().ToUpperInvariant()} ";
        var subject = $"{subjectPrefix} {notification.Status}: {direction}{notification.Instrument} ({notification.Strategy})".Trim();

        var body = new StringBuilder();
        body.AppendLine("A trade decision was made.");
        body.AppendLine();
        AppendLine(body, "Decision", notification.Status.ToString());
        AppendLine(body, "Trading mode", notification.Mode.ToString().ToUpperInvariant());
        AppendLine(body, "Instrument", notification.Instrument);
        AppendLine(body, "Strategy", notification.Strategy);
        AppendLine(body, "Decided at (UTC)", notification.DecidedAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        AppendLine(body, "Decision id", notification.DecisionId.ToString());

        if (proposal is not null)
        {
            body.AppendLine();
            AppendLine(body, "Direction", proposal.Direction.ToString().ToUpperInvariant());
            AppendLine(body, "Entry", FormatDecimal(proposal.SuggestedEntry));
            AppendLine(body, "Stop loss", FormatDecimal(proposal.StopLoss));
            AppendLine(body, "Take profit", FormatDecimal(proposal.TakeProfit));
            AppendLine(body, "Score", FormatDecimal(proposal.Score));
            AppendLine(body, "Expires at (UTC)", proposal.ExpiresAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        }

        if (notification.Quantity is { } quantity)
        {
            AppendLine(body, "Quantity", FormatDecimal(quantity));
        }

        if (!string.IsNullOrWhiteSpace(notification.BrokerOrderId))
        {
            AppendLine(body, "Broker order id", notification.BrokerOrderId);
        }

        if (notification.Reasons.Count > 0)
        {
            body.AppendLine();
            body.AppendLine("Reasons:");
            foreach (var reason in notification.Reasons)
            {
                body.Append("- ").AppendLine(reason);
            }
        }

        return new EmailMessage(subject, body.ToString());
    }

    private static void AppendLine(StringBuilder body, string label, string value) =>
        body.Append(label).Append(": ").AppendLine(value);

    private static string FormatDecimal(decimal value) =>
        value.ToString("0.#############################", CultureInfo.InvariantCulture);
}
