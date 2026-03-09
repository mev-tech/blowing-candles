using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Infrastructure.Persistence.Services;

namespace BlowingCandles.Infrastructure.Persistence.Providers;

public sealed class LiveSnapshotMarketDataProvider : IMarketDataProvider
{
    private readonly MarketDataSnapshotReadService _readService;
    private readonly IMarketDataProvider _earningsFallbackProvider;
    private readonly IClock _clock;

    public LiveSnapshotMarketDataProvider(
        MarketDataSnapshotReadService readService,
        IMarketDataProvider earningsFallbackProvider,
        IClock clock)
    {
        _readService = readService;
        _earningsFallbackProvider = earningsFallbackProvider;
        _clock = clock;
    }

    public IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate)
    {
        return _readService.GetLivePriceHistory(ticker, asOfDate, _clock.UtcNow);
    }

    public DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc)
    {
        return _earningsFallbackProvider.GetNextEarningsDate(ticker, asOfUtc);
    }
}
