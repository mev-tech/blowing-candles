using BlowingCandles.Domain.Interfaces;

namespace BlowingCandles.Infrastructure.MarketData;

public sealed class YahooFinanceAdapter : IMarketDataProvider
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
