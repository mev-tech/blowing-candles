using BlowingCandles.Domain.Enums;
using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;

namespace BlowingCandles.Domain.Services;

public sealed class EarningsGate
{
    private readonly IEarningsCalendar _earningsCalendar;
    private readonly IMarketDataProvider _marketDataProvider;

    public EarningsGate(IEarningsCalendar earningsCalendar, IMarketDataProvider marketDataProvider)
    {
        _earningsCalendar = earningsCalendar;
        _marketDataProvider = marketDataProvider;
    }

    public IReadOnlyList<NewsSignal> Check(IEnumerable<string> watchlist, IClock clock)
    {
        _ = _earningsCalendar;
        _ = _marketDataProvider;

        return watchlist
            .Select(ticker => ticker.Trim().ToUpperInvariant())
            .Where(ticker => !string.IsNullOrWhiteSpace(ticker))
            .Select(ticker => new NewsSignal(ticker, NewsState.WAIT, "NOT_IMPLEMENTED", clock.UtcNow))
            .ToArray();
    }
}
