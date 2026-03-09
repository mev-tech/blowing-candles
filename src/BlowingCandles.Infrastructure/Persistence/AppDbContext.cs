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

    public DbSet<SignalRunEntity> SignalRuns => Set<SignalRunEntity>();

    public DbSet<SignalRunResultEntity> SignalRunResults => Set<SignalRunResultEntity>();

    public DbSet<TradeGovernorStateEntity> TradeGovernorStates => Set<TradeGovernorStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
