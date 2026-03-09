using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlowingCandles.Infrastructure.Persistence.Services;

public sealed class MarketDataSnapshotReadService
{
    private readonly AppDbContext _dbContext;

    public MarketDataSnapshotReadService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<PriceBar> GetLivePriceHistory(string ticker, DateOnly requestedAsOfDate, DateTimeOffset nowUtc)
    {
        return GetPriceHistory(
            ticker,
            CreateSnapshotQuery(requestedAsOfDate)
                .Where(snapshot => snapshot.FreshUntilUtc > nowUtc));
    }

    public IReadOnlyList<PriceBar> GetHistoricalPriceHistory(string ticker, DateOnly requestedAsOfDate)
    {
        return GetPriceHistory(ticker, CreateSnapshotQuery(requestedAsOfDate));
    }

    private IReadOnlyList<PriceBar> GetPriceHistory(string ticker, IQueryable<MarketDataSnapshotEntity> snapshotQuery)
    {
        var normalizedSymbol = ticker.Trim().ToUpperInvariant();
        if (normalizedSymbol.Length == 0)
        {
            return [];
        }

        var snapshotId = snapshotQuery
            .Select(snapshot => (long?)snapshot.Id)
            .FirstOrDefault();

        if (snapshotId is null)
        {
            return [];
        }

        var isMissing = _dbContext.MarketDataSnapshotMissingSymbols
            .AsNoTracking()
            .Any(x => x.SnapshotId == snapshotId.Value && x.Symbol == normalizedSymbol);

        if (isMissing)
        {
            return [];
        }

        return _dbContext.MarketDataSnapshotQuotes
            .AsNoTracking()
            .Where(x => x.SnapshotId == snapshotId.Value && x.Symbol == normalizedSymbol)
            .OrderBy(x => x.QuoteDate)
            .Select(x => new PriceBar(x.MarketTimestampUtc, x.Open, x.High, x.Low, x.Close, x.Volume))
            .ToArray();
    }

    private IQueryable<MarketDataSnapshotEntity> CreateSnapshotQuery(DateOnly requestedAsOfDate)
    {
        return _dbContext.MarketDataSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.AsOfDate <= requestedAsOfDate)
            .Where(
                snapshot => snapshot.Status == MarketDataSnapshotStatus.Complete
                    || snapshot.Status == MarketDataSnapshotStatus.Partial)
            .OrderByDescending(snapshot => snapshot.AsOfDate)
            .ThenByDescending(snapshot => snapshot.CapturedAtUtc);
    }
}
