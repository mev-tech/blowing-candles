using BlowingCandles.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlowingCandles.Infrastructure.Persistence.Configurations;

public sealed class TradeGovernorStateConfiguration : IEntityTypeConfiguration<TradeGovernorStateEntity>
{
    public void Configure(EntityTypeBuilder<TradeGovernorStateEntity> builder)
    {
        builder.ToTable(
            "trade_governor_state",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_trade_governor_state_buys_today_non_negative",
                    "\"buys_today\" >= 0");
            });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Mode)
            .HasColumnName("mode")
            .HasMaxLength(SignalRunPersistenceLimits.ModeMaxLength);
        builder.Property(x => x.Day)
            .HasColumnName("day")
            .HasMaxLength(SignalRunPersistenceLimits.DayMaxLength);
        builder.Property(x => x.BuysToday).HasColumnName("buys_today");
        builder.Property(x => x.LastBuyAt).HasColumnName("last_buy_at");

        builder.HasIndex(x => x.Mode)
            .IsUnique()
            .HasDatabaseName("ix_trade_governor_state_mode");
    }
}
