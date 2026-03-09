# Feature: Signal Run Execution Service

## Feature Name

signal-run-execution-service

## Purpose

Extract signal pipeline orchestration from CLI handlers into a shared `SignalRunExecutionService` in the Application layer. This service becomes the single entry point for running the signal pipeline, regardless of trigger source (API endpoint, background worker, or CLI). It wires domain services, executes the pipeline, persists results to PostgreSQL via `SignalRunPersistenceService`, writes audit entries, and returns a structured result. This is the core of the API-first refactor — once it exists, both the API and the background worker are thin callers.

## Inputs

| Input | Source | Format | Required |
|-------|--------|--------|----------|
| Run type | Caller | `SignalRunType` enum: `Realtime`, `AsOf`, `Range` | Yes |
| Trigger source | Caller | String: `"api"`, `"worker"`, `"cli"` | Yes |
| As-of date | Caller | `DateOnly` (today for realtime, requested date for simulation) | Yes for `AsOf`; derived for `Realtime` |
| Config | `YamlConfigLoader` or DI | `AppConfig` | Yes |
| Clock | Derived from run type | `IClock` (`SystemClock` for realtime, `FixedClock` for simulation) | Yes (constructed internally) |

## Outputs

| Output | Format | Destination |
|--------|--------|-------------|
| `SignalRunExecutionResult` | C# record | Returned to caller |
| `signal_run` + `signal_run_result` rows | PostgreSQL | Via `SignalRunPersistenceService` |
| `trade_governor_state` row | PostgreSQL | Via `SignalRunPersistenceService` (governor state upsert) |
| Audit JSONL entries | JSONL file | Via `JsonlAuditWriter` (retained for now) |
| `signals.txt` + `signals.json` | Files | Via `OutputRenderer` (retained as write-behind for debugging; will be removed in a future step) |

### `SignalRunExecutionResult`

```csharp
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
```

## Configuration

### New files

- `src/BlowingCandles.Application/Services/ISignalRunExecutionService.cs` — interface
- `src/BlowingCandles.Application/Services/SignalRunExecutionService.cs` — implementation

### Modified files

- `src/BlowingCandles.Api/ApiHost.cs` — register `SignalRunExecutionService` in DI (or defer to Step 3)
- `src/BlowingCandles.Cli/Handlers/RunRealtimeHandler.cs` — delegate to `SignalRunExecutionService` instead of `RunCommandSupport`
- `src/BlowingCandles.Cli/Handlers/RunAsOfHandler.cs` — delegate to `SignalRunExecutionService` instead of `RunCommandSupport`
- `src/BlowingCandles.Cli/Handlers/RunRangeHandler.cs` — delegate to `SignalRunExecutionService` instead of `RunCommandSupport`

### Dependencies

- `BlowingCandles.Application` must reference `BlowingCandles.Infrastructure` (for `SignalRunPersistenceService`, `TradeGovernorDbStateStoreFactory`, `JsonlAuditWriter`, `OutputRenderer`, config, clock types)
- Alternatively, define infrastructure interfaces in Application and inject implementations — but this project uses manual wiring without a DI container, so direct references are acceptable

### NuGet dependencies

None new.

## Edge Cases

1. **Pipeline throws.** If `SignalPipeline.Run()` throws, the service catches the exception, persists a `Failed` run with the error message, and returns a result with `Status = Failed`. The pipeline's fail-safe invariant means most errors produce all-WAIT signals rather than throwing, but unhandled exceptions must still be caught.

2. **Persistence failure after successful pipeline.** If `SignalRunPersistenceService.PersistRun()` fails after signals were generated, the service should still attempt to write file output (write-behind) and return a failure result. The caller should not receive an unhandled exception.

3. **Empty watchlist.** A run with zero tickers succeeds: `TickerCount = 0`, zero signals, `Status = Completed`.

4. **Concurrent runs.** Two overlapping runs of the same type (e.g., two realtime runs) must be serialized to prevent governor state corruption. Use `SemaphoreSlim(1, 1)` per isolation mode (live vs simulation). A simulation run does not block a live run.

5. **Run type determines isolation.** `Realtime` → live mode (`is_simulation = false`, governor state mode `"live"`). `AsOf` and `Range` → simulation mode (`is_simulation = true`, governor state mode `"simulation"`).

6. **File output paths.** For realtime: use config paths directly. For simulation: use `RunCommandSupport.BuildSimulationOutputPath()` / `BuildSimulationAuditPath()` / `BuildSimulationStatePath()` logic. These path-building helpers should be reused or moved to the execution service.

7. **Range execution.** `RunRange` calls the execution service once per date in the range. Each date produces its own `signal_run` row. Trade governor state accumulates across dates within the range (same behavior as `RunRangeHandler`).

## Implementation Notes

### 1. Interface

```csharp
public interface ISignalRunExecutionService
{
    SignalRunExecutionResult RunRealtime(string trigger);
    SignalRunExecutionResult RunAsOf(string trigger, DateOnly asOfDate);
    IReadOnlyList<SignalRunExecutionResult> RunRange(string trigger, DateOnly startDate, DateOnly endDate);
}
```

### 2. Implementation sketch

`SignalRunExecutionService` constructor takes:
- `AppConfig config`
- `SignalRunPersistenceService signalRunPersistence`
- `TradeGovernorDbStateStoreFactory governorStateStoreFactory`
- `Func<AppConfig, IEarningsCalendar> earningsCalendarFactory`
- `Func<AppConfig, IMarketDataProvider> marketDataProviderFactory`
- `OutputRenderer outputRenderer`
- `Func<string, JsonlAuditWriter> auditWriterFactory`
- `Action<string>? diagnosticWriter`

Core flow for a single run:
1. Record `startedAtUtc`
2. Construct `IClock` (SystemClock for realtime, FixedClock for asof)
3. Determine governor state mode (`"live"` or `"simulation"`)
4. Construct domain services: `EarningsGate`, `TechnicalScorer`, `TradeGovernor` (using `TradeGovernorDbStateStore` from factory)
5. Create `SignalPipeline` and call `Run(watchlist, clock)`
6. Record `completedAtUtc`
7. Persist run via `SignalRunPersistenceService.PersistRun()`
8. Write file output via `OutputRenderer` (write-behind)
9. Write audit via `JsonlAuditWriter`
10. Return `SignalRunExecutionResult`

On exception at step 5: catch, persist failed run, return failure result.

### 3. Concurrency guard

```csharp
private static readonly SemaphoreSlim LiveSemaphore = new(1, 1);
private static readonly SemaphoreSlim SimulationSemaphore = new(1, 1);
```

Each run acquires the appropriate semaphore. If the semaphore is not immediately available, either wait or throw (TBD — the API step will need 409 Conflict, so a `TryWait` with zero timeout returning a conflict result is cleaner).

### 4. File output path resolution

Move or reuse the static path-building methods from `RunCommandSupport`:
- `ResolveAuditPath`
- `BuildSimulationAuditPath`
- `BuildSimulationStatePath`
- `BuildSimulationOutputPath`

These are pure string functions with no CLI dependency. They can be moved to the Application layer or kept as static utility methods.

### 5. Range execution

`RunRange` loops from `startDate` to `endDate` inclusive. Each date calls the internal single-run method with `RunType = Range`. The governor state store is shared across dates within the range (same `TradeGovernorDbStateStore` instance, same mode). Each date produces a separate `signal_run` row and separate file output.

### 6. CLI handler migration

After the execution service is built, update the CLI handlers to delegate to it. This validates that the execution service produces identical output to the current handlers. The handlers become thin wrappers:

```csharp
// RunRealtimeHandler.Handle(configPath)
var result = _executionService.RunRealtime("cli");
Console.WriteLine($"Generated {result.TickerCount} signals.");
return result.Status == SignalRunStatus.Completed ? 0 : 1;
```

## Test Scenarios

### Unit tests (mocked pipeline and persistence)

| Scenario | Input | Expected Output |
|----------|-------|-----------------|
| Successful realtime run | Watchlist with 3 tickers, mock pipeline returns 3 signals | `Status = Completed`, `TickerCount = 3`, 3 signals in result, `PersistRun` called once with correct request, file output written |
| Successful asof run | AsOfDate = 2026-03-07 | `IsSimulation = true`, `FixedClock` used, governor state mode = `"simulation"`, simulation file paths used |
| Successful range run | Start = 2026-03-01, End = 2026-03-03 | 3 results returned (one per date), 3 `PersistRun` calls, governor state shared across dates |
| Empty watchlist | Config with empty watchlist | `Status = Completed`, `TickerCount = 0`, zero signals |
| Pipeline throws | Mock pipeline throws `InvalidOperationException` | `Status = Failed`, `ErrorMessage` contains exception message, `PersistRun` called with error, no file output |
| Persistence failure after pipeline success | Mock persistence throws | Failure result returned, file output still attempted (best-effort), no unhandled exception |
| Trigger source preserved | Trigger = `"api"` | Persisted run has `Trigger = "api"` |
| Concurrent live runs serialized | Two parallel `RunRealtime` calls | Second call waits or returns conflict |

### Integration tests (Testcontainers)

| Scenario | Input | Expected Output |
|----------|-------|-----------------|
| End-to-end realtime with DB persistence | Mocked market data provider, real DB | `signal_run` and `signal_run_result` rows written, `trade_governor_state` updated, result matches DB state |
| End-to-end asof with simulation isolation | AsOfDate = 2026-03-07 | Governor state written with mode `"simulation"`, live state unchanged |
| Range accumulates governor state | 3-day range with BUY signals | `buys_today` increments across days, resets on day boundary |

## Open Questions

1. **Should file output be retained?** The execution plan says "keep temporarily as write-behind for debugging." This feature retains file output. A future step will remove it once DB reads are validated.

2. **Should the CLI handlers be migrated in this step or a later step?** Migrating them validates the execution service end-to-end, but it changes CLI behavior (signals are now persisted to DB). Recommendation: migrate them in this step to validate, but keep the CLI project functional.

3. **SemaphoreSlim wait vs reject?** For the execution service layer, use `WaitAsync` with a timeout. The API layer (Step 3) will translate timeout to 409 Conflict. The worker layer (Step 4) will log and skip.
