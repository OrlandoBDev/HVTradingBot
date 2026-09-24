using System.Globalization;
using System.Text;
using HVTradingBot.Domain.Common;

namespace HVTradingBot.Application.Notifications;

public static class TradeDecisionEmailFormatter
{
    public static EmailMessage Format(TradeDecisionNotification notification, string subjectPrefix)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var subject = $"{subjectPrefix} {Subject(notification)}".Trim();
        var body = new StringBuilder();
        body.AppendLine(Headline(notification));
        body.AppendLine();

        AppendLine(body, "Event", Label(notification.Kind));
        AppendLine(body, "Trading mode", notification.Mode.ToString().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(notification.Broker)) AppendLine(body, "Broker", notification.Broker);
        if (notification.Kind != NotificationKind.KillSwitch)
        {
            AppendLine(body, "Instrument", notification.Instrument);
            AppendLine(body, "Strategy", notification.Strategy ?? "-");
        }

        if (!string.IsNullOrWhiteSpace(notification.Regime)) AppendLine(body, "Regime", notification.Regime);
        AppendLine(body, "Time (UTC)", notification.DecidedAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        if (notification.Kind == NotificationKind.TradeClosed)
        {
            body.AppendLine();
            if (notification.Direction is { } d) AppendLine(body, "Direction", Side(d));
            if (notification.EntryPrice is { } entry) AppendLine(body, "Entry", FormatDecimal(entry));
            if (notification.ExitPrice is { } exit) AppendLine(body, "Exit", FormatDecimal(exit));
            if (!string.IsNullOrWhiteSpace(notification.ExitReason)) AppendLine(body, "Closed by", notification.ExitReason);
            if (notification.RealizedPnl is { } pnl) AppendLine(body, "Profit / loss", Money(pnl, notification.Currency));
            if (notification.RMultiple is { } r) AppendLine(body, "Result in R", r.ToString("+0.00;-0.00", CultureInfo.InvariantCulture));
        }
        else if (notification.Setup is { } setup)
        {
            body.AppendLine();
            AppendLine(body, "Direction", Side(setup.Direction));
            AppendLine(body, "Entry", FormatDecimal(setup.Entry));
            AppendLine(body, "Stop loss", FormatDecimal(setup.StopLoss));
            AppendLine(body, "Take profit", FormatDecimal(setup.TakeProfit));
            AppendLine(body, "Reward:risk", setup.RewardToRisk.ToString("0.00", CultureInfo.InvariantCulture));
        }

        if (notification.Score is { } score) AppendLine(body, "Score", score.ToString(CultureInfo.InvariantCulture));
        if (notification.Quantity is { } quantity) AppendLine(body, "Quantity", FormatDecimal(quantity));
        if (!string.IsNullOrWhiteSpace(notification.BrokerOrderId)) AppendLine(body, "Broker order id", notification.BrokerOrderId);

        if (notification.Reasons.Count > 0)
        {
            body.AppendLine();
            body.AppendLine(notification.Kind == NotificationKind.KillSwitch ? "Reason:" : "Reasons:");
            foreach (var reason in notification.Reasons)
            {
                body.Append("- ").AppendLine(reason);
            }
        }

        body.AppendLine();
        body.AppendLine("Sent by HVTradingBot. Notifications are informational only and cannot approve or execute trades.");
        return new EmailMessage(subject, body.ToString());
    }

    private static string Subject(TradeDecisionNotification n)
    {
        var strategy = string.IsNullOrWhiteSpace(n.Strategy) ? string.Empty : $" ({n.Strategy})";
        var side = n.Setup is { } s ? $"{Side(s.Direction)} " : n.Direction is { } d ? $"{Side(d)} " : string.Empty;
        return n.Kind switch
        {
            NotificationKind.TradeOpened => $"Trade opened: {side}{n.Instrument}{(n.Setup is { } setup ? $" @ {FormatDecimal(setup.Entry)}" : "")}{strategy}",
            NotificationKind.TradeClosed => $"Trade closed: {side}{n.Instrument} {n.ExitReason} {(n.RealizedPnl is { } p ? Money(p, n.Currency) : "")}"
                                            + (n.RMultiple is { } r ? $" ({r.ToString("+0.0;-0.0", CultureInfo.InvariantCulture)}R)" : ""),
            NotificationKind.KillSwitch => n.KillSwitchActive == false ? "Kill switch deactivated" : "Kill switch ACTIVATED",
            NotificationKind.Test => "Test email",
            _ => $"{n.Status}: {side}{n.Instrument}{strategy}"
        };
    }

    private static string Headline(TradeDecisionNotification n) => n.Kind switch
    {
        NotificationKind.TradeOpened => "A trade was opened.",
        NotificationKind.TradeClosed => "A trade was closed.",
        NotificationKind.KillSwitch => n.KillSwitchActive == false
            ? "The kill switch was deactivated; new trades are allowed again."
            : "The kill switch was activated; no new trades will be placed until it is turned off.",
        NotificationKind.Test => "This is a test email from HVTradingBot. Your email settings work.",
        _ => "An order was not placed."
    };

    private static string Label(NotificationKind kind) => kind switch
    {
        NotificationKind.TradeOpened => "Trade opened",
        NotificationKind.TradeClosed => "Trade closed",
        NotificationKind.OrderRejected => "Order rejected",
        NotificationKind.ApprovalRequired => "Approval required",
        NotificationKind.KillSwitch => "Kill switch",
        _ => "Test"
    };

    private static string Side(Direction direction) => direction == Direction.Long ? "BUY" : "SELL";

    private static string Money(decimal value, string? currency) =>
        $"{(value >= 0 ? "+" : "-")}{Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture)} {currency ?? "USD"}";

    private static void AppendLine(StringBuilder body, string label, string value) =>
        body.Append(label).Append(": ").AppendLine(value);

    private static string FormatDecimal(decimal value) =>
        value.ToString("0.#############################", CultureInfo.InvariantCulture);
}
