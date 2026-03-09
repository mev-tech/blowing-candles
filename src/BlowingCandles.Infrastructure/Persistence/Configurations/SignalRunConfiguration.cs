using BlowingCandles.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlowingCandles.Infrastructure.Persistence.Configurations;

public sealed class SignalRunConfiguration : IEntityTypeConfiguration<SignalRunEntity>
{
    public void Configure(EntityTypeBuilder<SignalRunEntity> builder)
    {
        builder.ToTable(
            "signal_run",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_signal_run_ticker_count_non_negative",
                    "\"ticker_count\" >= 0");
            });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.RunType)
            .HasColumnName("run_type")
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(x => x.Trigger)
            .HasColumnName("trigger")
            .HasMaxLength(SignalRunPersistenceLimits.TriggerMaxLength);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(x => x.AsOfDate).HasColumnName("as_of_date");
        builder.Property(x => x.IsSimulation).HasColumnName("is_simulation");
        builder.Property(x => x.TickerCount).HasColumnName("ticker_count");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(x => x.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(SignalRunPersistenceLimits.ErrorMessageMaxLength);

        builder.HasIndex(x => new { x.Status, x.StartedAtUtc })
            .HasDatabaseName("ix_signal_run_status_started_at_utc");
        builder.HasIndex(x => new { x.RunType, x.AsOfDate })
            .HasDatabaseName("ix_signal_run_run_type_as_of_date");
        builder.HasIndex(x => new { x.IsSimulation, x.CompletedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("ix_signal_run_is_simulation_completed_at_utc");
    }
}
