using BlowingCandles.Domain.Enums;
using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;
using BlowingCandles.Domain.Services;

namespace BlowingCandles.Domain.Tests;

public sealed class EarningsGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 7, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Check_LocalCalendarFutureOutsideBlockWindow_ReturnsTradeOkWithEmptyReason()
    {
        var calendar = new FakeEarningsCalendar(new Dictionary<string, IReadOnlyList<DateTimeOffset>>
        {
            ["AAPL"] = [Now.AddDays(30)]
        });
        var marketDataProvider = new FakeMarketDataProvider();
        var gate = new EarningsGate(calendar, marketDataProvider);

        var signals = gate.Check(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal("AAPL", signal.Ticker);
        Assert.Equal(NewsState.TRADE_OK, signal.State);
        Assert.Equal(string.Empty, signal.Reason);
        Assert.Equal(Now, signal.Timestamp);
        Assert.Empty(marketDataProvider.RequestedTickers);
    }

    [Fact]
    public void Check_LocalCalendarAtInclusiveBoundary_ReturnsNoTrade()
    {
        var calendar = new FakeEarningsCalendar(new Dictionary<string, IReadOnlyList<DateTimeOffset>>
        {
            ["AAPL"] = [Now.AddHours(48)]
        });
        var gate = new EarningsGate(calendar, new FakeMarketDataProvider());

        var signals = gate.Check(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(NewsState.NO_TRADE, signal.State);
        Assert.Equal("EARNINGS_LT_48H", signal.Reason);
    }

    [Fact]
    public void Check_LocalCalendarDeltaZeroWithDefaultWindow_ReturnsNoTrade()
    {
        var calendar = new FakeEarningsCalendar(new Dictionary<string, IReadOnlyList<DateTimeOffset>>
        {
            ["AAPL"] = [Now]
        });
        var gate = new EarningsGate(calendar, new FakeMarketDataProvider());

        var signals = gate.Check(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(NewsState.NO_TRADE, signal.State);
        Assert.Equal("EARNINGS_LT_48H", signal.Reason);
    }

    [Fact]
    public void Check_LocalCalendarEarningsOneMinuteAway_ReturnsNoTrade()
    {
        var calendar = new FakeEarningsCalendar(new Dictionary<string, IReadOnlyList<DateTimeOffset>>
        {
            ["AAPL"] = [Now.AddMinutes(1)]
        });
        var gate = new EarningsGate(calendar, new FakeMarketDataProvider());

        var signals = gate.Check(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(NewsState.NO_TRADE, signal.State);
        Assert.Equal("EARNINGS_LT_48H", signal.Reason);
    }

    [Fact]
    public void Check_LocalCalendarJustOutsideBlockWindow_ReturnsTradeOk()
    {
        var calendar = new FakeEarningsCalendar(new Dictionary<string, IReadOnlyList<DateTimeOffset>>
        {
            ["AAPL"] = [Now.AddHours(48).AddMinutes(1)]
        });
        var gate = new EarningsGate(calendar, new FakeMarketDataProvider());

        var signals = gate.Check(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(NewsState.TRADE_OK, signal.State);
        Assert.Equal(string.Empty, signal.Reason);
    }

    [Fact]
    public void Check_LocalCalendarPastOnly_ReturnsWaitAndSkipsYahooFallback()
    {
        var calendar = new FakeEarningsCalendar(new Dictionary<string, IReadOnlyList<DateTimeOffset>>
        {
            ["AAPL"] = [Now.AddDays(-2), Now.AddHours(-1)]
        });
        var marketDataProvider = new FakeMarketDataProvider(new Dictionary<string, DateTimeOffset?>
        {
            ["AAPL"] = Now.AddHours(4)
        });
        var gate = new EarningsGate(calendar, marketDataProvider);

        var signals = gate.Check(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(NewsState.WAIT, signal.State);
        Assert.Equal("CALENDAR_EXPIRED", signal.Reason);
        Assert.Empty(marketDataProvider.RequestedTickers);
    }

    [Fact]
    public void Check_TickerAbsentFromCalendar_YahooOutsideBlockWindow_ReturnsTradeOkWithYahooReason()
    {
        var marketDataProvider = new FakeMarketDataProvider(new Dictionary<string, DateTimeOffset?>
        {
            ["AAPL"] = Now.AddDays(5)
        });
        var gate = new EarningsGate(new FakeEarningsCalendar(), marketDataProvider);

        var signals = gate.Check(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(NewsState.TRADE_OK, signal.State);
        Assert.Equal("EARNINGS_FROM_YF", signal.Reason);
        Assert.Equal(["AAPL"], marketDataProvider.RequestedTickers);
    }

    [Fact]
    public void Check_TickerAbsentFromCalendar_YahooInsideBlockWindow_ReturnsNoTradeWithCombinedReason()
    {
        var marketDataProvider = new FakeMarketDataProvider(new Dictionary<string, DateTimeOffset?>
        {
            ["AAPL"] = Now.AddHours(24)
        });
        var gate = new EarningsGate(new FakeEarningsCalendar(), marketDataProvider);

        var signals = gate.Check(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(NewsState.NO_TRADE, signal.State);
        Assert.Equal("EARNINGS_FROM_YF,EARNINGS_LT_48H", signal.Reason);
    }

    [Fact]
    public void Check_TickerAbsentFromCalendar_YahooUnavailable_ReturnsWait()
    {
        var gate = new EarningsGate(new FakeEarningsCalendar(), new FakeMarketDataProvider());

        var signals = gate.Check(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(NewsState.WAIT, signal.State);
        Assert.Equal("DATA_UNAVAILABLE", signal.Reason);
    }

    [Fact]
    public void Check_TickerAbsentFromCalendar_YahooFailure_ReturnsWaitWithDataError()
    {
        var marketDataProvider = new FakeMarketDataProvider(earningsException: new IOException("yf failed"));
        var gate = new EarningsGate(new FakeEarningsCalendar(), marketDataProvider);

        var signals = gate.Check(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(NewsState.WAIT, signal.State);
        Assert.Equal("DATA_ERROR", signal.Reason);
    }

    [Fact]
    public void Check_LoadFailure_ReturnsWaitWithDataError()
    {
        var calendar = new FakeEarningsCalendar(loadException: new IOException("load failed"));
        var gate = new EarningsGate(calendar, new FakeMarketDataProvider());

        var signals = gate.Check(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(NewsState.WAIT, signal.State);
        Assert.Equal("DATA_ERROR", signal.Reason);
    }

    [Fact]
    public void Check_CalendarLookupException_ReturnsWaitWithDataError()
    {
        var calendar = new FakeEarningsCalendar(
            new Dictionary<string, IReadOnlyList<DateTimeOffset>>
            {
                ["AAPL"] = [Now.AddDays(5)]
            },
            lookupException: new IOException("lookup failed"));
        var gate = new EarningsGate(calendar, new FakeMarketDataProvider());

        var signals = gate.Check(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(NewsState.WAIT, signal.State);
        Assert.Equal("DATA_ERROR", signal.Reason);
    }

    [Fact]
    public void Check_EmptyWatchlist_ReturnsEmptyList()
    {
        var gate = new EarningsGate(new FakeEarningsCalendar(), new FakeMarketDataProvider());

        var signals = gate.Check([], new StubClock(Now));

        Assert.Empty(signals);
    }

    [Fact]
    public void Check_NormalizesTickersAndPreservesInputOrder()
    {
        var calendar = new FakeEarningsCalendar(new Dictionary<string, IReadOnlyList<DateTimeOffset>>
        {
            ["MSFT"] = [Now.AddDays(5)],
            ["AAPL"] = [Now.AddHours(2)]
        });
        var gate = new EarningsGate(calendar, new FakeMarketDataProvider());

        var signals = gate.Check([" msft ", " aapl "], new StubClock(Now));

        Assert.Collection(
            signals,
            signal =>
            {
                Assert.Equal("MSFT", signal.Ticker);
                Assert.Equal(NewsState.TRADE_OK, signal.State);
            },
            signal =>
            {
                Assert.Equal("AAPL", signal.Ticker);
                Assert.Equal(NewsState.NO_TRADE, signal.State);
                Assert.Equal("EARNINGS_LT_48H", signal.Reason);
            });
    }

    [Fact]
    public void Check_UsesNearestFutureLocalEarningsDate()
    {
        var calendar = new FakeEarningsCalendar(new Dictionary<string, IReadOnlyList<DateTimeOffset>>
        {
            ["AAPL"] = [Now.AddDays(-1), Now.AddHours(12), Now.AddDays(60)]
        });
        var gate = new EarningsGate(calendar, new FakeMarketDataProvider());

        var signals = gate.Check(["AAPL"], new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(NewsState.NO_TRADE, signal.State);
        Assert.Equal("EARNINGS_LT_48H", signal.Reason);
    }

    [Fact]
    public void Check_BlockWindowZero_OnlyBlocksExactCurrentTime()
    {
        var calendar = new FakeEarningsCalendar(new Dictionary<string, IReadOnlyList<DateTimeOffset>>
        {
            ["AAPL"] = [Now],
            ["MSFT"] = [Now.AddHours(1)]
        });
        var gate = new EarningsGate(calendar, new FakeMarketDataProvider(), blockWindowHours: 0);

        var signals = gate.Check(["AAPL", "MSFT"], new StubClock(Now));

        Assert.Collection(
            signals,
            signal =>
            {
                Assert.Equal("AAPL", signal.Ticker);
                Assert.Equal(NewsState.NO_TRADE, signal.State);
                Assert.Equal("EARNINGS_LT_48H", signal.Reason);
            },
            signal =>
            {
                Assert.Equal("MSFT", signal.Ticker);
                Assert.Equal(NewsState.TRADE_OK, signal.State);
                Assert.Equal(string.Empty, signal.Reason);
            });
    }

    [Fact]
    public void Check_CustomBlockWindow24Hours_AppliesCorrectWindow()
    {
        var calendar = new FakeEarningsCalendar(new Dictionary<string, IReadOnlyList<DateTimeOffset>>
        {
            ["AAPL"] = [Now.AddHours(20)],
            ["MSFT"] = [Now.AddHours(30)]
        });
        var gate = new EarningsGate(calendar, new FakeMarketDataProvider(), blockWindowHours: 24);

        var signals = gate.Check(["AAPL", "MSFT"], new StubClock(Now));

        Assert.Collection(
            signals,
            signal =>
            {
                Assert.Equal("AAPL", signal.Ticker);
                Assert.Equal(NewsState.NO_TRADE, signal.State);
                Assert.Equal("EARNINGS_LT_48H", signal.Reason);
            },
            signal =>
            {
                Assert.Equal("MSFT", signal.Ticker);
                Assert.Equal(NewsState.TRADE_OK, signal.State);
                Assert.Equal(string.Empty, signal.Reason);
            });
    }

    private sealed class StubClock : IClock
    {
        public StubClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeEarningsCalendar : IEarningsCalendar
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> _calendar;
        private readonly Exception? _loadException;
        private readonly Exception? _lookupException;

        public FakeEarningsCalendar(
            IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>>? calendar = null,
            Exception? loadException = null,
            Exception? lookupException = null)
        {
            _calendar = NormalizeCalendar(calendar);
            _loadException = loadException;
            _lookupException = lookupException;
        }

        public IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> Load()
        {
            if (_loadException is not null)
            {
                throw _loadException;
            }

            return _calendar;
        }

        public DateTimeOffset? GetNextFutureEarningsDate(string ticker, DateTimeOffset referenceTimeUtc)
        {
            if (_lookupException is not null)
            {
                throw _lookupException;
            }

            if (!_calendar.TryGetValue(NormalizeTicker(ticker), out var dates))
            {
                return null;
            }

            foreach (var date in dates)
            {
                if (date >= referenceTimeUtc)
                {
                    return date;
                }
            }

            return null;
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> NormalizeCalendar(
            IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>>? calendar)
        {
            if (calendar is null)
            {
                return new Dictionary<string, IReadOnlyList<DateTimeOffset>>(StringComparer.Ordinal);
            }

            return calendar.ToDictionary(
                pair => NormalizeTicker(pair.Key),
                pair => (IReadOnlyList<DateTimeOffset>)pair.Value.OrderBy(date => date).ToArray(),
                StringComparer.Ordinal);
        }
    }

    private sealed class FakeMarketDataProvider : IMarketDataProvider
    {
        private readonly IReadOnlyDictionary<string, DateTimeOffset?> _earningsDates;
        private readonly Exception? _earningsException;

        public FakeMarketDataProvider(
            IReadOnlyDictionary<string, DateTimeOffset?>? earningsDates = null,
            Exception? earningsException = null)
        {
            _earningsDates = NormalizeEarningsDates(earningsDates);
            _earningsException = earningsException;
        }

        public List<string> RequestedTickers { get; } = [];

        public IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate)
        {
            _ = ticker;
            _ = asOfDate;
            return Array.Empty<PriceBar>();
        }

        public DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc)
        {
            _ = asOfUtc;

            if (_earningsException is not null)
            {
                throw _earningsException;
            }

            var normalizedTicker = NormalizeTicker(ticker);
            RequestedTickers.Add(normalizedTicker);

            return _earningsDates.TryGetValue(normalizedTicker, out var earningsDate)
                ? earningsDate
                : null;
        }

        private static IReadOnlyDictionary<string, DateTimeOffset?> NormalizeEarningsDates(
            IReadOnlyDictionary<string, DateTimeOffset?>? earningsDates)
        {
            if (earningsDates is null)
            {
                return new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
            }

            return earningsDates.ToDictionary(
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
