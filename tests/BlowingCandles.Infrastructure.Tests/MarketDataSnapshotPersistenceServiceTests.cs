using BlowingCandles.Infrastructure.Persistence;
using BlowingCandles.Infrastructure.Persistence.Entities;
using BlowingCandles.Infrastructure.Persistence.Models;
using BlowingCandles.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace BlowingCandles.Infrastructure.Tests;

public sealed class MarketDataSnapshotPersistenceServiceTests
{
    [Fact]
    public void PersistRefresh_FullSuccess_WritesSucceededRunAndCompleteSnapshot()
    {
        using var dbContext = CreateInMemoryContext();
        var service = new MarketDataSnapshotPersistenceService(dbContext);
        var capturedAtUtc = new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero);

        var result = service.PersistRefresh(
            new PersistMarketDataRefreshRequest(
                new DateOnly(2026, 3, 8),
                new DateTimeOffset(2026, 3, 8, 19, 30, 0, TimeSpan.Zero),
                capturedAtUtc,
                "scheduled",
                "yahoo-finance",
                3600,
                ["aapl", "msft"],
                [
                    CreateSymbol("aapl", new DateOnly(2026, 3, 6), 100m, 101m),
                    CreateSymbol("msft", new DateOnly(2026, 3, 6), 200m, 201m)
                ],
                []));

        var run = Assert.Single(dbContext.MarketDataRefreshRuns);
        var snapshot = Assert.Single(dbContext.MarketDataSnapshots);

        Assert.Equal(MarketDataRefreshRunStatus.Succeeded, result.Status);
        Assert.Equal(run.Id, result.RefreshRunId);
        Assert.Equal(snapshot.Id, result.SnapshotId);
        Assert.Equal(MarketDataRefreshRunStatus.Succeeded, run.Status);
        Assert.Equal(2, run.RequestedSymbolCount);
        Assert.Equal(2, run.PersistedSymbolCount);
        Assert.Equal(0, run.MissingSymbolCount);
        Assert.Equal(capturedAtUtc, run.CompletedAtUtc);
        Assert.Equal(MarketDataSnapshotStatus.Complete, snapshot.Status);
        Assert.Equal(4, snapshot.QuoteRowCount);
        Assert.Equal(2, snapshot.CoveredSymbolCount);
        Assert.Equal(0, snapshot.MissingSymbolCount);
        Assert.Equal(new DateOnly(2026, 3, 6), snapshot.FirstQuoteDate);
        Assert.Equal(new DateOnly(2026, 3, 7), snapshot.LastQuoteDate);
        Assert.Equal(capturedAtUtc.AddSeconds(3600), snapshot.FreshUntilUtc);
        Assert.Equal(4, dbContext.MarketDataSnapshotQuotes.Count());
        Assert.Empty(dbContext.MarketDataSnapshotMissingSymbols);
    }

    [Fact]
    public void PersistRefresh_PartialSuccess_WritesMissingSymbolsAndPartialStatuses()
    {
        using var dbContext = CreateInMemoryContext();
        var service = new MarketDataSnapshotPersistenceService(dbContext);

        var result = service.PersistRefresh(
            new PersistMarketDataRefreshRequest(
                new DateOnly(2026, 3, 8),
                new DateTimeOffset(2026, 3, 8, 19, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
                "manual",
                "yahoo-finance",
                1800,
                ["AAPL", "MSFT"],
                [CreateSymbol("AAPL", new DateOnly(2026, 3, 7), 100m, 102m)],
                [new PersistedMarketDataMissingSymbol("msft", MarketDataMissingSymbolReason.EmptySeries, "no rows", true)]));

        var run = Assert.Single(dbContext.MarketDataRefreshRuns);
        var snapshot = Assert.Single(dbContext.MarketDataSnapshots);
        var missingSymbol = Assert.Single(dbContext.MarketDataSnapshotMissingSymbols);

        Assert.Equal(MarketDataRefreshRunStatus.Partial, result.Status);
        Assert.Equal(MarketDataRefreshRunStatus.Partial, run.Status);
        Assert.Equal(1, run.PersistedSymbolCount);
        Assert.Equal(1, run.MissingSymbolCount);
        Assert.Equal(MarketDataSnapshotStatus.Partial, snapshot.Status);
        Assert.Equal(1, snapshot.CoveredSymbolCount);
        Assert.Equal(1, snapshot.MissingSymbolCount);
        Assert.Equal("MSFT", missingSymbol.Symbol);
        Assert.Equal(MarketDataMissingSymbolReason.EmptySeries, missingSymbol.Reason);
        Assert.Equal("no rows", missingSymbol.Detail);
        Assert.True(missingSymbol.IsRetryable);
    }

    [Fact]
    public void PersistRefresh_TotalFailure_WritesFailedRunWithoutSnapshot()
    {
        using var dbContext = CreateInMemoryContext();
        var service = new MarketDataSnapshotPersistenceService(dbContext);

        var result = service.PersistRefresh(
            new PersistMarketDataRefreshRequest(
                new DateOnly(2026, 3, 8),
                new DateTimeOffset(2026, 3, 8, 19, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
                "repair",
                "yahoo-finance",
                600,
                ["AAPL", "MSFT"],
                [],
                [],
                "transport_error",
                "provider timed out"));

        var run = Assert.Single(dbContext.MarketDataRefreshRuns);

        Assert.Equal(MarketDataRefreshRunStatus.Failed, result.Status);
        Assert.Null(result.SnapshotId);
        Assert.Equal(MarketDataRefreshRunStatus.Failed, run.Status);
        Assert.Equal("transport_error", run.ErrorCode);
        Assert.Equal("provider timed out", run.ErrorMessage);
        Assert.Empty(dbContext.MarketDataSnapshots);
        Assert.Empty(dbContext.MarketDataSnapshotQuotes);
        Assert.Empty(dbContext.MarketDataSnapshotMissingSymbols);
    }

    [Fact]
    public void PersistRefresh_DeduplicatesBarsBySymbolAndQuoteDate()
    {
        using var dbContext = CreateInMemoryContext();
        var service = new MarketDataSnapshotPersistenceService(dbContext);

        service.PersistRefresh(
            new PersistMarketDataRefreshRequest(
                new DateOnly(2026, 3, 8),
                new DateTimeOffset(2026, 3, 8, 19, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
                "scheduled",
                "yahoo-finance",
                900,
                ["AAPL"],
                [
                    new PersistedMarketDataSymbol(
                        "AAPL",
                        [
                            new PersistedMarketDataQuote(
                                new DateOnly(2026, 3, 7),
                                new DateTimeOffset(2026, 3, 7, 20, 0, 0, TimeSpan.Zero),
                                100m,
                                102m,
                                99m,
                                101m,
                                1000),
                            new PersistedMarketDataQuote(
                                new DateOnly(2026, 3, 7),
                                new DateTimeOffset(2026, 3, 7, 21, 0, 0, TimeSpan.Zero),
                                100m,
                                103m,
                                99m,
                                102m,
                                2000)
                        ])
                ],
                []));

        var quote = Assert.Single(dbContext.MarketDataSnapshotQuotes);

        Assert.Equal(new DateOnly(2026, 3, 7), quote.QuoteDate);
        Assert.Equal(102m, quote.Close);
        Assert.Equal(2000L, quote.Volume);
    }

    [Fact]
    public void PersistRefresh_SymbolCannotBePersistedAndMissingInSameSnapshot()
    {
        using var dbContext = CreateInMemoryContext();
        var service = new MarketDataSnapshotPersistenceService(dbContext);

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.PersistRefresh(
                new PersistMarketDataRefreshRequest(
                    new DateOnly(2026, 3, 8),
                    new DateTimeOffset(2026, 3, 8, 19, 30, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
                    "scheduled",
                    "yahoo-finance",
                    900,
                    ["AAPL"],
                    [CreateSymbol("AAPL", new DateOnly(2026, 3, 7), 100m, 101m)],
                    [new PersistedMarketDataMissingSymbol("AAPL", MarketDataMissingSymbolReason.ProviderError, null, true)])));

        Assert.Contains("both persisted and missing", exception.Message, StringComparison.Ordinal);
    }

    private static AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private static PersistedMarketDataSymbol CreateSymbol(
        string symbol,
        DateOnly firstDate,
        decimal firstClose,
        decimal secondClose)
    {
        return new PersistedMarketDataSymbol(
            symbol,
            [
                new PersistedMarketDataQuote(
                    firstDate,
                    new DateTimeOffset(firstDate.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc)),
                    firstClose - 1m,
                    firstClose + 1m,
                    firstClose - 2m,
                    firstClose,
                    1000),
                new PersistedMarketDataQuote(
                    firstDate.AddDays(1),
                    new DateTimeOffset(firstDate.AddDays(1).ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc)),
                    secondClose - 1m,
                    secondClose + 1m,
                    secondClose - 2m,
                    secondClose,
                    1200)
            ]);
    }
}
