using BlowingCandles.Infrastructure.Persistence.Entities;
using BlowingCandles.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlowingCandles.Infrastructure.Persistence.Services;

public sealed class MarketDataSnapshotPersistenceService
{
    private readonly AppDbContext _dbContext;

    public MarketDataSnapshotPersistenceService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public MarketDataRefreshPersistenceResult PersistRefresh(PersistMarketDataRefreshRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.FreshnessTtlSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Freshness TTL must be greater than zero.");
        }

        var requestedSymbols = NormalizeDistinctSymbols(request.RequestedSymbols);
        var persistedSymbols = NormalizePersistedSymbols(request.PersistedSymbols);
        var missingSymbols = NormalizeMissingSymbols(request.MissingSymbols);

        ValidateCoverage(requestedSymbols, persistedSymbols, missingSymbols);

        var run = new MarketDataRefreshRunEntity
        {
            RequestedAsOfDate = request.RequestedAsOfDate,
            StartedAtUtc = request.StartedAtUtc,
            Trigger = TrimToLength(request.Trigger, MarketDataPersistenceLimits.TriggerMaxLength)
                ?? throw new ArgumentException("Trigger is required.", nameof(request)),
            Provider = TrimToLength(request.Provider, MarketDataPersistenceLimits.ProviderMaxLength)
                ?? throw new ArgumentException("Provider is required.", nameof(request)),
            Status = MarketDataRefreshRunStatus.Running,
            RequestedSymbolCount = requestedSymbols.Count,
            PersistedSymbolCount = 0,
            MissingSymbolCount = 0,
            ErrorCode = TrimToLength(request.ErrorCode, MarketDataPersistenceLimits.ErrorCodeMaxLength),
            ErrorMessage = TrimToLength(request.ErrorMessage, MarketDataPersistenceLimits.ErrorMessageMaxLength)
        };

        _dbContext.MarketDataRefreshRuns.Add(run);
        _dbContext.SaveChanges();

        if (persistedSymbols.Count == 0)
        {
            run.Status = MarketDataRefreshRunStatus.Failed;
            run.CompletedAtUtc = request.CapturedAtUtc;
            run.MissingSymbolCount = missingSymbols.Count;
            _dbContext.SaveChanges();

            return new MarketDataRefreshPersistenceResult(run.Id, null, run.Status);
        }

        IDbContextTransaction? transaction = null;

        try
        {
            if (_dbContext.Database.IsRelational())
            {
                transaction = _dbContext.Database.BeginTransaction();
            }

            var snapshot = BuildSnapshot(request, run.Id, persistedSymbols, missingSymbols);

            _dbContext.MarketDataSnapshots.Add(snapshot);

            run.Status = snapshot.MissingSymbolCount == 0
                ? MarketDataRefreshRunStatus.Succeeded
                : MarketDataRefreshRunStatus.Partial;
            run.PersistedSymbolCount = snapshot.CoveredSymbolCount;
            run.MissingSymbolCount = snapshot.MissingSymbolCount;
            run.CompletedAtUtc = request.CapturedAtUtc;

            _dbContext.SaveChanges();
            transaction?.Commit();

            return new MarketDataRefreshPersistenceResult(run.Id, snapshot.Id, run.Status);
        }
        catch
        {
            transaction?.Rollback();
            MarkRunFailed(run.Id, request.CapturedAtUtc, request.ErrorCode, request.ErrorMessage);
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private MarketDataSnapshotEntity BuildSnapshot(
        PersistMarketDataRefreshRequest request,
        long refreshRunId,
        IReadOnlyList<NormalizedPersistedSymbol> persistedSymbols,
        IReadOnlyList<NormalizedMissingSymbol> missingSymbols)
    {
        var quoteEntities = persistedSymbols
            .SelectMany(
                symbol => symbol.Quotes.Select(
                    quote => new MarketDataSnapshotQuoteEntity
                    {
                        Symbol = symbol.Symbol,
                        QuoteDate = quote.QuoteDate,
                        MarketTimestampUtc = quote.MarketTimestampUtc,
                        Open = quote.Open,
                        High = quote.High,
                        Low = quote.Low,
                        Close = quote.Close,
                        Volume = quote.Volume
                    }))
            .OrderBy(x => x.Symbol)
            .ThenBy(x => x.QuoteDate)
            .ToList();

        var missingSymbolEntities = missingSymbols
            .Select(
                missingSymbol => new MarketDataSnapshotMissingSymbolEntity
                {
                    Symbol = missingSymbol.Symbol,
                    Reason = missingSymbol.Reason,
                    Detail = missingSymbol.Detail,
                    IsRetryable = missingSymbol.IsRetryable
                })
            .OrderBy(x => x.Symbol)
            .ToList();

        var snapshot = new MarketDataSnapshotEntity
        {
            RefreshRunId = refreshRunId,
            AsOfDate = request.RequestedAsOfDate,
            CapturedAtUtc = request.CapturedAtUtc,
            FreshnessTtlSeconds = request.FreshnessTtlSeconds,
            FreshUntilUtc = request.CapturedAtUtc.AddSeconds(request.FreshnessTtlSeconds),
            Status = missingSymbolEntities.Count == 0
                ? MarketDataSnapshotStatus.Complete
                : MarketDataSnapshotStatus.Partial,
            FirstQuoteDate = quoteEntities.Min(x => x.QuoteDate),
            LastQuoteDate = quoteEntities.Max(x => x.QuoteDate),
            QuoteRowCount = quoteEntities.Count,
            CoveredSymbolCount = persistedSymbols.Count,
            MissingSymbolCount = missingSymbolEntities.Count
        };

        foreach (var quoteEntity in quoteEntities)
        {
            quoteEntity.Snapshot = snapshot;
            snapshot.Quotes.Add(quoteEntity);
        }

        foreach (var missingSymbolEntity in missingSymbolEntities)
        {
            missingSymbolEntity.Snapshot = snapshot;
            snapshot.MissingSymbols.Add(missingSymbolEntity);
        }

        return snapshot;
    }

    private void MarkRunFailed(
        long runId,
        DateTimeOffset completedAtUtc,
        string? errorCode,
        string? errorMessage)
    {
        try
        {
            _dbContext.ChangeTracker.Clear();
            var run = _dbContext.MarketDataRefreshRuns.Single(x => x.Id == runId);
            run.Status = MarketDataRefreshRunStatus.Failed;
            run.CompletedAtUtc = completedAtUtc;
            run.ErrorCode = TrimToLength(errorCode, MarketDataPersistenceLimits.ErrorCodeMaxLength);
            run.ErrorMessage = TrimToLength(errorMessage, MarketDataPersistenceLimits.ErrorMessageMaxLength);
            _dbContext.SaveChanges();
        }
        catch
        {
        }
    }

    private static IReadOnlyList<string> NormalizeDistinctSymbols(IEnumerable<string> symbols)
    {
        return symbols
            .Select(NormalizeSymbol)
            .Where(symbol => symbol.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<NormalizedPersistedSymbol> NormalizePersistedSymbols(
        IEnumerable<PersistedMarketDataSymbol> symbols)
    {
        var normalizedSymbols = new List<NormalizedPersistedSymbol>();

        foreach (var symbol in symbols)
        {
            var normalizedSymbol = NormalizeSymbol(symbol.Symbol);
            if (normalizedSymbol.Length == 0)
            {
                continue;
            }

            var deduplicatedQuotes = symbol.Quotes
                .Select(quote => ValidateQuote(normalizedSymbol, quote))
                .OrderBy(quote => quote.QuoteDate)
                .ThenBy(quote => quote.MarketTimestampUtc)
                .GroupBy(quote => quote.QuoteDate)
                .Select(group => group.Last())
                .ToArray();

            if (deduplicatedQuotes.Length == 0)
            {
                continue;
            }

            normalizedSymbols.Add(new NormalizedPersistedSymbol(normalizedSymbol, deduplicatedQuotes));
        }

        return normalizedSymbols
            .GroupBy(symbol => symbol.Symbol, StringComparer.Ordinal)
            .Select(
                group =>
                {
                    var mergedQuotes = group
                        .SelectMany(symbol => symbol.Quotes)
                        .OrderBy(quote => quote.QuoteDate)
                        .ThenBy(quote => quote.MarketTimestampUtc)
                        .GroupBy(quote => quote.QuoteDate)
                        .Select(quoteGroup => quoteGroup.Last())
                        .ToArray();

                    return new NormalizedPersistedSymbol(group.Key, mergedQuotes);
                })
            .OrderBy(symbol => symbol.Symbol, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<NormalizedMissingSymbol> NormalizeMissingSymbols(
        IEnumerable<PersistedMarketDataMissingSymbol> symbols)
    {
        return symbols
            .Select(
                symbol => new NormalizedMissingSymbol(
                    NormalizeSymbol(symbol.Symbol),
                    symbol.Reason,
                    TrimToLength(symbol.Detail, MarketDataPersistenceLimits.DetailMaxLength),
                    symbol.IsRetryable))
            .Where(symbol => symbol.Symbol.Length > 0)
            .GroupBy(symbol => symbol.Symbol, StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderBy(symbol => symbol.Symbol, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateCoverage(
        IReadOnlyList<string> requestedSymbols,
        IReadOnlyList<NormalizedPersistedSymbol> persistedSymbols,
        IReadOnlyList<NormalizedMissingSymbol> missingSymbols)
    {
        var persistedSet = persistedSymbols.Select(symbol => symbol.Symbol).ToHashSet(StringComparer.Ordinal);
        var missingSet = missingSymbols.Select(symbol => symbol.Symbol).ToHashSet(StringComparer.Ordinal);

        var overlap = persistedSet.Intersect(missingSet, StringComparer.Ordinal).FirstOrDefault();
        if (overlap is not null)
        {
            throw new InvalidOperationException(
                $"Symbol '{overlap}' cannot be both persisted and missing in the same snapshot.");
        }

        if (persistedSet.Count == 0)
        {
            return;
        }

        foreach (var requestedSymbol in requestedSymbols)
        {
            if (persistedSet.Contains(requestedSymbol) || missingSet.Contains(requestedSymbol))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Requested symbol '{requestedSymbol}' is not covered by persisted or missing-symbol data.");
        }
    }

    private static string NormalizeSymbol(string symbol)
    {
        return symbol.Trim().ToUpperInvariant();
    }

    private static PersistedMarketDataQuote ValidateQuote(string symbol, PersistedMarketDataQuote quote)
    {
        if (quote.Open < 0m || quote.High < 0m || quote.Low < 0m || quote.Close < 0m)
        {
            throw new InvalidOperationException($"Symbol '{symbol}' contains a quote with a negative price.");
        }

        if (quote.Volume < 0)
        {
            throw new InvalidOperationException($"Symbol '{symbol}' contains a quote with a negative volume.");
        }

        return quote;
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }

    private sealed record NormalizedPersistedSymbol(
        string Symbol,
        IReadOnlyList<PersistedMarketDataQuote> Quotes);

    private sealed record NormalizedMissingSymbol(
        string Symbol,
        MarketDataMissingSymbolReason Reason,
        string? Detail,
        bool IsRetryable);
}
