using HVTradingBot.Application.Abstractions;

namespace HVTradingBot.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
