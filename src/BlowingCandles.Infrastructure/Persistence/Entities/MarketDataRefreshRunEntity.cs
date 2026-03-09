namespace BlowingCandles.Infrastructure.Persistence.Entities;

public sealed class MarketDataRefreshRunEntity
{
    public long Id { get; set; }

    public DateOnly RequestedAsOfDate { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string Trigger { get; set; } = null!;

    public string Provider { get; set; } = null!;

    public MarketDataRefreshRunStatus Status { get; set; }

    public int RequestedSymbolCount { get; set; }

    public int PersistedSymbolCount { get; set; }

    public int MissingSymbolCount { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public MarketDataSnapshotEntity? Snapshot { get; set; }
}
