using BlowingCandles.Domain.Models;
using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Enums;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Domain.Services;

public sealed class TradeGovernor
{
    private const string StateErrorReason = "STATE_ERROR";
    private const string DataStaleReason = "DATA_STALE";
    private const string DuplicateNewsSignalReason = "DUPLICATE_NEWS_SIGNAL";
    private const string DuplicateMarketSignalReason = "DUPLICATE_MARKET_SIGNAL";
    private const string UnsupportedNewsStateReason = "UNSUPPORTED_NEWS_STATE";
    private const string UnsupportedMarketActionReason = "UNSUPPORTED_MARKET_ACTION";

    private readonly int _maxBuysPerDay;
    private readonly TimeSpan _cooldown;
    private readonly TimeSpan _newsTtl;
    private readonly ITradeGovernorStateStore? _stateStore;

    public TradeGovernor(
        int maxBuysPerDay = int.MaxValue,
        int cooldownMinutes = 0,
        ITradeGovernorStateStore? stateStore = null,
        int newsTtlMinutes = 180)
    {
        _maxBuysPerDay = maxBuysPerDay;
        _cooldown = TimeSpan.FromMinutes(cooldownMinutes);
        _newsTtl = TimeSpan.FromMinutes(newsTtlMinutes);
        _stateStore = stateStore;
    }

    public IReadOnlyList<FinalSignal> Decide(IEnumerable<NewsSignal> newsSignals, IEnumerable<MarketSignal> marketSignals, IClock clock)
    {
        var now = clock.UtcNow;
        var newsIndex = BuildIndex(newsSignals, signal => signal.Ticker);
        var marketIndex = BuildIndex(marketSignals, signal => signal.Ticker);
        var state = LoadState(clock, now);

        var signals = new List<FinalSignal>();

        foreach (var ticker in newsIndex.ByTicker.Keys
                     .Union(marketIndex.ByTicker.Keys, StringComparer.Ordinal)
                     .OrderBy(ticker => ticker, StringComparer.Ordinal))
        {
            var newsSignal = newsIndex.ByTicker.GetValueOrDefault(ticker);
            var marketSignal = marketIndex.ByTicker.GetValueOrDefault(ticker);
            var marketAction = marketSignal?.Action ?? TradingAction.WAIT;

            if (newsIndex.DuplicateTickers.Contains(ticker))
            {
                signals.Add(CreateSignal(ticker, TradingAction.WAIT, newsSignal?.State ?? NewsState.WAIT, marketAction, DuplicateNewsSignalReason, now));
                continue;
            }

            if (newsSignal is null)
            {
                signals.Add(CreateSignal(ticker, TradingAction.WAIT, NewsState.WAIT, marketAction, "NO_NEWS_STATE", now));
                continue;
            }

            if ((now - newsSignal.Timestamp) > _newsTtl)
            {
                signals.Add(CreateSignal(ticker, TradingAction.WAIT, newsSignal.State, marketAction, DataStaleReason, now));
                continue;
            }

            switch (newsSignal.State)
            {
                case NewsState.NO_TRADE:
                    signals.Add(CreateSignal(ticker, TradingAction.WAIT, newsSignal.State, marketAction, "BLOCKED_BY_NEWS", now));
                    continue;
                case NewsState.WAIT:
                    signals.Add(CreateSignal(ticker, TradingAction.WAIT, newsSignal.State, marketAction, "NEWS_WAIT", now));
                    continue;
                case NewsState.TRADE_OK:
                    break;
                default:
                    signals.Add(CreateSignal(ticker, TradingAction.WAIT, newsSignal.State, marketAction, UnsupportedNewsStateReason, now));
                    continue;
            }

            if (marketIndex.DuplicateTickers.Contains(ticker))
            {
                signals.Add(CreateSignal(ticker, TradingAction.WAIT, newsSignal.State, marketAction, DuplicateMarketSignalReason, now));
                continue;
            }

            if (marketSignal is null)
            {
                signals.Add(CreateSignal(ticker, TradingAction.WAIT, newsSignal.State, TradingAction.WAIT, "NO_MARKET_DATA", now));
                continue;
            }

            switch (marketSignal.Action)
            {
                case TradingAction.SELL:
                case TradingAction.WAIT:
                    signals.Add(CreateSignal(ticker, marketSignal.Action, newsSignal.State, marketSignal.Action, marketSignal.Reason, now));
                    continue;
                case TradingAction.BUY:
                    break;
                default:
                    signals.Add(CreateSignal(ticker, TradingAction.WAIT, newsSignal.State, marketSignal.Action, UnsupportedMarketActionReason, now));
                    continue;
            }

            if (state.BuysToday >= _maxBuysPerDay)
            {
                signals.Add(CreateSignal(ticker, TradingAction.WAIT, newsSignal.State, marketSignal.Action, "MAX_BUYS_REACHED", now));
                continue;
            }

            if (_cooldown > TimeSpan.Zero &&
                state.LastBuyAt is { } lastBuyAt &&
                (now - lastBuyAt) < _cooldown)
            {
                signals.Add(CreateSignal(ticker, TradingAction.WAIT, newsSignal.State, marketSignal.Action, "COOLDOWN_ACTIVE", now));
                continue;
            }

            var updatedState = RecordBuy(state, now);
            if (!TrySaveState(updatedState))
            {
                signals.Add(CreateSignal(ticker, TradingAction.WAIT, newsSignal.State, marketSignal.Action, StateErrorReason, now));
                continue;
            }

            state = updatedState;
            signals.Add(CreateSignal(ticker, TradingAction.BUY, newsSignal.State, marketSignal.Action, marketSignal.Reason, now));
        }

        return signals;
    }

    private TradeGovernorState LoadState(IClock clock, DateTimeOffset now)
    {
        if (_stateStore is null)
        {
            return CreateEmptyState(now);
        }

        try
        {
            return _stateStore.Load(clock) ?? CreateEmptyState(now);
        }
        catch
        {
            return CreateEmptyState(now);
        }
    }

    private bool TrySaveState(TradeGovernorState state)
    {
        if (_stateStore is null)
        {
            return true;
        }

        try
        {
            _stateStore.Save(state);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static FinalSignal CreateSignal(
        string ticker,
        TradingAction action,
        NewsState newsState,
        TradingAction marketAction,
        string reason,
        DateTimeOffset timestamp)
    {
        return new FinalSignal(ticker, action, newsState, marketAction, reason, timestamp);
    }

    private static SignalIndex<TSignal> BuildIndex<TSignal>(
        IEnumerable<TSignal> signals,
        Func<TSignal, string> tickerSelector)
    {
        var byTicker = new Dictionary<string, TSignal>(StringComparer.Ordinal);
        var duplicateTickers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var signal in signals)
        {
            var ticker = tickerSelector(signal);
            if (!byTicker.TryAdd(ticker, signal))
            {
                duplicateTickers.Add(ticker);
            }
        }

        return new SignalIndex<TSignal>(byTicker, duplicateTickers);
    }

    private static TradeGovernorState CreateEmptyState(DateTimeOffset now)
    {
        return new TradeGovernorState(
            DateOnly.FromDateTime(now.UtcDateTime).ToString("yyyy-MM-dd"),
            0,
            null);
    }

    private static TradeGovernorState RecordBuy(TradeGovernorState state, DateTimeOffset whenUtc)
    {
        return state with
        {
            Day = DateOnly.FromDateTime(whenUtc.UtcDateTime).ToString("yyyy-MM-dd"),
            BuysToday = state.BuysToday + 1,
            LastBuyAt = whenUtc.ToUniversalTime()
        };
    }

    private sealed record SignalIndex<TSignal>(
        IReadOnlyDictionary<string, TSignal> ByTicker,
        IReadOnlySet<string> DuplicateTickers);
}
