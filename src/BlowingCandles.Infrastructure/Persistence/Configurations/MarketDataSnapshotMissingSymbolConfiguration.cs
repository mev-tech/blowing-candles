using BlowingCandles.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlowingCandles.Infrastructure.Persistence.Configurations;

public sealed class MarketDataSnapshotMissingSymbolConfiguration
    : IEntityTypeConfiguration<MarketDataSnapshotMissingSymbolEntity>
{
    public void Configure(EntityTypeBuilder<MarketDataSnapshotMissingSymbolEntity> builder)
    {
        builder.ToTable("market_data_snapshot_missing_symbol");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.SnapshotId).HasColumnName("snapshot_id");
        builder.Property(x => x.Symbol)
            .HasColumnName("symbol")
            .HasMaxLength(MarketDataPersistenceLimits.SymbolMaxLength);
        builder.Property(x => x.Reason)
            .HasColumnName("reason")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(x => x.Detail)
            .HasColumnName("detail")
            .HasMaxLength(MarketDataPersistenceLimits.DetailMaxLength);
        builder.Property(x => x.IsRetryable).HasColumnName("is_retryable");

        builder.HasOne(x => x.Snapshot)
            .WithMany(x => x.MissingSymbols)
            .HasForeignKey(x => x.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.SnapshotId, x.Symbol })
            .IsUnique()
            .HasDatabaseName("ix_market_data_snapshot_missing_symbol_snapshot_id_symbol");
    }
}
