using BlowingCandles.Domain.Interfaces;

namespace BlowingCandles.Infrastructure.Clock;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
