using Ospreya.Application.Abstractions;

namespace Ospreya.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
