using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Persistence;
using BlowingCandles.Infrastructure.Persistence.Entities;
using BlowingCandles.Infrastructure.Persistence.Providers;
using BlowingCandles.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace BlowingCandles.Infrastructure.Tests;

[Collection(PostgresContainerCollection.Name)]
public sealed class MarketDataSnapshotReadServiceTests
{
    private readonly PostgresContainerFixture _fixture;

    public MarketDataSnapshotReadServiceTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetLivePriceHistory_ExpiredSnapshot_ReturnsEmpty()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        await SeedSnapshotAsync(
            dbContext,
            asOfDate: new DateOnly(2026, 3, 8),
            capturedAtUtc: new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
            freshUntilUtc: new DateTimeOffset(2026, 3, 8, 20, 5, 0, TimeSpan.Zero),
            status: MarketDataSnapshotStatus.Complete,
            symbol: "AAPL",
            close: 101m);
        var service = new MarketDataSnapshotReadService(dbContext);

        var bars = service.GetLivePriceHistory(
            "AAPL",
            new DateOnly(2026, 3, 8),
            new DateTimeOffset(2026, 3, 8, 20, 5, 0, TimeSpan.Zero));

        Assert.Empty(bars);
    }

    [Fact]
    public async Task GetHistoricalPriceHistory_ExpiredSnapshotStillSelectableByAsOfDate()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        await SeedSnapshotAsync(
            dbContext,
            asOfDate: new DateOnly(2026, 3, 7),
            capturedAtUtc: new DateTimeOffset(2026, 3, 7, 20, 0, 0, TimeSpan.Zero),
            freshUntilUtc: new DateTimeOffset(2026, 3, 7, 21, 0, 0, TimeSpan.Zero),
            status: MarketDataSnapshotStatus.Complete,
            symbol: "AAPL",
            close: 101m);
        await SeedSnapshotAsync(
            dbContext,
            asOfDate: new DateOnly(2026, 3, 9),
            capturedAtUtc: new DateTimeOffset(2026, 3, 9, 20, 0, 0, TimeSpan.Zero),
            freshUntilUtc: new DateTimeOffset(2026, 3, 9, 21, 0, 0, TimeSpan.Zero),
            status: MarketDataSnapshotStatus.Complete,
            symbol: "AAPL",
            close: 109m);
        var service = new MarketDataSnapshotReadService(dbContext);

        var bars = service.GetHistoricalPriceHistory("AAPL", new DateOnly(2026, 3, 8));

        var bar = Assert.Single(bars);
        Assert.Equal(101m, bar.Close);
    }

    [Fact]
    public async Task GetHistoricalPriceHistory_MissingSymbol_ReturnsEmpty()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        var snapshotId = await SeedSnapshotAsync(
            dbContext,
            asOfDate: new DateOnly(2026, 3, 8),
            capturedAtUtc: new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
            freshUntilUtc: new DateTimeOffset(2026, 3, 8, 21, 0, 0, TimeSpan.Zero),
            status: MarketDataSnapshotStatus.Partial,
            symbol: "AAPL",
            close: 101m);
        dbContext.MarketDataSnapshotMissingSymbols.Add(
            new MarketDataSnapshotMissingSymbolEntity
            {
                SnapshotId = snapshotId,
                Symbol = "MSFT",
                Reason = MarketDataMissingSymbolReason.EmptySeries,
                Detail = "no rows",
                IsRetryable = true
            });
        await dbContext.SaveChangesAsync();
        var service = new MarketDataSnapshotReadService(dbContext);

        var bars = service.GetHistoricalPriceHistory("MSFT", new DateOnly(2026, 3, 8));

        Assert.Empty(bars);
    }

    [Fact]
    public async Task LiveSnapshotProvider_ReadsFromFreshSnapshotAndDelegatesEarningsLookup()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        await SeedSnapshotAsync(
            dbContext,
            asOfDate: new DateOnly(2026, 3, 8),
            capturedAtUtc: new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
            freshUntilUtc: new DateTimeOffset(2026, 3, 8, 22, 0, 0, TimeSpan.Zero),
            status: MarketDataSnapshotStatus.Complete,
            symbol: "AAPL",
            close: 101m);
        var readService = new MarketDataSnapshotReadService(dbContext);
        var expectedEarningsDate = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var fallbackProvider = new StubMarketDataProvider(expectedEarningsDate);
        var provider = new LiveSnapshotMarketDataProvider(
            readService,
            fallbackProvider,
            new FixedClock(new DateTimeOffset(2026, 3, 8, 21, 0, 0, TimeSpan.Zero)));

        var bars = provider.GetDailyPriceHistory("AAPL", new DateOnly(2026, 3, 8));
        var earningsDate = provider.GetNextEarningsDate("AAPL", new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.Zero));

        var bar = Assert.Single(bars);
        Assert.Equal(101m, bar.Close);
        Assert.Equal(expectedEarningsDate, earningsDate);
    }

    [Fact]
    public async Task HistoricalSnapshotProvider_IgnoresExpiredTtlForAsOfReads()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        await SeedSnapshotAsync(
            dbContext,
            asOfDate: new DateOnly(2026, 3, 6),
            capturedAtUtc: new DateTimeOffset(2026, 3, 6, 20, 0, 0, TimeSpan.Zero),
            freshUntilUtc: new DateTimeOffset(2026, 3, 6, 21, 0, 0, TimeSpan.Zero),
            status: MarketDataSnapshotStatus.Complete,
            symbol: "AAPL",
            close: 99m);
        var readService = new MarketDataSnapshotReadService(dbContext);
        var provider = new HistoricalSnapshotMarketDataProvider(
            readService,
            new StubMarketDataProvider(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)));

        var bars = provider.GetDailyPriceHistory("AAPL", new DateOnly(2026, 3, 8));

        var bar = Assert.Single(bars);
        Assert.Equal(99m, bar.Close);
    }

    private static async Task<long> SeedSnapshotAsync(
        AppDbContext dbContext,
        DateOnly asOfDate,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset freshUntilUtc,
        MarketDataSnapshotStatus status,
        string symbol,
        decimal close)
    {
        var run = new MarketDataRefreshRunEntity
        {
            RequestedAsOfDate = asOfDate,
            StartedAtUtc = capturedAtUtc.AddMinutes(-30),
            CompletedAtUtc = capturedAtUtc,
            Trigger = "scheduled",
            Provider = "yahoo-finance",
            Status = status == MarketDataSnapshotStatus.Complete
                ? MarketDataRefreshRunStatus.Succeeded
                : MarketDataRefreshRunStatus.Partial,
            RequestedSymbolCount = 1,
            PersistedSymbolCount = 1,
            MissingSymbolCount = status == MarketDataSnapshotStatus.Partial ? 1 : 0
        };
        var snapshot = new MarketDataSnapshotEntity
        {
            RefreshRun = run,
            AsOfDate = asOfDate,
            CapturedAtUtc = capturedAtUtc,
            FreshnessTtlSeconds = (int)Math.Max(1, (freshUntilUtc - capturedAtUtc).TotalSeconds),
            FreshUntilUtc = freshUntilUtc,
            Status = status,
            FirstQuoteDate = asOfDate.AddDays(-1),
            LastQuoteDate = asOfDate,
            QuoteRowCount = 1,
            CoveredSymbolCount = 1,
            MissingSymbolCount = status == MarketDataSnapshotStatus.Partial ? 1 : 0
        };
        var quote = new MarketDataSnapshotQuoteEntity
        {
            Snapshot = snapshot,
            Symbol = symbol,
            QuoteDate = asOfDate,
            MarketTimestampUtc = new DateTimeOffset(asOfDate.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc)),
            Open = close - 1m,
            High = close + 1m,
            Low = close - 2m,
            Close = close,
            Volume = 1000
        };

        run.Snapshot = snapshot;
        snapshot.Quotes.Add(quote);

        dbContext.MarketDataSnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync();

        return snapshot.Id;
    }

    private sealed class StubMarketDataProvider : IMarketDataProvider
    {
        private readonly DateTimeOffset _earningsDate;

        public StubMarketDataProvider(DateTimeOffset earningsDate)
        {
            _earningsDate = earningsDate;
        }

        public IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate)
        {
            _ = ticker;
            _ = asOfDate;
            return [];
        }

        public DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc)
        {
            _ = ticker;
            _ = asOfUtc;
            return _earningsDate;
        }
    }
}
