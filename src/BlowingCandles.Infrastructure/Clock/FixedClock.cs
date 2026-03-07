using BlowingCandles.Domain.Interfaces;

namespace BlowingCandles.Infrastructure.Clock;

public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow.ToUniversalTime();
    }

    public DateTimeOffset UtcNow { get; }
}
