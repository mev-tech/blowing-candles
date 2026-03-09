using BlowingCandles.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlowingCandles.Infrastructure.Persistence.Configurations;

public sealed class MarketDataSnapshotConfiguration : IEntityTypeConfiguration<MarketDataSnapshotEntity>
{
    public void Configure(EntityTypeBuilder<MarketDataSnapshotEntity> builder)
    {
        builder.ToTable(
            "market_data_snapshot",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_market_data_snapshot_freshness_ttl_seconds_positive",
                    "\"freshness_ttl_seconds\" > 0");
                tableBuilder.HasCheckConstraint(
                    "ck_market_data_snapshot_quote_row_count_non_negative",
                    "\"quote_row_count\" >= 0");
                tableBuilder.HasCheckConstraint(
                    "ck_market_data_snapshot_covered_symbol_count_non_negative",
                    "\"covered_symbol_count\" >= 0");
                tableBuilder.HasCheckConstraint(
                    "ck_market_data_snapshot_missing_symbol_count_non_negative",
                    "\"missing_symbol_count\" >= 0");
            });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.RefreshRunId).HasColumnName("refresh_run_id");
        builder.Property(x => x.AsOfDate).HasColumnName("as_of_date");
        builder.Property(x => x.CapturedAtUtc).HasColumnName("captured_at_utc");
        builder.Property(x => x.FreshnessTtlSeconds).HasColumnName("freshness_ttl_seconds");
        builder.Property(x => x.FreshUntilUtc).HasColumnName("fresh_until_utc");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(x => x.FirstQuoteDate).HasColumnName("first_quote_date");
        builder.Property(x => x.LastQuoteDate).HasColumnName("last_quote_date");
        builder.Property(x => x.QuoteRowCount).HasColumnName("quote_row_count");
        builder.Property(x => x.CoveredSymbolCount).HasColumnName("covered_symbol_count");
        builder.Property(x => x.MissingSymbolCount).HasColumnName("missing_symbol_count");

        builder.HasIndex(x => x.RefreshRunId)
            .IsUnique()
            .HasDatabaseName("ix_market_data_snapshot_refresh_run_id");
        builder.HasIndex(x => new { x.AsOfDate, x.CapturedAtUtc })
            .HasDatabaseName("ix_market_data_snapshot_as_of_date_captured_at_utc");
        builder.HasIndex(x => x.FreshUntilUtc)
            .HasDatabaseName("ix_market_data_snapshot_fresh_until_utc");
    }
}
