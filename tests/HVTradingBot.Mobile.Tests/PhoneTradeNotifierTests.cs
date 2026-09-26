using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Notifications;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Strategies;
using HVTradingBot.Mobile.Core;

namespace HVTradingBot.Mobile.Tests;

public class PhoneTradeNotifierTests
{
    private static TradeDecisionNotification Notification(DecisionState state) =>
        new(Guid.NewGuid(), "EUR/USD", "Trend Following", state, TradingMode.Paper, DateTimeOffset.UtcNow, ["Score 88"]);

    [Fact]
    public void Opened_and_closed_trades_and_kill_switch_changes_reach_the_phone()
    {
        var opened = PhoneTradeNotifier.Format(Notification(DecisionState.Executed) with
        {
            Setup = new TradeSetup(Direction.Long, 1.1m, 1.098m, 1.104m),
            Score = 88
        })!;
        Assert.Equal("Opened long EUR/USD", opened.Title);
        Assert.Contains("score 88", opened.Text);

        var closed = PhoneTradeNotifier.Format(Notification(DecisionState.Executed) with
        {
            Kind = NotificationKind.TradeClosed,
            RealizedPnl = -12.5m,
            Currency = "USD",
            RMultiple = -1m,
            ExitReason = "StopLoss"
        })!;
        Assert.Equal("EUR/USD closed -12.50 USD", closed.Title);
        Assert.Contains("-1.00R", closed.Text);

        var kill = PhoneTradeNotifier.Format(Notification(DecisionState.NoTrade) with { Kind = NotificationKind.KillSwitch, KillSwitchActive = true })!;
        Assert.StartsWith("Kill switch ON", kill.Title);
    }

    [Fact]
    public void Rejections_stay_off_the_phone() =>
        Assert.Null(PhoneTradeNotifier.Format(Notification(DecisionState.RejectedByRisk)));

    [Fact]
    public async Task Email_channel_still_gets_everything_even_if_the_phone_fails()
    {
        var inner = new RecordingNotifier();
        var notifier = new PhoneTradeNotifier(inner, new ThrowingSink());

        await notifier.NotifyAsync(Notification(DecisionState.Executed), CancellationToken.None);

        Assert.Single(inner.Received);
    }

    private sealed class RecordingNotifier : ITradeDecisionNotifier
    {
        public List<TradeDecisionNotification> Received { get; } = [];

        public ValueTask NotifyAsync(TradeDecisionNotification notification, CancellationToken cancellationToken)
        {
            Received.Add(notification);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingSink : IPhoneNotificationSink
    {
        public void Show(PhoneNotification notification) => throw new InvalidOperationException("No notification permission");
    }
}
