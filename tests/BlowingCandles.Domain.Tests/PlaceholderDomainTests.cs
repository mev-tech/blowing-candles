using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Services;
using Xunit;

namespace BlowingCandles.Domain.Tests;

public sealed class PlaceholderDomainTests
{
    [Fact]
    public void EarningsGate_ReturnsWaitSignalsForEachTicker()
    {
        var clock = new StubClock(new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero));
        var gate = new EarningsGate(new StubCalendar(), new StubMarketDataProvider());

        var signals = gate.Check(["AAPL", "MSFT"], clock);

        Assert.Collection(
            signals,
            signal => Assert.Equal("AAPL", signal.Ticker),
            signal => Assert.Equal("MSFT", signal.Ticker));
    }

    private sealed class StubClock : IClock
    {
        public StubClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class StubCalendar : IEarningsCalendar
    {
        public IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> Load()
        {
            return new Dictionary<string, IReadOnlyList<DateTimeOffset>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class StubMarketDataProvider : IMarketDataProvider
    {
        public IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate)
        {
            _ = ticker;
            _ = asOfDate;
            return Array.Empty<PriceBar>();
        }

        public DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc)
        {
            _ = ticker;
            _ = asOfUtc;
            return null;
        }
    }
}
