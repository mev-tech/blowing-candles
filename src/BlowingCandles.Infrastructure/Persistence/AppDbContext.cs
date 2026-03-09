using BlowingCandles.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlowingCandles.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<MarketDataRefreshRunEntity> MarketDataRefreshRuns => Set<MarketDataRefreshRunEntity>();

    public DbSet<MarketDataSnapshotEntity> MarketDataSnapshots => Set<MarketDataSnapshotEntity>();

    public DbSet<MarketDataSnapshotQuoteEntity> MarketDataSnapshotQuotes => Set<MarketDataSnapshotQuoteEntity>();

    public DbSet<MarketDataSnapshotMissingSymbolEntity> MarketDataSnapshotMissingSymbols =>
        Set<MarketDataSnapshotMissingSymbolEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
