using Microsoft.AspNetCore.SignalR;

namespace HVTradingBot.Api.Hubs;

/// <summary>Server-to-client push only. Clients cannot invoke trading actions through the hub.</summary>
public sealed class DashboardHub : Hub
{
    public const string Path = "/hubs/dashboard";
    public const string StatusMessage = "status";

    /// <summary>A newly closed 5-minute candle (<c>LiveBarDto</c>), one message per bar in time order.</summary>
    public const string BarMessage = "bar";

    /// <summary>Latest quotes that changed since the last push (<c>LiveTickDto[]</c>), sent only when there are some.</summary>
    public const string TicksMessage = "ticks";
}
