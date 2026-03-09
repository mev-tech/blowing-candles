using BlowingCandles.Infrastructure.Persistence.Entities;
using BlowingCandles.Infrastructure.Persistence.Models;

namespace BlowingCandles.Application.Services;

public interface ISignalRunExecutionService
{
    SignalRunExecutionResult RunRealtime(string trigger);

    SignalRunExecutionResult RunAsOf(string trigger, DateOnly asOfDate);

    IReadOnlyList<SignalRunExecutionResult> RunRange(string trigger, DateOnly startDate, DateOnly endDate);
}

public sealed record SignalRunExecutionResult(
    long RunId,
    SignalRunType RunType,
    string Trigger,
    SignalRunStatus Status,
    DateOnly AsOfDate,
    bool IsSimulation,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int TickerCount,
    IReadOnlyList<SignalRunSignalResult> Signals,
    string? ErrorMessage);
