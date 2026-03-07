using BlowingCandles.Infrastructure.Clock;
using Xunit;

namespace BlowingCandles.Infrastructure.Tests;

public sealed class PlaceholderInfrastructureTests
{
    [Fact]
    public void FixedClock_ReturnsConfiguredUtcTimestamp()
    {
        var timestamp = new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(timestamp);

        Assert.Equal(timestamp, clock.UtcNow);
    }
}
