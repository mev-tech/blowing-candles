using BlowingCandles.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlowingCandles.Infrastructure.Persistence.Configurations;

public sealed class MarketDataSnapshotQuoteConfiguration : IEntityTypeConfiguration<MarketDataSnapshotQuoteEntity>
{
    public void Configure(EntityTypeBuilder<MarketDataSnapshotQuoteEntity> builder)
    {
        builder.ToTable(
            "market_data_snapshot_quote",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_market_data_snapshot_quote_volume_non_negative",
                    "\"volume\" >= 0");
            });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.SnapshotId).HasColumnName("snapshot_id");
        builder.Property(x => x.Symbol)
            .HasColumnName("symbol")
            .HasMaxLength(MarketDataPersistenceLimits.SymbolMaxLength);
        builder.Property(x => x.QuoteDate).HasColumnName("quote_date");
        builder.Property(x => x.MarketTimestampUtc).HasColumnName("market_timestamp_utc");
        builder.Property(x => x.Open).HasColumnName("open").HasPrecision(18, 8);
        builder.Property(x => x.High).HasColumnName("high").HasPrecision(18, 8);
        builder.Property(x => x.Low).HasColumnName("low").HasPrecision(18, 8);
        builder.Property(x => x.Close).HasColumnName("close").HasPrecision(18, 8);
        builder.Property(x => x.Volume).HasColumnName("volume");

        builder.HasOne(x => x.Snapshot)
            .WithMany(x => x.Quotes)
            .HasForeignKey(x => x.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.SnapshotId, x.Symbol, x.QuoteDate })
            .IsUnique()
            .HasDatabaseName("ix_market_data_snapshot_quote_snapshot_id_symbol_quote_date");
        builder.HasIndex(x => new { x.Symbol, x.QuoteDate })
            .HasDatabaseName("ix_market_data_snapshot_quote_symbol_quote_date");
    }
}
