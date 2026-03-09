using BlowingCandles.Infrastructure.Persistence;
using BlowingCandles.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BlowingCandles.Infrastructure.Tests;

public sealed class AppDbContextModelTests
{
    [Fact]
    public void Model_UsesRequiredSnakeCaseTablesAndConstraints()
    {
        using var dbContext = CreateRelationalContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;

        var refreshRun = model.FindEntityType(typeof(MarketDataRefreshRunEntity));
        var snapshot = model.FindEntityType(typeof(MarketDataSnapshotEntity));
        var quote = model.FindEntityType(typeof(MarketDataSnapshotQuoteEntity));
        var missingSymbol = model.FindEntityType(typeof(MarketDataSnapshotMissingSymbolEntity));

        Assert.NotNull(refreshRun);
        Assert.NotNull(snapshot);
        Assert.NotNull(quote);
        Assert.NotNull(missingSymbol);

        Assert.Equal("market_data_refresh_run", refreshRun!.GetTableName());
        Assert.Equal("market_data_snapshot", snapshot!.GetTableName());
        Assert.Equal("market_data_snapshot_quote", quote!.GetTableName());
        Assert.Equal("market_data_snapshot_missing_symbol", missingSymbol!.GetTableName());

        Assert.Contains(
            refreshRun.GetCheckConstraints(),
            constraint => constraint.Name == "ck_market_data_refresh_run_requested_symbol_count_non_negative");
        Assert.Contains(
            snapshot.GetCheckConstraints(),
            constraint => constraint.Name == "ck_market_data_snapshot_freshness_ttl_seconds_positive");
        Assert.Contains(
            quote.GetCheckConstraints(),
            constraint => constraint.Name == "ck_market_data_snapshot_quote_volume_non_negative");
    }

    [Fact]
    public void Model_EnforcesRequiredUniqueIndexes()
    {
        using var dbContext = CreateRelationalContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;

        var snapshot = model.FindEntityType(typeof(MarketDataSnapshotEntity));
        var quote = model.FindEntityType(typeof(MarketDataSnapshotQuoteEntity));
        var missingSymbol = model.FindEntityType(typeof(MarketDataSnapshotMissingSymbolEntity));

        Assert.NotNull(snapshot);
        Assert.NotNull(quote);
        Assert.NotNull(missingSymbol);

        Assert.Contains(
            snapshot!.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(["RefreshRunId"]));
        Assert.Contains(
            quote!.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(["SnapshotId", "Symbol", "QuoteDate"]));
        Assert.Contains(
            missingSymbol!.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(["SnapshotId", "Symbol"]));
    }

    private static AppDbContext CreateRelationalContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=metadata;Username=postgres;Password=postgres")
            .Options;

        return new AppDbContext(options);
    }
}
