using BlowingCandles.Domain.Enums;
using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;
using BlowingCandles.Domain.Services;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Domain.Tests;

public sealed class TradeGovernorTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 7, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Decide_TradeOkBuy_EmitsBuyAndUpdatesState()
    {
        var stateStore = new FakeStateStore(CreateState());
        var governor = new TradeGovernor(maxBuysPerDay: 2, cooldownMinutes: 30, stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.TRADE_OK)],
            [CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal("AAPL", signal.Ticker);
        Assert.Equal(TradingAction.BUY, signal.Action);
        Assert.Equal(NewsState.TRADE_OK, signal.NewsState);
        Assert.Equal(TradingAction.BUY, signal.MarketAction);
        Assert.Equal("SCORE_80", signal.Reason);
        Assert.Equal(Now, signal.Timestamp);

        Assert.Equal(1, stateStore.SaveCalls);
        Assert.NotNull(stateStore.SavedState);
        Assert.Equal(1, stateStore.SavedState!.BuysToday);
        Assert.Equal(Now, stateStore.SavedState.LastBuyAt);
    }

    [Fact]
    public void Decide_NoTradeBuy_DowngradesToWaitAndPreservesOriginalSignals()
    {
        var stateStore = new FakeStateStore(CreateState());
        var governor = new TradeGovernor(stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.NO_TRADE)],
            [CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(NewsState.NO_TRADE, signal.NewsState);
        Assert.Equal(TradingAction.BUY, signal.MarketAction);
        Assert.Equal("BLOCKED_BY_NEWS", signal.Reason);
        Assert.Equal(0, stateStore.SaveCalls);
    }

    [Fact]
    public void Decide_WaitNews_DowngradesToWait()
    {
        var governor = new TradeGovernor();

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.WAIT)],
            [CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(NewsState.WAIT, signal.NewsState);
        Assert.Equal(TradingAction.BUY, signal.MarketAction);
        Assert.Equal("NEWS_WAIT", signal.Reason);
    }

    [Fact]
    public void Decide_MissingNews_ReturnsWaitWithNoNewsStateReason()
    {
        var governor = new TradeGovernor();

        var signals = governor.Decide(
            [],
            [CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(NewsState.WAIT, signal.NewsState);
        Assert.Equal(TradingAction.BUY, signal.MarketAction);
        Assert.Equal("NO_NEWS_STATE", signal.Reason);
    }

    [Fact]
    public void Decide_StaleNews_ReturnsWaitWithDataStaleReason()
    {
        var stateStore = new FakeStateStore(CreateState());
        var governor = new TradeGovernor(stateStore: stateStore, newsTtlMinutes: 180);

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.TRADE_OK, timestamp: Now.AddMinutes(-181))],
            [CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(NewsState.TRADE_OK, signal.NewsState);
        Assert.Equal(TradingAction.BUY, signal.MarketAction);
        Assert.Equal("DATA_STALE", signal.Reason);
        Assert.Equal(0, stateStore.SaveCalls);
    }

    [Fact]
    public void Decide_ExactStaleBoundary_IsNotBlocked()
    {
        var stateStore = new FakeStateStore(CreateState());
        var governor = new TradeGovernor(stateStore: stateStore, newsTtlMinutes: 180);

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.TRADE_OK, timestamp: Now.AddMinutes(-180))],
            [CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.BUY, signal.Action);
        Assert.Equal(1, stateStore.SaveCalls);
    }

    [Fact]
    public void Decide_PassesThroughSellAndWaitMarketActions()
    {
        var stateStore = new FakeStateStore(CreateState());
        var governor = new TradeGovernor(stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("MSFT", NewsState.TRADE_OK), CreateNews("AAPL", NewsState.TRADE_OK)],
            [
                CreateMarket("MSFT", TradingAction.SELL, "BELOW_SMA200"),
                CreateMarket("AAPL", TradingAction.WAIT, "SCORE_70")
            ],
            new StubClock(Now));

        Assert.Collection(
            signals,
            signal =>
            {
                Assert.Equal("AAPL", signal.Ticker);
                Assert.Equal(TradingAction.WAIT, signal.Action);
                Assert.Equal("SCORE_70", signal.Reason);
            },
            signal =>
            {
                Assert.Equal("MSFT", signal.Ticker);
                Assert.Equal(TradingAction.SELL, signal.Action);
                Assert.Equal("BELOW_SMA200", signal.Reason);
            });

        Assert.Equal(0, stateStore.SaveCalls);
    }

    [Fact]
    public void Decide_MissingMarket_ReturnsWaitWithNoMarketDataReason()
    {
        var governor = new TradeGovernor();

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.TRADE_OK)],
            [],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(NewsState.TRADE_OK, signal.NewsState);
        Assert.Equal(TradingAction.WAIT, signal.MarketAction);
        Assert.Equal("NO_MARKET_DATA", signal.Reason);
    }

    [Fact]
    public void Decide_BlocksBuyWhenMaxBuysReached()
    {
        var governor = new TradeGovernor(
            maxBuysPerDay: 2,
            stateStore: new FakeStateStore(CreateState(buysToday: 2)));

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.TRADE_OK)],
            [CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal("MAX_BUYS_REACHED", signal.Reason);
    }

    [Fact]
    public void Decide_ProcessesTickersAlphabeticallySoEarlierBuyConsumesSlotsFirst()
    {
        var stateStore = new FakeStateStore(CreateState());
        var governor = new TradeGovernor(maxBuysPerDay: 1, stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("TSLA", NewsState.TRADE_OK), CreateNews("AAPL", NewsState.TRADE_OK)],
            [
                CreateMarket("TSLA", TradingAction.BUY, "SCORE_90"),
                CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")
            ],
            new StubClock(Now));

        Assert.Collection(
            signals,
            signal =>
            {
                Assert.Equal("AAPL", signal.Ticker);
                Assert.Equal(TradingAction.BUY, signal.Action);
            },
            signal =>
            {
                Assert.Equal("TSLA", signal.Ticker);
                Assert.Equal(TradingAction.WAIT, signal.Action);
                Assert.Equal("MAX_BUYS_REACHED", signal.Reason);
            });

        Assert.Equal(1, stateStore.SaveCalls);
        Assert.Equal(1, stateStore.SavedState!.BuysToday);
    }

    [Fact]
    public void Decide_MultipleEligibleBuys_IncrementStateForEachBuy()
    {
        var stateStore = new FakeStateStore(CreateState());
        var governor = new TradeGovernor(maxBuysPerDay: 2, stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("MSFT", NewsState.TRADE_OK), CreateNews("AAPL", NewsState.TRADE_OK)],
            [
                CreateMarket("MSFT", TradingAction.BUY, "SCORE_90"),
                CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")
            ],
            new StubClock(Now));

        Assert.All(signals, signal => Assert.Equal(TradingAction.BUY, signal.Action));
        Assert.Equal(2, stateStore.SaveCalls);
        Assert.Equal(2, stateStore.SavedState!.BuysToday);
        Assert.Equal(Now, stateStore.SavedState.LastBuyAt);
    }

    [Fact]
    public void Decide_BlocksBuyWhenCooldownIsActive()
    {
        var governor = new TradeGovernor(
            cooldownMinutes: 30,
            stateStore: new FakeStateStore(CreateState(lastBuyAt: Now.AddMinutes(-10))));

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.TRADE_OK)],
            [CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal("COOLDOWN_ACTIVE", signal.Reason);
    }

    [Fact]
    public void Decide_AllowsBuyAtExactCooldownBoundary()
    {
        var stateStore = new FakeStateStore(CreateState(lastBuyAt: Now.AddMinutes(-30)));
        var governor = new TradeGovernor(cooldownMinutes: 30, stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.TRADE_OK)],
            [CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.BUY, signal.Action);
        Assert.Equal(1, stateStore.SaveCalls);
        Assert.Equal(1, stateStore.SavedState!.BuysToday);
    }

    [Fact]
    public void Decide_CooldownZero_DoesNotBlockSequentialBuys()
    {
        var stateStore = new FakeStateStore(CreateState());
        var governor = new TradeGovernor(maxBuysPerDay: 2, cooldownMinutes: 0, stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("MSFT", NewsState.TRADE_OK), CreateNews("AAPL", NewsState.TRADE_OK)],
            [
                CreateMarket("MSFT", TradingAction.BUY, "SCORE_90"),
                CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")
            ],
            new StubClock(Now));

        Assert.All(signals, signal => Assert.Equal(TradingAction.BUY, signal.Action));
        Assert.Equal(2, stateStore.SaveCalls);
        Assert.Equal(2, stateStore.SavedState!.BuysToday);
    }

    [Fact]
    public void Decide_StateLoadFailure_FallsBackToEmptyState()
    {
        var stateStore = new FakeStateStore(
            loadedState: CreateState(buysToday: 99, lastBuyAt: Now.AddMinutes(-1)),
            loadException: new IOException("load failed"));
        var governor = new TradeGovernor(maxBuysPerDay: 1, cooldownMinutes: 30, stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.TRADE_OK)],
            [CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.BUY, signal.Action);
        Assert.Equal(1, stateStore.SaveCalls);
        Assert.Equal(1, stateStore.SavedState!.BuysToday);
    }

    [Fact]
    public void Decide_StateSaveFailure_DowngradesBuyToWait()
    {
        var stateStore = new FakeStateStore(
            CreateState(),
            saveFailures: [new IOException("save failed")]);
        var governor = new TradeGovernor(stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.TRADE_OK)],
            [CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(TradingAction.BUY, signal.MarketAction);
        Assert.Equal("STATE_ERROR", signal.Reason);
        Assert.Equal(1, stateStore.SaveCalls);
        Assert.Null(stateStore.SavedState);
    }

    [Fact]
    public void Decide_StateSaveFailure_RemainsIsolatedToAffectedTicker()
    {
        var stateStore = new FakeStateStore(
            CreateState(),
            saveFailures: [new IOException("first save failed"), null]);
        var governor = new TradeGovernor(maxBuysPerDay: 2, stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("MSFT", NewsState.TRADE_OK), CreateNews("AAPL", NewsState.TRADE_OK)],
            [
                CreateMarket("MSFT", TradingAction.BUY, "SCORE_90"),
                CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")
            ],
            new StubClock(Now));

        Assert.Collection(
            signals,
            signal =>
            {
                Assert.Equal("AAPL", signal.Ticker);
                Assert.Equal(TradingAction.WAIT, signal.Action);
                Assert.Equal("STATE_ERROR", signal.Reason);
            },
            signal =>
            {
                Assert.Equal("MSFT", signal.Ticker);
                Assert.Equal(TradingAction.BUY, signal.Action);
            });

        Assert.Equal(2, stateStore.SaveCalls);
        Assert.Equal(1, stateStore.SavedState!.BuysToday);
    }

    [Fact]
    public void Decide_UnsupportedNewsState_FailsSafeToWait()
    {
        var stateStore = new FakeStateStore(CreateState());
        var governor = new TradeGovernor(stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.MANAGE)],
            [CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(NewsState.MANAGE, signal.NewsState);
        Assert.Equal(TradingAction.BUY, signal.MarketAction);
        Assert.Equal("UNSUPPORTED_NEWS_STATE", signal.Reason);
        Assert.Equal(0, stateStore.SaveCalls);
    }

    [Fact]
    public void Decide_UnsupportedMarketAction_FailsSafeToWait()
    {
        var stateStore = new FakeStateStore(CreateState());
        var governor = new TradeGovernor(stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.TRADE_OK)],
            [CreateMarket("AAPL", TradingAction.IGNORE, "NOT_SUPPORTED")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(NewsState.TRADE_OK, signal.NewsState);
        Assert.Equal(TradingAction.IGNORE, signal.MarketAction);
        Assert.Equal("UNSUPPORTED_MARKET_ACTION", signal.Reason);
        Assert.Equal(0, stateStore.SaveCalls);
    }

    [Fact]
    public void Decide_DuplicateNewsSignals_ReturnsWaitWithFailSafeReason()
    {
        var stateStore = new FakeStateStore(CreateState());
        var governor = new TradeGovernor(stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.TRADE_OK), CreateNews("AAPL", NewsState.WAIT)],
            [CreateMarket("AAPL", TradingAction.BUY, "SCORE_80")],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(NewsState.TRADE_OK, signal.NewsState);
        Assert.Equal(TradingAction.BUY, signal.MarketAction);
        Assert.Equal("DUPLICATE_NEWS_SIGNAL", signal.Reason);
        Assert.Equal(0, stateStore.SaveCalls);
    }

    [Fact]
    public void Decide_DuplicateMarketSignals_ReturnsWaitWithFailSafeReason()
    {
        var stateStore = new FakeStateStore(CreateState());
        var governor = new TradeGovernor(stateStore: stateStore);

        var signals = governor.Decide(
            [CreateNews("AAPL", NewsState.TRADE_OK)],
            [
                CreateMarket("AAPL", TradingAction.BUY, "SCORE_80"),
                CreateMarket("AAPL", TradingAction.SELL, "BELOW_SMA200")
            ],
            new StubClock(Now));

        var signal = Assert.Single(signals);
        Assert.Equal(TradingAction.WAIT, signal.Action);
        Assert.Equal(NewsState.TRADE_OK, signal.NewsState);
        Assert.Equal(TradingAction.BUY, signal.MarketAction);
        Assert.Equal("DUPLICATE_MARKET_SIGNAL", signal.Reason);
        Assert.Equal(0, stateStore.SaveCalls);
    }

    [Fact]
    public void Decide_EmptyInputs_ReturnsEmptyList()
    {
        var governor = new TradeGovernor();

        var signals = governor.Decide([], [], new StubClock(Now));

        Assert.Empty(signals);
    }

    private static NewsSignal CreateNews(string ticker, NewsState state, DateTimeOffset? timestamp = null)
    {
        return new NewsSignal(ticker, state, $"NEWS_{state}", timestamp ?? Now);
    }

    private static MarketSignal CreateMarket(string ticker, TradingAction action, string reason, DateTimeOffset? timestamp = null)
    {
        return new MarketSignal(ticker, action, 80, 100m, 90m, 80m, 55m, reason, timestamp ?? Now);
    }

    private static TradeGovernorState CreateState(int buysToday = 0, DateTimeOffset? lastBuyAt = null)
    {
        return new TradeGovernorState("2026-03-07", buysToday, lastBuyAt);
    }

    private sealed class StubClock : IClock
    {
        public StubClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeStateStore : ITradeGovernorStateStore
    {
        private readonly Exception? _loadException;
        private readonly Queue<Exception?> _saveFailures;

        public FakeStateStore(
            TradeGovernorState loadedState,
            Exception? loadException = null,
            IEnumerable<Exception?>? saveFailures = null)
        {
            LoadedState = loadedState;
            _loadException = loadException;
            _saveFailures = saveFailures is null
                ? new Queue<Exception?>()
                : new Queue<Exception?>(saveFailures);
        }

        public TradeGovernorState LoadedState { get; }

        public TradeGovernorState? SavedState { get; private set; }

        public int SaveCalls { get; private set; }

        public TradeGovernorState Load(IClock clock)
        {
            _ = clock;

            if (_loadException is not null)
            {
                throw _loadException;
            }

            return LoadedState;
        }

        public void Save(TradeGovernorState state)
        {
            SaveCalls++;

            if (_saveFailures.Count > 0 && _saveFailures.Dequeue() is { } failure)
            {
                throw failure;
            }

            SavedState = state;
        }
    }
}
