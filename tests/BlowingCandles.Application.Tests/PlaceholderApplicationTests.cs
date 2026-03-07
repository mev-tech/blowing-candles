using BlowingCandles.Application;
using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Services;
using Xunit;

namespace BlowingCandles.Application.Tests;

public sealed class PlaceholderApplicationTests
{
    [Fact]
    public void Pipeline_ProducesOneFinalSignalPerTicker()
    {
        var clock = new StubClock(new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero));
        var pipeline = new SignalPipeline(
            new EarningsGate(new StubCalendar(), new StubMarketDataProvider()),
            new TechnicalScorer(new StubMarketDataProvider()),
            new TradeGovernor());

        var signals = pipeline.Run(["AAPL", "MSFT"], clock);

        Assert.Equal(2, signals.Count);
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
