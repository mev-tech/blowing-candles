namespace BlowingCandles.Infrastructure.Persistence.Entities;

public sealed class MarketDataSnapshotQuoteEntity
{
    public long Id { get; set; }

    public long SnapshotId { get; set; }

    public string Symbol { get; set; } = null!;

    public DateOnly QuoteDate { get; set; }

    public DateTimeOffset MarketTimestampUtc { get; set; }

    public decimal Open { get; set; }

    public decimal High { get; set; }

    public decimal Low { get; set; }

    public decimal Close { get; set; }

    public long Volume { get; set; }

    public MarketDataSnapshotEntity Snapshot { get; set; } = null!;
}
