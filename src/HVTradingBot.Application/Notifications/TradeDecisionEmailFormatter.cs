using System.Globalization;
using System.Text;
using HVTradingBot.Domain.Common;

namespace HVTradingBot.Application.Notifications;

public static class TradeDecisionEmailFormatter
{
    public static EmailMessage Format(TradeDecisionNotification notification, string subjectPrefix)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var setup = notification.Setup;
        var direction = setup is null ? string.Empty : $"{Side(setup.Direction)} ";
        var strategy = string.IsNullOrWhiteSpace(notification.Strategy) ? string.Empty : $" ({notification.Strategy})";
        var subject = $"{subjectPrefix} {notification.Status}: {direction}{notification.Instrument}{strategy}".Trim();

        var body = new StringBuilder();
        body.AppendLine("A trade decision was made.");
        body.AppendLine();
        AppendLine(body, "Decision", notification.Status.ToString());
        AppendLine(body, "Trading mode", notification.Mode.ToString().ToUpperInvariant());
        AppendLine(body, "Instrument", notification.Instrument);
        if (!string.IsNullOrWhiteSpace(notification.Broker))
        {
            AppendLine(body, "Broker", notification.Broker);
        }

        AppendLine(body, "Strategy", notification.Strategy ?? "-");
        if (!string.IsNullOrWhiteSpace(notification.Regime))
        {
            AppendLine(body, "Regime", notification.Regime);
        }

        AppendLine(body, "Decided at (UTC)", notification.DecidedAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        AppendLine(body, "Decision id", notification.DecisionId.ToString());

        if (setup is not null)
        {
            body.AppendLine();
            AppendLine(body, "Direction", Side(setup.Direction));
            AppendLine(body, "Entry", FormatDecimal(setup.Entry));
            AppendLine(body, "Stop loss", FormatDecimal(setup.StopLoss));
            AppendLine(body, "Take profit", FormatDecimal(setup.TakeProfit));
            AppendLine(body, "Reward:risk", setup.RewardToRisk.ToString("0.00", CultureInfo.InvariantCulture));
        }

        if (notification.Score is { } score)
        {
            AppendLine(body, "Score", score.ToString(CultureInfo.InvariantCulture));
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

    private static string Side(Direction direction) => direction == Direction.Long ? "BUY" : "SELL";

    private static void AppendLine(StringBuilder body, string label, string value) =>
        body.Append(label).Append(": ").AppendLine(value);

    private static string FormatDecimal(decimal value) =>
        value.ToString("0.#############################", CultureInfo.InvariantCulture);
}
