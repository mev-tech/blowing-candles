using BlowingCandles.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlowingCandles.Infrastructure.Persistence.Configurations;

public sealed class MarketDataRefreshRunConfiguration : IEntityTypeConfiguration<MarketDataRefreshRunEntity>
{
    public void Configure(EntityTypeBuilder<MarketDataRefreshRunEntity> builder)
    {
        builder.ToTable(
            "market_data_refresh_run",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_market_data_refresh_run_requested_symbol_count_non_negative",
                    "\"requested_symbol_count\" >= 0");
                tableBuilder.HasCheckConstraint(
                    "ck_market_data_refresh_run_persisted_symbol_count_non_negative",
                    "\"persisted_symbol_count\" >= 0");
                tableBuilder.HasCheckConstraint(
                    "ck_market_data_refresh_run_missing_symbol_count_non_negative",
                    "\"missing_symbol_count\" >= 0");
            });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.RequestedAsOfDate).HasColumnName("requested_as_of_date");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(x => x.Trigger)
            .HasColumnName("trigger")
            .HasMaxLength(MarketDataPersistenceLimits.TriggerMaxLength);
        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasMaxLength(MarketDataPersistenceLimits.ProviderMaxLength);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(x => x.RequestedSymbolCount).HasColumnName("requested_symbol_count");
        builder.Property(x => x.PersistedSymbolCount).HasColumnName("persisted_symbol_count");
        builder.Property(x => x.MissingSymbolCount).HasColumnName("missing_symbol_count");
        builder.Property(x => x.ErrorCode)
            .HasColumnName("error_code")
            .HasMaxLength(MarketDataPersistenceLimits.ErrorCodeMaxLength);
        builder.Property(x => x.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(MarketDataPersistenceLimits.ErrorMessageMaxLength);

        builder.HasOne(x => x.Snapshot)
            .WithOne(x => x.RefreshRun)
            .HasForeignKey<MarketDataSnapshotEntity>(x => x.RefreshRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.Status, x.StartedAtUtc })
            .HasDatabaseName("ix_market_data_refresh_run_status_started_at_utc");
        builder.HasIndex(x => new { x.RequestedAsOfDate, x.StartedAtUtc })
            .HasDatabaseName("ix_market_data_refresh_run_requested_as_of_date_started_at_utc");
    }
}
