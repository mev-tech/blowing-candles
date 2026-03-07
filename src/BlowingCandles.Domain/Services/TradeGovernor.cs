using BlowingCandles.Domain.Models;
using BlowingCandles.Domain.Interfaces;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Domain.Services;

public sealed class TradeGovernor
{
    public IReadOnlyList<FinalSignal> Decide(IEnumerable<NewsSignal> newsSignals, IEnumerable<MarketSignal> marketSignals, IClock clock)
    {
        var newsByTicker = newsSignals
            .GroupBy(signal => signal.Ticker, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        var marketByTicker = marketSignals
            .GroupBy(signal => signal.Ticker, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        return newsByTicker.Keys
            .Union(marketByTicker.Keys, StringComparer.Ordinal)
            .OrderBy(ticker => ticker, StringComparer.Ordinal)
            .Select(ticker =>
            {
                var newsSignal = newsByTicker.GetValueOrDefault(ticker);
                var marketSignal = marketByTicker.GetValueOrDefault(ticker);

                return new FinalSignal(
                    ticker,
                    TradingAction.WAIT,
                    newsSignal?.State ?? global::BlowingCandles.Domain.Enums.NewsState.WAIT,
                    marketSignal?.Action ?? TradingAction.WAIT,
                    "NOT_IMPLEMENTED",
                    clock.UtcNow);
            })
            .ToArray();
    }
}
