using BlowingCandles.Infrastructure.Persistence.Entities;
using BlowingCandles.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlowingCandles.Infrastructure.Persistence.Services;

public sealed class SignalRunReadService
{
    private const int MaxRecentRunsLimit = 100;
    private readonly AppDbContext _dbContext;

    public SignalRunReadService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public SignalRunReadResult? GetLatestLiveRun()
    {
        var run = CreateRunQuery()
            .Where(x => !x.IsSimulation && x.Status == SignalRunStatus.Completed)
            .OrderByDescending(x => x.CompletedAtUtc)
            .FirstOrDefault();

        return run is null ? null : Map(run);
    }

    public SignalRunReadResult? GetRunById(long runId)
    {
        var run = CreateRunQuery()
            .SingleOrDefault(x => x.Id == runId);

        return run is null ? null : Map(run);
    }

    public IReadOnlyList<SignalRunReadResult> GetRecentRuns(int limit = 20)
    {
        if (limit <= 0)
        {
            return [];
        }

        var effectiveLimit = Math.Min(limit, MaxRecentRunsLimit);

        return CreateRunQuery()
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(effectiveLimit)
            .AsEnumerable()
            .Select(Map)
            .ToArray();
    }

    private IQueryable<SignalRunEntity> CreateRunQuery()
    {
        return _dbContext.SignalRuns
            .AsNoTracking()
            .Include(x => x.Results);
    }

    private static SignalRunReadResult Map(SignalRunEntity run)
    {
        return new SignalRunReadResult(
            run.Id,
            run.RunType,
            run.Trigger,
            run.Status,
            run.AsOfDate,
            run.IsSimulation,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.TickerCount,
            run.Results
                .OrderBy(x => x.Ticker, StringComparer.Ordinal)
                .Select(
                    x => new SignalRunSignalResult(
                        x.Ticker,
                        x.Action,
                        x.NewsState,
                        x.MarketAction,
                        x.Reason,
                        x.Timestamp))
                .ToArray(),
            run.ErrorMessage);
    }
}
