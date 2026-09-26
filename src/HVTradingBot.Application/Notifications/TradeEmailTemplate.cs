using System.Globalization;
using System.Net;
using System.Text;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Application.Notifications;

/// <summary>
/// HTML version of the notification emails. Table layout and inline styles only (no external CSS, images or scripts),
/// which Gmail, Outlook and Apple Mail render consistently. Every value is HTML-encoded.
/// </summary>
public static class TradeEmailTemplate
{
    private const string Font = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";
    private const string Mono = "'SF Mono',Menlo,Consolas,monospace";
    private const string Ink = "#0f172a";
    private const string Muted = "#64748b";
    private const string Line = "#e2e8f0";
    private const string Green = "#16a34a";
    private const string Red = "#dc2626";
    private const string Amber = "#d97706";
    private const string Blue = "#2563eb";

    public static string Render(TradeDecisionNotification n)
    {
        var accent = Accent(n);
        var decimals = Decimals(n);
        var title = Title(n, decimals);
        var headline = Subtitle(n);
        var html = new StringBuilder();
        html.Append($$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="light">
<meta name="supported-color-schemes" content="light">
<title>{{E(title)}}</title>
</head>
<body style="margin:0;padding:0;background:#f1f5f9;">
<div style="display:none;max-height:0;overflow:hidden;opacity:0;">{{E(Preheader(n))}}</div>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f1f5f9;">
<tr><td align="center" style="padding:24px 12px;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;background:#ffffff;border-radius:14px;overflow:hidden;border:1px solid {{Line}};font-family:{{Font}};color:{{Ink}};">
""");

        // Header
        html.Append($$"""
<tr><td style="background:{{Ink}};padding:16px 24px;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>
<td style="font-family:{{Font}};">
<span style="display:inline-block;width:30px;height:30px;line-height:30px;text-align:center;border-radius:8px;background:#5b9dff;color:#ffffff;font-weight:700;font-size:12px;vertical-align:middle;">HV</span>
<span style="color:#ffffff;font-size:16px;font-weight:600;vertical-align:middle;padding-left:10px;">HVTradingBot</span>
</td>
<td align="right" style="font-family:{{Font}};">{{ModeBadge(n)}}</td>
</tr></table>
</td></tr>
<tr><td style="height:4px;background:{{accent}};font-size:0;line-height:0;">&nbsp;</td></tr>
""");

        // Title block
        html.Append($$"""
<tr><td style="padding:26px 28px 8px;">
<div style="font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:{{accent}};font-weight:700;">{{E(EventLabel(n))}}</div>
<div style="font-size:24px;line-height:1.25;font-weight:700;margin-top:6px;">{{E(title)}}</div>
<div style="font-size:14px;line-height:1.5;color:{{Muted}};margin-top:6px;">{{E(headline)}}</div>
</td></tr>
""");

        // Key figures
        var figures = Figures(n, decimals);
        if (figures.Count > 0)
        {
            html.Append("<tr><td style=\"padding:16px 22px 4px;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>");
            var width = 100 / figures.Count;
            foreach (var (label, value, color) in figures)
            {
                html.Append($$"""
<td width="{{width}}%" style="padding:0 6px;" valign="top">
<div style="background:#f8fafc;border:1px solid {{Line}};border-radius:10px;padding:12px 14px;">
<div style="font-size:11px;letter-spacing:.06em;text-transform:uppercase;color:{{Muted}};">{{E(label)}}</div>
<div style="font-size:18px;font-weight:700;margin-top:4px;color:{{color ?? Ink}};font-family:{{(IsNumeric(value) ? Mono : Font)}};">{{E(value)}}</div>
</div>
</td>
""");
            }

            html.Append("</tr></table></td></tr>");
        }

        // Details
        var rows = Details(n, decimals);
        if (rows.Count > 0)
        {
            html.Append($"<tr><td style=\"padding:18px 28px 4px;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-top:1px solid {Line};\">");
            foreach (var (label, value) in rows)
            {
                html.Append($$"""
<tr>
<td style="padding:9px 0;border-bottom:1px solid {{Line}};font-size:13px;color:{{Muted}};width:42%;">{{E(label)}}</td>
<td style="padding:9px 0;border-bottom:1px solid {{Line}};font-size:13px;text-align:right;font-weight:600;">{{E(value)}}</td>
</tr>
""");
            }

            html.Append("</table></td></tr>");
        }

        // Reasons
        if (n.Reasons.Count > 0)
        {
            html.Append($$"""
<tr><td style="padding:16px 28px 4px;">
<div style="font-size:12px;letter-spacing:.06em;text-transform:uppercase;color:{{Muted}};font-weight:700;margin-bottom:6px;">{{(n.Kind == NotificationKind.KillSwitch ? "Reason" : "Why")}}</div>
""");
            foreach (var reason in n.Reasons)
            {
                html.Append($"<div style=\"font-size:13px;line-height:1.5;padding:6px 10px;margin-bottom:6px;background:#f8fafc;border-left:3px solid {accent};border-radius:4px;\">{E(reason)}</div>");
            }

            html.Append("</td></tr>");
        }

        // Footer
        html.Append($$"""
<tr><td style="padding:22px 28px 26px;">
<div style="font-size:12px;line-height:1.55;color:{{Muted}};border-top:1px solid {{Line}};padding-top:14px;">
Informational only — notifications cannot approve, change or execute trades.
{{(n.Broker?.Contains("demo", StringComparison.OrdinalIgnoreCase) == true ? "Trades are placed on a <b>demo</b> account with virtual funds. " : "")}}Results are not evidence of a future edge and are not financial advice.<br>
Choose which emails you receive under <b>Settings › Notifications</b> in the HVTradingBot dashboard.
</div>
</td></tr>
</table>
<div style="font-family:{{Font}};font-size:11px;color:#94a3b8;padding-top:12px;">HVTradingBot · {{E(LocalTime(n.DecidedAtUtc, "yyyy-MM-dd HH:mm"))}}</div>
</td></tr>
</table>
</body>
</html>
""");
        return html.ToString();
    }

    private static string Title(TradeDecisionNotification n, int decimals) => n.Kind switch
    {
        NotificationKind.TradeOpened when n.Setup is { } s => $"{Side(s.Direction)} {Market(n.Instrument)} @ {Price(s.Entry, decimals)}",
        NotificationKind.TradeClosed => $"{Market(n.Instrument)} · {Humanize(n.ExitReason ?? "Closed")}",
        NotificationKind.KillSwitch => n.KillSwitchActive == false ? "Trading resumed" : "Trading stopped",
        NotificationKind.Test => "Your email settings work",
        NotificationKind.ApprovalRequired => $"{Market(n.Instrument)} needs approval",
        _ => $"{(n.Setup is { } setup ? Side(setup.Direction) + " " : "")}{Market(n.Instrument)} was not placed"
    };

    private static string Subtitle(TradeDecisionNotification n)
    {
        var parts = new List<string>();
        switch (n.Kind)
        {
            case NotificationKind.TradeClosed:
                var side = n.Direction == Direction.Short ? "short (sell)" : "long (buy)";
                return $"Your {side} position closed{(n.ExitReason is { } r ? $" at its {Humanize(r).ToLowerInvariant()}" : "")}"
                       + (n.Strategy is { } st ? $" · {Humanize(st)}" : "") + ".";
            case NotificationKind.KillSwitch:
                return n.KillSwitchActive == false
                    ? "The kill switch was turned off; the engine may place new trades again."
                    : "The kill switch is on. No new trades will be placed until it is turned off; open positions keep their stop loss and take profit.";
            case NotificationKind.Test:
                return "HVTradingBot can send you email. Trade notifications will look like this.";
        }

        if (!string.IsNullOrWhiteSpace(n.Strategy)) parts.Add(Humanize(n.Strategy));
        if (!string.IsNullOrWhiteSpace(n.Regime)) parts.Add(Humanize(n.Regime));
        if (n.Score is { } score) parts.Add($"score {score}/100");
        return parts.Count == 0 ? "" : string.Join(" · ", parts);
    }

    /// <summary>Consistent decimals for all prices in one email (e.g. 1.13520, not 1.1352 next to 1.13761).</summary>
    private static int Decimals(TradeDecisionNotification n)
    {
        IEnumerable<decimal> prices = n.Setup is { } s ? [s.Entry, s.StopLoss, s.TakeProfit] : [n.EntryPrice ?? 0, n.ExitPrice ?? 0];
        return Math.Clamp(prices.Select(p => (int)((decimal.GetBits(p)[3] >> 16) & 0xFF)).DefaultIfEmpty(2).Max(), 0, 8);
    }

    private static string Price(decimal value, int decimals) => value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    private static string Accent(TradeDecisionNotification n) => n.Kind switch
    {
        NotificationKind.TradeOpened => Green,
        NotificationKind.TradeClosed => n.RealizedPnl is < 0 ? Red : Green,
        NotificationKind.KillSwitch => n.KillSwitchActive == false ? Green : Red,
        NotificationKind.Test => Blue,
        _ => Amber
    };

    private static string EventLabel(TradeDecisionNotification n) => n.Kind switch
    {
        NotificationKind.TradeOpened => "Trade opened",
        NotificationKind.TradeClosed => n.RealizedPnl is < 0 ? "Trade closed · loss" : "Trade closed · profit",
        NotificationKind.KillSwitch => "Kill switch",
        NotificationKind.ApprovalRequired => "Approval required",
        NotificationKind.Test => "Test",
        _ => "Order not placed"
    };

    private static string Preheader(TradeDecisionNotification n) => n.Kind switch
    {
        NotificationKind.TradeClosed when n.RealizedPnl is { } p => $"{Market(n.Instrument)}: {Money(p, n.Currency)} ({n.ExitReason})",
        NotificationKind.TradeOpened when n.Setup is { } s => $"{Side(s.Direction)} {Market(n.Instrument)} — stop {Num(s.StopLoss)}, target {Num(s.TakeProfit)}",
        NotificationKind.KillSwitch => n.KillSwitchActive == false ? "New trades are allowed again." : "No new trades will be placed.",
        NotificationKind.Test => "Your email settings work.",
        _ => $"{Market(n.Instrument)}: {string.Join(" ", n.Reasons.Take(1))}"
    };

    private static List<(string Label, string Value, string? Color)> Figures(TradeDecisionNotification n, int decimals)
    {
        var list = new List<(string, string, string?)>();
        if (n.Kind == NotificationKind.TradeClosed)
        {
            if (n.RealizedPnl is { } p) list.Add(("Profit / loss", Money(p, n.Currency), p < 0 ? Red : Green));
            if (n.RMultiple is { } r) list.Add(("Result", $"{r.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)}R", r < 0 ? Red : Green));
            if (n.ExitReason is { } reason) list.Add(("Closed by", Humanize(reason), null));
        }
        else if (n.Setup is { } s && n.Kind != NotificationKind.KillSwitch)
        {
            list.Add(("Entry", Price(s.Entry, decimals), null));
            list.Add(("Stop loss", Price(s.StopLoss, decimals), Red));
            list.Add(("Take profit", Price(s.TakeProfit, decimals), Green));
        }
        else if (n.Kind == NotificationKind.KillSwitch)
        {
            list.Add(("New trades", n.KillSwitchActive == false ? "Allowed" : "Blocked", n.KillSwitchActive == false ? Green : Red));
        }

        return list;
    }

    private static List<(string Label, string Value)> Details(TradeDecisionNotification n, int decimals)
    {
        var rows = new List<(string, string)>();
        if (n.Kind is not (NotificationKind.KillSwitch or NotificationKind.Test))
        {
            rows.Add(("Market", MarketWithSymbol(n.Instrument)));
            var direction = n.Setup?.Direction ?? n.Direction;
            if (direction is { } d) rows.Add(("Direction", Side(d)));
            if (n.Kind == NotificationKind.TradeClosed)
            {
                if (n.EntryPrice is { } entry) rows.Add(("Entry price", Price(entry, decimals)));
                if (n.ExitPrice is { } exit) rows.Add(("Exit price", Price(exit, decimals)));
            }

            if (!string.IsNullOrWhiteSpace(n.Strategy)) rows.Add(("Strategy", Humanize(n.Strategy)));
            if (!string.IsNullOrWhiteSpace(n.Regime)) rows.Add(("Market regime", Humanize(n.Regime)));
            if (n.Setup is { } s) rows.Add(("Reward : risk", $"{s.RewardToRisk.ToString("0.00", CultureInfo.InvariantCulture)} : 1"));
            if (n.Score is { } score) rows.Add(("Score", $"{score} / 100"));
            if (n.Quantity is { } q) rows.Add(("Position size", $"{q.ToString(q < 100 ? "#,0.####" : "#,0", CultureInfo.InvariantCulture)} units"));
        }

        if (!string.IsNullOrWhiteSpace(n.Broker)) rows.Add(("Account", n.Broker));
        rows.Add(("Time", LocalTime(n.DecidedAtUtc, "ddd d MMM yyyy, HH:mm:ss")));
        if (!string.IsNullOrWhiteSpace(n.BrokerOrderId)) rows.Add(("Order reference", n.BrokerOrderId));
        return rows;
    }

    private static string ModeBadge(TradeDecisionNotification n)
    {
        var demo = n.Broker?.Contains("demo", StringComparison.OrdinalIgnoreCase) == true || n.Mode == TradingMode.Paper;
        var (text, bg) = demo ? ("DEMO", "#1e3a8a") : ("LIVE", Red);
        return $"<span style=\"display:inline-block;padding:4px 10px;border-radius:999px;background:{bg};color:#ffffff;font-size:11px;font-weight:700;letter-spacing:.06em;\">{text}</span>";
    }

    /// <summary>"TrendFollowing" -> "Trend following", "TrendingBullish" -> "Trending bullish".</summary>
    public static string Humanize(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value)
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ').Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsNumeric(string value) => value.Length > 0 && (char.IsDigit(value[0]) || value[0] is '+' or '-' or '−');

    /// <summary>Readable market name, e.g. "Volatility 75 (1s) Index" for 1HZ75V.</summary>
    /// <summary>
    /// A time in this device's time zone (the phone's in the Android app, the server's otherwise) with its UTC offset,
    /// e.g. "2026-09-26 14:05 (UTC-04:00)", so it is unambiguous wherever the email is read.
    /// </summary>
    public static string LocalTime(DateTimeOffset time, string format, TimeZoneInfo? zone = null)
    {
        var local = TimeZoneInfo.ConvertTime(time, zone ?? TimeZoneInfo.Local);
        var offset = local.Offset == TimeSpan.Zero
            ? "UTC"
            : $"UTC{(local.Offset < TimeSpan.Zero ? "-" : "+")}{local.Offset.Duration():hh\\:mm}";
        return $"{local.ToString(format, CultureInfo.InvariantCulture)} ({offset})";
    }

    public static string Market(string symbol) => Instruments.DisplayNameOf(symbol);

    /// <summary>Market name with its symbol when they differ, e.g. "Volatility 75 (1s) Index (1HZ75V)".</summary>
    public static string MarketWithSymbol(string symbol) => Market(symbol) is var name && name != symbol ? $"{name} ({symbol})" : symbol;

    private static string Side(Direction d) => d == Direction.Long ? "BUY" : "SELL";

    private static string Num(decimal value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    private static string Money(decimal value, string? currency) =>
        $"{(value >= 0 ? "+" : "−")}{Math.Abs(value).ToString("#,0.00", CultureInfo.InvariantCulture)} {currency ?? "USD"}";

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
