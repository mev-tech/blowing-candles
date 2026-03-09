using BlowingCandles.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlowingCandles.Infrastructure.Persistence.Configurations;

public sealed class SignalRunResultConfiguration : IEntityTypeConfiguration<SignalRunResultEntity>
{
    public void Configure(EntityTypeBuilder<SignalRunResultEntity> builder)
    {
        builder.ToTable("signal_run_result");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.RunId).HasColumnName("run_id");
        builder.Property(x => x.Ticker)
            .HasColumnName("ticker")
            .HasMaxLength(SignalRunPersistenceLimits.TickerMaxLength);
        builder.Property(x => x.Action)
            .HasColumnName("action")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(x => x.NewsState)
            .HasColumnName("news_state")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(x => x.MarketAction)
            .HasColumnName("market_action")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(x => x.Reason)
            .HasColumnName("reason")
            .HasMaxLength(SignalRunPersistenceLimits.ReasonMaxLength);
        builder.Property(x => x.Timestamp).HasColumnName("timestamp");

        builder.HasOne(x => x.Run)
            .WithMany(x => x.Results)
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.RunId)
            .HasDatabaseName("ix_signal_run_result_run_id");
        builder.HasIndex(x => new { x.RunId, x.Ticker })
            .IsUnique()
            .HasDatabaseName("ix_signal_run_result_run_id_ticker");
    }
}
