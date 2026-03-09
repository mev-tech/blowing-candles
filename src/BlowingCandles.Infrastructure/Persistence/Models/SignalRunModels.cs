using BlowingCandles.Domain.Enums;
using BlowingCandles.Domain.Models;
using BlowingCandles.Infrastructure.Persistence.Entities;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Infrastructure.Persistence.Models;

public sealed record PersistSignalRunRequest(
    SignalRunType RunType,
    string Trigger,
    DateOnly AsOfDate,
    bool IsSimulation,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<FinalSignal> Signals,
    TradeGovernorState? GovernorState = null,
    string? GovernorStateMode = null,
    string? ErrorMessage = null);

public sealed record SignalRunPersistenceResult(
    long RunId,
    SignalRunStatus Status);

public sealed record SignalRunReadResult(
    long RunId,
    SignalRunType RunType,
    string Trigger,
    SignalRunStatus Status,
    DateOnly AsOfDate,
    bool IsSimulation,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TickerCount,
    IReadOnlyList<SignalRunSignalResult> Signals,
    string? ErrorMessage);

public sealed record SignalRunSignalResult(
    string Ticker,
    TradingAction Action,
    NewsState NewsState,
    TradingAction MarketAction,
    string Reason,
    DateTimeOffset Timestamp);
