using System.Data.Common;
using BlowingCandles.Infrastructure.Persistence;
using BlowingCandles.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlowingCandles.Infrastructure.Tests;

[Collection(PostgresContainerCollection.Name)]
public sealed class AppDbContextModelTests
{
    private readonly PostgresContainerFixture _fixture;

    public AppDbContextModelTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Fixture_StartsContainerAndAppliesMigration()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();

        var tableNames = await QueryStringSetAsync(
            dbContext,
            """
            SELECT tablename
            FROM pg_tables
            WHERE schemaname = 'public'
            """);

        Assert.Contains("__EFMigrationsHistory", tableNames);
        Assert.Contains("market_data_refresh_run", tableNames);
        Assert.Contains("market_data_snapshot", tableNames);
        Assert.Contains("market_data_snapshot_quote", tableNames);
        Assert.Contains("market_data_snapshot_missing_symbol", tableNames);

        var migrationHistoryCount = await ExecuteScalarAsync(
            dbContext,
            """
            SELECT COUNT(*)
            FROM "__EFMigrationsHistory"
            """);

        Assert.Equal(1, migrationHistoryCount);
    }

    [Fact]
    public async Task Model_UsesRequiredSnakeCaseTablesAndConstraints()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();

        var tableNames = await QueryStringSetAsync(
            dbContext,
            """
            SELECT tablename
            FROM pg_tables
            WHERE schemaname = 'public'
            AND tablename IN (
                'market_data_refresh_run',
                'market_data_snapshot',
                'market_data_snapshot_quote',
                'market_data_snapshot_missing_symbol')
            """);
        var constraintNames = await QueryStringSetAsync(
            dbContext,
            """
            SELECT conname
            FROM pg_constraint
            WHERE conrelid IN (
                'market_data_refresh_run'::regclass,
                'market_data_snapshot'::regclass,
                'market_data_snapshot_quote'::regclass)
            """);

        Assert.Contains("market_data_refresh_run", tableNames);
        Assert.Contains("market_data_snapshot", tableNames);
        Assert.Contains("market_data_snapshot_quote", tableNames);
        Assert.Contains("market_data_snapshot_missing_symbol", tableNames);
        Assert.Contains(
            "ck_market_data_refresh_run_requested_symbol_count_non_negative",
            constraintNames);
        Assert.Contains(
            "ck_market_data_snapshot_freshness_ttl_seconds_positive",
            constraintNames);
        Assert.Contains(
            "ck_market_data_snapshot_quote_volume_non_negative",
            constraintNames);
    }

    [Fact]
    public async Task Model_EnforcesRequiredUniqueIndexes()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();

        var uniqueIndexNames = await QueryStringSetAsync(
            dbContext,
            """
            SELECT index_class.relname
            FROM pg_index index_info
            JOIN pg_class index_class ON index_class.oid = index_info.indexrelid
            WHERE index_info.indisunique
            AND index_info.indrelid IN (
                'market_data_snapshot'::regclass,
                'market_data_snapshot_quote'::regclass,
                'market_data_snapshot_missing_symbol'::regclass)
            """);

        Assert.Contains("ix_market_data_snapshot_refresh_run_id", uniqueIndexNames);
        Assert.Contains(
            "ix_market_data_snapshot_quote_snapshot_id_symbol_quote_date",
            uniqueIndexNames);
        Assert.Contains(
            "ix_market_data_snapshot_missing_symbol_snapshot_id_symbol",
            uniqueIndexNames);
    }

    [Fact]
    public async Task Database_RejectsInvalidFreshnessTtlViaCheckConstraint()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        var capturedAtUtc = new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero);
        var run = CreateRefreshRun(
            new DateOnly(2026, 3, 8),
            capturedAtUtc,
            MarketDataRefreshRunStatus.Succeeded,
            requestedSymbolCount: 1,
            persistedSymbolCount: 1,
            missingSymbolCount: 0);
        var snapshot = CreateSnapshot(
            run,
            new DateOnly(2026, 3, 8),
            capturedAtUtc,
            freshnessTtlSeconds: 0,
            MarketDataSnapshotStatus.Complete,
            quoteRowCount: 0,
            coveredSymbolCount: 0,
            missingSymbolCount: 0);

        dbContext.MarketDataSnapshots.Add(snapshot);

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_RejectsDuplicateQuoteRowsViaUniqueIndex()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        var capturedAtUtc = new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero);
        var run = CreateRefreshRun(
            new DateOnly(2026, 3, 8),
            capturedAtUtc,
            MarketDataRefreshRunStatus.Succeeded,
            requestedSymbolCount: 1,
            persistedSymbolCount: 1,
            missingSymbolCount: 0);
        var snapshot = CreateSnapshot(
            run,
            new DateOnly(2026, 3, 8),
            capturedAtUtc,
            freshnessTtlSeconds: 3600,
            MarketDataSnapshotStatus.Complete,
            quoteRowCount: 2,
            coveredSymbolCount: 1,
            missingSymbolCount: 0);

        snapshot.Quotes.Add(CreateQuote(snapshot, "AAPL", new DateOnly(2026, 3, 8), 101m));
        snapshot.Quotes.Add(CreateQuote(snapshot, "AAPL", new DateOnly(2026, 3, 8), 102m));
        dbContext.MarketDataSnapshots.Add(snapshot);

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task DeletingSnapshot_CascadesToQuoteAndMissingSymbolRows()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        var asOfDate = new DateOnly(2026, 3, 8);
        var capturedAtUtc = new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero);
        var run = CreateRefreshRun(
            asOfDate,
            capturedAtUtc,
            MarketDataRefreshRunStatus.Partial,
            requestedSymbolCount: 2,
            persistedSymbolCount: 1,
            missingSymbolCount: 1);
        var snapshot = CreateSnapshot(
            run,
            asOfDate,
            capturedAtUtc,
            freshnessTtlSeconds: 3600,
            MarketDataSnapshotStatus.Partial,
            quoteRowCount: 1,
            coveredSymbolCount: 1,
            missingSymbolCount: 1);

        snapshot.Quotes.Add(CreateQuote(snapshot, "AAPL", asOfDate, 101m));
        snapshot.MissingSymbols.Add(CreateMissingSymbol(snapshot, "MSFT"));

        dbContext.MarketDataSnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync();

        var runId = run.Id;
        var snapshotId = snapshot.Id;

        dbContext.MarketDataSnapshots.Remove(snapshot);
        await dbContext.SaveChangesAsync();

        await using var verificationContext = _fixture.CreateDbContext();

        Assert.True(await verificationContext.MarketDataRefreshRuns.AnyAsync(x => x.Id == runId));
        Assert.False(await verificationContext.MarketDataSnapshots.AnyAsync(x => x.Id == snapshotId));
        Assert.False(await verificationContext.MarketDataSnapshotQuotes.AnyAsync(x => x.SnapshotId == snapshotId));
        Assert.False(await verificationContext.MarketDataSnapshotMissingSymbols.AnyAsync(x => x.SnapshotId == snapshotId));
    }

    private static async Task<HashSet<string>> QueryStringSetAsync(AppDbContext dbContext, string sql)
    {
        await using var command = CreateCommand(dbContext, sql);
        var values = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task<int> ExecuteScalarAsync(AppDbContext dbContext, string sql)
    {
        await using var command = CreateCommand(dbContext, sql);
        var result = await command.ExecuteScalarAsync();

        return Convert.ToInt32(result);
    }

    private static DbCommand CreateCommand(AppDbContext dbContext, string sql)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static MarketDataRefreshRunEntity CreateRefreshRun(
        DateOnly asOfDate,
        DateTimeOffset capturedAtUtc,
        MarketDataRefreshRunStatus status,
        int requestedSymbolCount,
        int persistedSymbolCount,
        int missingSymbolCount)
    {
        return new MarketDataRefreshRunEntity
        {
            RequestedAsOfDate = asOfDate,
            StartedAtUtc = capturedAtUtc.AddMinutes(-30),
            CompletedAtUtc = capturedAtUtc,
            Trigger = "scheduled",
            Provider = "yahoo-finance",
            Status = status,
            RequestedSymbolCount = requestedSymbolCount,
            PersistedSymbolCount = persistedSymbolCount,
            MissingSymbolCount = missingSymbolCount
        };
    }

    private static MarketDataSnapshotEntity CreateSnapshot(
        MarketDataRefreshRunEntity run,
        DateOnly asOfDate,
        DateTimeOffset capturedAtUtc,
        int freshnessTtlSeconds,
        MarketDataSnapshotStatus status,
        int quoteRowCount,
        int coveredSymbolCount,
        int missingSymbolCount)
    {
        var snapshot = new MarketDataSnapshotEntity
        {
            RefreshRun = run,
            AsOfDate = asOfDate,
            CapturedAtUtc = capturedAtUtc,
            FreshnessTtlSeconds = freshnessTtlSeconds,
            FreshUntilUtc = capturedAtUtc.AddSeconds(freshnessTtlSeconds),
            Status = status,
            FirstQuoteDate = asOfDate,
            LastQuoteDate = asOfDate,
            QuoteRowCount = quoteRowCount,
            CoveredSymbolCount = coveredSymbolCount,
            MissingSymbolCount = missingSymbolCount
        };

        run.Snapshot = snapshot;
        return snapshot;
    }

    private static MarketDataSnapshotQuoteEntity CreateQuote(
        MarketDataSnapshotEntity snapshot,
        string symbol,
        DateOnly quoteDate,
        decimal close)
    {
        return new MarketDataSnapshotQuoteEntity
        {
            Snapshot = snapshot,
            Symbol = symbol,
            QuoteDate = quoteDate,
            MarketTimestampUtc =
                new DateTimeOffset(quoteDate.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc)),
            Open = close - 1m,
            High = close + 1m,
            Low = close - 2m,
            Close = close,
            Volume = 1000
        };
    }

    private static MarketDataSnapshotMissingSymbolEntity CreateMissingSymbol(
        MarketDataSnapshotEntity snapshot,
        string symbol)
    {
        return new MarketDataSnapshotMissingSymbolEntity
        {
            Snapshot = snapshot,
            Symbol = symbol,
            Reason = MarketDataMissingSymbolReason.EmptySeries,
            Detail = "no rows",
            IsRetryable = true
        };
    }
}
