using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Infrastructure.Persistence.Services;

namespace BlowingCandles.Infrastructure.Persistence.Providers;

public sealed class HistoricalSnapshotMarketDataProvider : IMarketDataProvider
{
    private readonly MarketDataSnapshotReadService _readService;
    private readonly IMarketDataProvider _earningsFallbackProvider;

    public HistoricalSnapshotMarketDataProvider(
        MarketDataSnapshotReadService readService,
        IMarketDataProvider earningsFallbackProvider)
    {
        _readService = readService;
        _earningsFallbackProvider = earningsFallbackProvider;
    }

    public IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate)
    {
        return _readService.GetHistoricalPriceHistory(ticker, asOfDate);
    }

    public DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc)
    {
        return _earningsFallbackProvider.GetNextEarningsDate(ticker, asOfUtc);
    }
}
