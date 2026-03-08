using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Services;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Domain.Tests;

public sealed class TechnicalScorerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Score_EmptyWatchlist_ReturnsEmptyList()
    {
        var scorer = new TechnicalScorer(new FakeMarketDataProvider());

        var signals = scorer.Score([], new StubClock(Now));

        Assert.Empty(signals);
    }

    [Fact]
    public void Score_NormalizesTickersPreservesInputOrderAndUsesClockDate()
    {
        var history = CreatePriceHistory(Enumerable.Repeat(100m, 200));
        var marketDataProvider = new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>?>
        {
            ["MSFT"] = history,
            ["AAPL"] = history
        });
        var scorer = new TechnicalScorer(marketDataProvider);

        var signals = scorer.Score([" msft ", " aapl "], new StubClock(Now));

        Assert.Collection(
            signals,
            signal =>
            {
                Assert.Equal("MSFT", signal.Ticker);
                Assert.Equal(TradingAction.SELL, signal.Action);
            },
            signal =>
            {
                Assert.Equal("AAPL", signal.Ticker);
                Assert.Equal(TradingAction.SELL, signal.Action);
            });

        Assert.Equal(
            [("MSFT", DateOnly.FromDateTime(Now.UtcDateTime)), ("AAPL", DateOnly.FromDateTime(Now.UtcDateTime))],
            marketDataProvider.Requests);
    }

    [Fact]
    public void Score_AllConditionsMetAtRsiOversoldBoundary_ReturnsBuyWithScore100()
    {
        var closes = Enumerable.Repeat(100m, 185)
            .Concat([150m, 151m, 150m, 150m, 149m, 149m, 150m, 149m, 149m, 148m, 149m, 148m, 148m, 149m, 148m]);
        var scorer = new TechnicalScorer(new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>?>
        {
            ["AAPL"] = CreatePriceHistory(closes)
        }));

        var signal = Assert.Single(scorer.Score(["AAPL"], new StubClock(Now)));

        Assert.Equal(TradingAction.BUY, signal.Action);
        Assert.Equal(100, signal.Score);
        Assert.Equal(148m, signal.Close);
        Assert.Equal(40m, signal.Rsi14);
        Assert.Equal("ABOVE_SMA50,ABOVE_SMA200,GOLDEN_CROSS,RSI_OVERSOLD", signal.Reason);
        Assert.Equal(Now, signal.Timestamp);
    }

    [Fact]
    public void Score_RsiNeutralBoundaryWithTrendStrength_ReturnsBuyAtExactThreshold()
    {
        var closes = Enumerable.Repeat(100m, 185)
            .Concat([150m, 151m, 150m, 151m, 150m, 151m, 150m, 151m, 150m, 151m, 150m, 151m, 150m, 151m, 150m]);
        var scorer = new TechnicalScorer(new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>?>
        {
            ["AAPL"] = CreatePriceHistory(closes)
        }));

        var signal = Assert.Single(scorer.Score(["AAPL"], new StubClock(Now)));

        Assert.Equal(TradingAction.BUY, signal.Action);
        Assert.Equal(80, signal.Score);
        Assert.Equal(50m, signal.Rsi14);
        Assert.Equal("ABOVE_SMA50,ABOVE_SMA200,GOLDEN_CROSS,RSI_NEUTRAL", signal.Reason);
    }

    [Fact]
    public void Score_BuyViaRsiOversoldWithoutGoldenCross_ReturnsBuyWithScore80()
    {
        var closes = Enumerable.Repeat(150m, 150)
            .Concat(Enumerable.Repeat(130m, 35))
            .Concat([150m, 151m, 150m, 150m, 149m, 149m, 150m, 149m, 149m, 148m, 149m, 148m, 148m, 149m, 148m]);
        var scorer = new TechnicalScorer(new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>?>
        {
            ["AAPL"] = CreatePriceHistory(closes)
        }));

        var signal = Assert.Single(scorer.Score(["AAPL"], new StubClock(Now)));

        Assert.Equal(TradingAction.BUY, signal.Action);
        Assert.Equal(80, signal.Score);
        Assert.Equal(40m, signal.Rsi14);
        Assert.True(signal.Close > signal.Sma50);
        Assert.True(signal.Close > signal.Sma200);
        Assert.True(signal.Sma50 < signal.Sma200);
        Assert.Equal("ABOVE_SMA50,ABOVE_SMA200,RSI_OVERSOLD", signal.Reason);
    }

    [Fact]
    public void Score_RsiJustAboveNeutralBoundary_AddsZeroRsiPoints()
    {
        var closes = Enumerable.Repeat(100m, 185)
            .Concat([150m, 151m, 150m, 151m, 150m, 151m, 150m, 151m, 150m, 151m, 150m, 151m, 150m, 151m, 150.01m]);
        var scorer = new TechnicalScorer(new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>?>
        {
            ["AAPL"] = CreatePriceHistory(closes)
        }));

        var signal = Assert.Single(scorer.Score(["AAPL"], new StubClock(Now)));

        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(60, signal.Score);
        Assert.True(signal.Rsi14 > 50m);
        Assert.Equal("ABOVE_SMA50,ABOVE_SMA200,GOLDEN_CROSS", signal.Reason);
    }

    [Fact]
    public void Score_FlatMarketProducesNeutralRsiAndSellAtExactThreshold()
    {
        var scorer = new TechnicalScorer(new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>?>
        {
            ["AAPL"] = CreatePriceHistory(Enumerable.Repeat(100m, 200))
        }));

        var signal = Assert.Single(scorer.Score(["AAPL"], new StubClock(Now)));

        Assert.Equal(TradingAction.SELL, signal.Action);
        Assert.Equal(20, signal.Score);
        Assert.Equal(50m, signal.Rsi14);
        Assert.Equal(100m, signal.Close);
        Assert.Equal(100m, signal.Sma50);
        Assert.Equal(100m, signal.Sma200);
        Assert.Equal("RSI_NEUTRAL", signal.Reason);
    }

    [Fact]
    public void Score_CloseAboveSma50AndNeutralRsi_ReturnsWait()
    {
        var closes = Enumerable.Repeat(200m, 150)
            .Concat(Enumerable.Repeat(100m, 35))
            .Concat([110m, 111m, 110m, 111m, 110m, 111m, 110m, 111m, 110m, 111m, 110m, 111m, 110m, 111m, 110m]);
        var scorer = new TechnicalScorer(new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>?>
        {
            ["AAPL"] = CreatePriceHistory(closes)
        }));

        var signal = Assert.Single(scorer.Score(["AAPL"], new StubClock(Now)));

        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(40, signal.Score);
        Assert.Equal(50m, signal.Rsi14);
        Assert.Equal("ABOVE_SMA50,RSI_NEUTRAL", signal.Reason);
    }

    [Fact]
    public void Score_AllConditionsMissed_ReturnsSellWithScoreZero()
    {
        var closes = Enumerable.Repeat(200m, 150)
            .Concat(Enumerable.Repeat(100m, 35))
            .Concat(Enumerable.Range(80, 15).Select(value => (decimal)value));
        var scorer = new TechnicalScorer(new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>?>
        {
            ["AAPL"] = CreatePriceHistory(closes)
        }));

        var signal = Assert.Single(scorer.Score(["AAPL"], new StubClock(Now)));

        Assert.Equal(TradingAction.SELL, signal.Action);
        Assert.Equal(0, signal.Score);
        Assert.True(signal.Close < signal.Sma50);
        Assert.True(signal.Close < signal.Sma200);
        Assert.True(signal.Sma50 < signal.Sma200);
        Assert.True(signal.Rsi14 > 50m);
        Assert.Equal(string.Empty, signal.Reason);
    }

    [Fact]
    public void Score_ComputesSma50AndSma200FromLastClosingPrices()
    {
        var scorer = new TechnicalScorer(new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>?>
        {
            ["AAPL"] = CreatePriceHistory(Enumerable.Range(1, 200).Select(value => (decimal)value))
        }));

        var signal = Assert.Single(scorer.Score(["AAPL"], new StubClock(Now)));

        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(60, signal.Score);
        Assert.Equal(200m, signal.Close);
        Assert.Equal(175.5m, signal.Sma50);
        Assert.Equal(100.5m, signal.Sma200);
        Assert.Equal(100m, signal.Rsi14);
        Assert.Equal("ABOVE_SMA50,ABOVE_SMA200,GOLDEN_CROSS", signal.Reason);
    }

    [Fact]
    public void Score_AllLosses_ReturnsRsiZero()
    {
        var closes = Enumerable.Repeat(100m, 185)
            .Concat(Enumerable.Range(86, 15).Reverse().Select(value => (decimal)value));
        var scorer = new TechnicalScorer(new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>?>
        {
            ["AAPL"] = CreatePriceHistory(closes)
        }));

        var signal = Assert.Single(scorer.Score(["AAPL"], new StubClock(Now)));

        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(40, signal.Score);
        Assert.Equal(0m, signal.Rsi14);
        Assert.Equal("RSI_OVERSOLD", signal.Reason);
    }

    [Fact]
    public void Score_FewerThan200Prices_ReturnsWaitWithInsufficientData()
    {
        var scorer = new TechnicalScorer(new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>?>
        {
            ["AAPL"] = CreatePriceHistory(Enumerable.Repeat(100m, 199))
        }));

        var signal = Assert.Single(scorer.Score(["AAPL"], new StubClock(Now)));

        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(0, signal.Score);
        Assert.Equal("INSUFFICIENT_DATA", signal.Reason);
        Assert.Equal(0m, signal.Close);
        Assert.Equal(0m, signal.Sma50);
        Assert.Equal(0m, signal.Sma200);
        Assert.Equal(0m, signal.Rsi14);
    }

    [Fact]
    public void Score_EmptyHistory_ReturnsWaitWithMarketDataError()
    {
        var scorer = new TechnicalScorer(new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>?>
        {
            ["AAPL"] = Array.Empty<PriceBar>()
        }));

        var signal = Assert.Single(scorer.Score(["AAPL"], new StubClock(Now)));

        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(0, signal.Score);
        Assert.Equal("MARKET_DATA_ERROR", signal.Reason);
    }

    [Fact]
    public void Score_NullHistory_ReturnsWaitWithMarketDataError()
    {
        var scorer = new TechnicalScorer(new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>?>
        {
            ["AAPL"] = null
        }));

        var signal = Assert.Single(scorer.Score(["AAPL"], new StubClock(Now)));

        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal("MARKET_DATA_ERROR", signal.Reason);
    }

    [Fact]
    public void Score_ProviderThrows_ReturnsWaitWithMarketDataError()
    {
        var scorer = new TechnicalScorer(new FakeMarketDataProvider(priceHistoryException: new IOException("download failed")));

        var signal = Assert.Single(scorer.Score(["AAPL"], new StubClock(Now)));

        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(0, signal.Score);
        Assert.Equal("MARKET_DATA_ERROR", signal.Reason);
    }

    private static IReadOnlyList<PriceBar> CreatePriceHistory(IEnumerable<decimal> closes)
    {
        var timestamp = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        return closes
            .Select((close, index) => new PriceBar(
                timestamp.AddDays(index),
                close,
                close,
                close,
                close,
                1_000))
            .ToArray();
    }

    private sealed class StubClock : IClock
    {
        public StubClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeMarketDataProvider : IMarketDataProvider
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<PriceBar>?> _priceHistoryByTicker;
        private readonly Exception? _priceHistoryException;

        public FakeMarketDataProvider(
            IReadOnlyDictionary<string, IReadOnlyList<PriceBar>?>? priceHistoryByTicker = null,
            Exception? priceHistoryException = null)
        {
            _priceHistoryByTicker = NormalizePriceHistory(priceHistoryByTicker);
            _priceHistoryException = priceHistoryException;
        }

        public List<(string Ticker, DateOnly AsOfDate)> Requests { get; } = [];

        public IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate)
        {
            if (_priceHistoryException is not null)
            {
                throw _priceHistoryException;
            }

            var normalizedTicker = NormalizeTicker(ticker);
            Requests.Add((normalizedTicker, asOfDate));

            return _priceHistoryByTicker.TryGetValue(normalizedTicker, out var priceHistory)
                ? priceHistory!
                : Array.Empty<PriceBar>();
        }

        public DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc)
        {
            _ = ticker;
            _ = asOfUtc;
            return null;
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<PriceBar>?> NormalizePriceHistory(
            IReadOnlyDictionary<string, IReadOnlyList<PriceBar>?>? priceHistoryByTicker)
        {
            if (priceHistoryByTicker is null)
            {
                return new Dictionary<string, IReadOnlyList<PriceBar>?>(StringComparer.Ordinal);
            }

            return priceHistoryByTicker.ToDictionary(
                pair => NormalizeTicker(pair.Key),
                pair => pair.Value,
                StringComparer.Ordinal);
        }
    }

    private static string NormalizeTicker(string ticker)
    {
        return ticker.Trim().ToUpperInvariant();
    }
}
