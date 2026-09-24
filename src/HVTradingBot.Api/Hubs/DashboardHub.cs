using Microsoft.AspNetCore.SignalR;

namespace HVTradingBot.Api.Hubs;

/// <summary>Server-to-client push only. Clients cannot invoke trading actions through the hub.</summary>
public sealed class DashboardHub : Hub
{
    public const string Path = "/hubs/dashboard";
    public const string StatusMessage = "status";
}
