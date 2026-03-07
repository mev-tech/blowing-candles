using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Domain.Services;

public sealed class TechnicalScorer
{
    private readonly IMarketDataProvider _marketDataProvider;

    public TechnicalScorer(IMarketDataProvider marketDataProvider)
    {
        _marketDataProvider = marketDataProvider;
    }

    public IReadOnlyList<MarketSignal> Score(IEnumerable<string> watchlist, IClock clock)
    {
        _ = _marketDataProvider;

        return watchlist
            .Select(ticker => ticker.Trim().ToUpperInvariant())
            .Where(ticker => !string.IsNullOrWhiteSpace(ticker))
            .Select(ticker => new MarketSignal(
                ticker,
                TradingAction.WAIT,
                0,
                0m,
                0m,
                0m,
                0m,
                "NOT_IMPLEMENTED",
                clock.UtcNow))
            .ToArray();
    }
}
