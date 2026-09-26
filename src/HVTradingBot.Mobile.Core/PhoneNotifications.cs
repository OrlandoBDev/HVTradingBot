using System.Globalization;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Notifications;

namespace HVTradingBot.Mobile.Core;

/// <summary>A notification for the phone's notification shade.</summary>
public sealed record PhoneNotification(string Title, string Text, NotificationKind Kind);

/// <summary>Implemented by the Android app to show notifications.</summary>
public interface IPhoneNotificationSink
{
    void Show(PhoneNotification notification);
}

/// <summary>
/// Sends trade openings, closings and kill-switch changes to the phone as well as to the configured email channel.
/// Like the email notifier it never blocks or fails the trading flow.
/// </summary>
public sealed class PhoneTradeNotifier(ITradeDecisionNotifier inner, IPhoneNotificationSink sink) : ITradeDecisionNotifier
{
    public ValueTask NotifyAsync(TradeDecisionNotification notification, CancellationToken cancellationToken)
    {
        if (Format(notification) is { } phone)
        {
            try
            {
                sink.Show(phone);
            }
            catch
            {
                // A notification problem must never affect trading.
            }
        }

        return inner.NotifyAsync(notification, cancellationToken);
    }

    public static PhoneNotification? Format(TradeDecisionNotification n)
    {
        var inv = CultureInfo.InvariantCulture;
        return n.Kind switch
        {
            NotificationKind.TradeOpened => new PhoneNotification(
                $"Opened {n.Setup?.Direction.ToString().ToLowerInvariant() ?? "trade"} {n.Instrument}",
                string.Join(" · ", new[]
                {
                    n.Strategy,
                    n.Score is { } score ? $"score {score}" : null,
                    n.Setup is { } setup ? string.Create(inv, $"stop {setup.StopLoss} · target {setup.TakeProfit}") : null
                }.Where(s => s is not null)),
                n.Kind),
            NotificationKind.TradeClosed => new PhoneNotification(
                string.Create(inv, $"{n.Instrument} closed {(n.RealizedPnl >= 0 ? "+" : "")}{n.RealizedPnl:0.00} {n.Currency}"),
                string.Join(" · ", new[]
                {
                    n.ExitReason,
                    n.RMultiple is { } r ? string.Create(inv, $"{r:0.00}R") : null,
                    n.Strategy
                }.Where(s => s is not null)),
                n.Kind),
            NotificationKind.KillSwitch => new PhoneNotification(
                n.KillSwitchActive == true ? "Kill switch ON - new trades blocked" : "Kill switch off - trading resumed",
                string.Join(" ", n.Reasons),
                n.Kind),
            _ => null
        };
    }
}
