namespace BlowingCandles.Infrastructure.Persistence.Entities;

public sealed class MarketDataSnapshotEntity
{
    public long Id { get; set; }

    public long RefreshRunId { get; set; }

    public DateOnly AsOfDate { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; }

    public int FreshnessTtlSeconds { get; set; }

    public DateTimeOffset FreshUntilUtc { get; set; }

    public MarketDataSnapshotStatus Status { get; set; }

    public DateOnly FirstQuoteDate { get; set; }

    public DateOnly LastQuoteDate { get; set; }

    public int QuoteRowCount { get; set; }

    public int CoveredSymbolCount { get; set; }

    public int MissingSymbolCount { get; set; }

    public MarketDataRefreshRunEntity RefreshRun { get; set; } = null!;

    public List<MarketDataSnapshotQuoteEntity> Quotes { get; } = [];

    public List<MarketDataSnapshotMissingSymbolEntity> MissingSymbols { get; } = [];
}
