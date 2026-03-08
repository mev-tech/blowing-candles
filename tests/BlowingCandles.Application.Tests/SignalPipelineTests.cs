using BlowingCandles.Application;
using BlowingCandles.Domain.Enums;
using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;
using BlowingCandles.Domain.Services;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Application.Tests;

public sealed class SignalPipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly AsOfDate = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public void Run_NormalizesAndDeduplicatesWatchlist_BeforeExecutingPipeline()
    {
        var provider = new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)
        {
            ["AAPL"] = CreateBuyHistory(AsOfDate)
        });
        var calendar = new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)
        {
            ["AAPL"] = Now.AddDays(7)
        });
        var pipeline = CreatePipeline(calendar, provider);

        var signals = pipeline.Run([" aapl ", "AAPL", " "], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal("AAPL", signal.Ticker);
        Assert.Equal(TradingAction.BUY, signal.Action);
        Assert.Equal(NewsState.TRADE_OK, signal.NewsState);
        Assert.Equal(TradingAction.BUY, signal.MarketAction);
        Assert.Equal(["AAPL"], provider.PriceRequests);
        Assert.Equal(["AAPL"], calendar.EarningsRequests);
    }

    [Fact]
    public void Run_EarningsBlock_OverridesStrongTechnicalBuy()
    {
        var provider = new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)
        {
            ["AAPL"] = CreateBuyHistory(AsOfDate)
        });
        var calendar = new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)
        {
            ["AAPL"] = Now.AddHours(12)
        });
        var pipeline = CreatePipeline(calendar, provider);

        var signals = pipeline.Run(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(NewsState.NO_TRADE, signal.NewsState);
        Assert.Equal(TradingAction.BUY, signal.MarketAction);
        Assert.Equal("BLOCKED_BY_NEWS", signal.Reason);
    }

    [Fact]
    public void Run_WeakTechnicals_ProduceSellWhenEarningsAreClear()
    {
        var provider = new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)
        {
            ["MSFT"] = CreateSellHistory(AsOfDate)
        });
        var calendar = new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)
        {
            ["MSFT"] = Now.AddDays(10)
        });
        var pipeline = CreatePipeline(calendar, provider);

        var signals = pipeline.Run(["MSFT"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.SELL, signal.Action);
        Assert.Equal(NewsState.TRADE_OK, signal.NewsState);
        Assert.Equal(TradingAction.SELL, signal.MarketAction);
    }

    [Fact]
    public void Run_EmptyWatchlist_ReturnsEmptyList()
    {
        var pipeline = CreatePipeline(
            new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)),
            new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)));

        var signals = pipeline.Run(Array.Empty<string>(), new StubClock(Now));

        Assert.Empty(signals);
    }

    private static SignalPipeline CreatePipeline(FakeCalendar calendar, FakeMarketDataProvider provider)
    {
        return new SignalPipeline(
            new EarningsGate(calendar, provider),
            new TechnicalScorer(provider),
            new TradeGovernor());
    }

    private static IReadOnlyList<PriceBar> CreateBuyHistory(DateOnly asOfDate)
    {
        var bars = new List<PriceBar>(capacity: 200);
        var startDate = asOfDate.AddDays(-199);

        for (var index = 0; index < 186; index++)
        {
            bars.Add(CreateBar(startDate.AddDays(index), 100m + index));
        }

        for (var index = 186; index < 200; index++)
        {
            bars.Add(CreateBar(startDate.AddDays(index), 470m - index));
        }

        return bars;
    }

    private static IReadOnlyList<PriceBar> CreateSellHistory(DateOnly asOfDate)
    {
        var bars = new List<PriceBar>(capacity: 200);
        var startDate = asOfDate.AddDays(-199);

        for (var index = 0; index < 185; index++)
        {
            bars.Add(CreateBar(startDate.AddDays(index), 300m));
        }

        bars.Add(CreateBar(startDate.AddDays(185), 100m));

        for (var index = 186; index < 200; index++)
        {
            bars.Add(CreateBar(startDate.AddDays(index), index - 85m));
        }

        return bars;
    }

    private static PriceBar CreateBar(DateOnly day, decimal close)
    {
        var timestamp = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        return new PriceBar(timestamp, close, close, close, close, 1_000L);
    }

    private sealed class StubClock : IClock
    {
        public StubClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeCalendar : IEarningsCalendar
    {
        private readonly IReadOnlyDictionary<string, DateTimeOffset?> _datesByTicker;

        public FakeCalendar(IReadOnlyDictionary<string, DateTimeOffset?> datesByTicker)
        {
            _datesByTicker = datesByTicker;
        }

        public List<string> EarningsRequests { get; } = [];

        public IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> Load()
        {
            return _datesByTicker.ToDictionary(
                entry => entry.Key,
                entry => entry.Value is { } date
                    ? (IReadOnlyList<DateTimeOffset>)[date]
                    : Array.Empty<DateTimeOffset>(),
                StringComparer.Ordinal);
        }

        public DateTimeOffset? GetNextFutureEarningsDate(string ticker, DateTimeOffset referenceTimeUtc)
        {
            EarningsRequests.Add(ticker);

            return _datesByTicker.TryGetValue(ticker, out var date) && date > referenceTimeUtc
                ? date
                : null;
        }
    }

    private sealed class FakeMarketDataProvider : IMarketDataProvider
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<PriceBar>> _priceHistoryByTicker;

        public FakeMarketDataProvider(IReadOnlyDictionary<string, IReadOnlyList<PriceBar>> priceHistoryByTicker)
        {
            _priceHistoryByTicker = priceHistoryByTicker;
        }

        public List<string> PriceRequests { get; } = [];

        public IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate)
        {
            _ = asOfDate;
            PriceRequests.Add(ticker);
            return _priceHistoryByTicker[ticker];
        }

        public DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc)
        {
            _ = ticker;
            _ = asOfUtc;
            return null;
        }
    }
}
