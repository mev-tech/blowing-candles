using BlowingCandles.Infrastructure.Persistence.Entities;

namespace BlowingCandles.Infrastructure.Persistence.Models;

public sealed record PersistMarketDataRefreshRequest(
    DateOnly RequestedAsOfDate,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CapturedAtUtc,
    string Trigger,
    string Provider,
    int FreshnessTtlSeconds,
    IReadOnlyList<string> RequestedSymbols,
    IReadOnlyList<PersistedMarketDataSymbol> PersistedSymbols,
    IReadOnlyList<PersistedMarketDataMissingSymbol> MissingSymbols,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record PersistedMarketDataSymbol(
    string Symbol,
    IReadOnlyList<PersistedMarketDataQuote> Quotes);

public sealed record PersistedMarketDataQuote(
    DateOnly QuoteDate,
    DateTimeOffset MarketTimestampUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

public sealed record PersistedMarketDataMissingSymbol(
    string Symbol,
    MarketDataMissingSymbolReason Reason,
    string? Detail,
    bool IsRetryable);

public sealed record MarketDataRefreshPersistenceResult(
    long RefreshRunId,
    long? SnapshotId,
    MarketDataRefreshRunStatus Status);
