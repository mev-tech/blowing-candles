namespace BlowingCandles.Infrastructure.Persistence.Entities;

public sealed class MarketDataSnapshotMissingSymbolEntity
{
    public long Id { get; set; }

    public long SnapshotId { get; set; }

    public string Symbol { get; set; } = null!;

    public MarketDataMissingSymbolReason Reason { get; set; }

    public string? Detail { get; set; }

    public bool IsRetryable { get; set; }

    public MarketDataSnapshotEntity Snapshot { get; set; } = null!;
}
