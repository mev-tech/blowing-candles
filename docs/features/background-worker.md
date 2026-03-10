# Feature: Background Worker for Scheduled Signal Generation

## Summary

Add a `SignalGenerationWorker` hosted service (`BackgroundService`) to the API process that runs signal generation on a configurable interval. The worker calls `ISignalRunExecutionService.RunRealtime("worker")` on each tick, sharing the same concurrency semaphore as API-triggered runs. Configuration is provided via `appsettings.json` (`Worker:IntervalMinutes`). The worker is disabled by default and enabled via configuration.

## Purpose

Replace the external cron + CLI invocation pattern with an in-process scheduled worker. The API process becomes a self-contained service that both serves HTTP requests and generates signals on a schedule. This simplifies deployment (single process), eliminates the need for external cron configuration, and ensures the worker respects the same concurrency guards as API-triggered runs.

## Inputs

| Input | Source | Format | Required |
|-------|--------|--------|----------|
| `ISignalRunExecutionService` | DI (Application layer) | Interface | Yes |
| `Worker:Enabled` | `appsettings.json` / environment variable | Boolean | No (default: `false`) |
| `Worker:IntervalMinutes` | `appsettings.json` / environment variable | Integer (minutes) | No (default: `60`) |

## Outputs

| Output | Format | Destination |
|--------|--------|-------------|
| Completed signal run | Persisted to PostgreSQL via execution service | `signal_run` table |
| Worker lifecycle log messages | Structured log (Information/Error) | Console via `ILogger` |

## Configuration

### appsettings.json

```json
{
  "Worker": {
    "Enabled": false,
    "IntervalMinutes": 60
  }
}
```

- `Worker:Enabled` — controls whether the worker is registered as a hosted service. When `false` (default), the worker is not started and the API behaves exactly as before. When `true`, the worker starts with the host and runs on the configured interval.
- `Worker:IntervalMinutes` — the interval between signal generation runs in minutes. Must be >= 1. Default: `60`.
- Configurable via environment variables: `Worker__Enabled=true`, `Worker__IntervalMinutes=30`.

### Options class

```csharp
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public bool Enabled { get; set; } = false;
    public int IntervalMinutes { get; set; } = 60;
}
```

Bound via `builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName))`.

## Edge Cases

1. **Worker disabled (default)** — `Worker:Enabled` is `false` or absent. The `SignalGenerationWorker` is not registered as a hosted service. No background work occurs. This is the default behavior to avoid surprising existing deployments.
2. **IntervalMinutes < 1** — The worker logs a warning at startup and does not execute any runs. It remains idle until the host shuts down.
3. **Execution service throws** — The worker catches the exception, logs it at `Error` level, and waits for the next interval. The worker never crashes the host.
4. **Host shutdown during run** — The worker checks `CancellationToken` before each run and between the run and the delay. If cancellation is requested during an active run, the run completes (execution service is synchronous and not cancellation-aware), then the worker exits its loop gracefully.
5. **Concurrent API and worker runs** — Both share the same `LiveSemaphore` inside `SignalRunExecutionService`. If an API `POST /api/runs/realtime` is in progress, the worker blocks on the semaphore until the API run completes, then proceeds. This is the existing concurrency model.
6. **Worker run overlaps next interval** — If a run takes longer than `IntervalMinutes`, the next run starts immediately after the current one finishes (no drift accumulation). The delay is computed as `max(0, interval - elapsed)`.
7. **First run timing** — The worker executes its first run immediately on startup (no initial delay), then waits `IntervalMinutes` before subsequent runs.

## Implementation Notes

### Worker Class

**File:** `src/BlowingCandles.Api/Workers/SignalGenerationWorker.cs`

```
SignalGenerationWorker : BackgroundService
```

- Constructor takes `IServiceScopeFactory` (for resolving scoped `ISignalRunExecutionService`), `IOptions<WorkerOptions>`, and `ILogger<SignalGenerationWorker>`.
- `ExecuteAsync(CancellationToken stoppingToken)` loop:
  1. Log "Signal generation worker started with interval {IntervalMinutes} minutes" at `Information`.
  2. Loop while `!stoppingToken.IsCancellationRequested`:
     a. Record `startTime = Stopwatch.GetTimestamp()`.
     b. Create a scope via `IServiceScopeFactory.CreateScope()`.
     c. Resolve `ISignalRunExecutionService` from the scope.
     d. Call `executionService.RunRealtime("worker")` inside try/catch.
     e. On success: log run ID and status at `Information`.
     f. On exception: log at `Error` with exception. Do not rethrow.
     g. Dispose the scope.
     h. Compute remaining delay: `interval - elapsed`. If positive, `await Task.Delay(remaining, stoppingToken)` inside try/catch for `OperationCanceledException` (graceful shutdown).
  3. Log "Signal generation worker stopped" at `Information`.

### DI Registration

**File:** `src/BlowingCandles.Api/ApiHost.cs`

In `Build()`, after existing service registrations:

```csharp
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));

var workerOptions = builder.Configuration.GetSection(WorkerOptions.SectionName).Get<WorkerOptions>();
if (workerOptions is { Enabled: true })
{
    builder.Services.AddHostedService<SignalGenerationWorker>();
}
```

The worker is conditionally registered based on configuration. When `Enabled` is `false` or the section is absent, `AddHostedService` is never called.

### Scope Lifecycle

`ISignalRunExecutionService` is registered as `Scoped` (it depends on scoped `SignalRunPersistenceService` and `TradeGovernorDbStateStoreFactory`). The worker must create a new `IServiceScope` per run and resolve the service from that scope. The scope is disposed after each run to release the scoped `AppDbContext`.

### Trigger Source

The worker uses trigger `"worker"` to distinguish its runs from API-triggered (`"api"`) and CLI-triggered (`"cli"`) runs in the `signal_run` table.

### What NOT to Build

- No cron expression parsing — simple fixed-interval only
- No jitter or randomized delay
- No run-if-stale logic (always runs on schedule regardless of recent API-triggered runs)
- No separate health check for worker status
- No configurable start delay beyond the immediate first run
- No changes to `AppConfig` or `config.yaml` — worker configuration lives in `appsettings.json`

## Test Scenarios

| Scenario | Input | Expected Output |
|----------|-------|-----------------|
| Worker disabled by default | No `Worker` section in config | Worker is not registered; no hosted service starts; no runs executed |
| Worker disabled explicitly | `Worker:Enabled = false` | Same as above |
| Worker enabled, runs on interval | `Worker:Enabled = true, IntervalMinutes = 1` | Worker starts, executes `RunRealtime("worker")`, waits ~1 minute, repeats |
| Worker logs run result | Worker enabled, successful run | Log contains run ID and "Completed" status at Information level |
| Worker survives execution failure | Worker enabled, execution service throws | Worker logs error, does not crash, waits for next interval, runs again |
| Worker respects cancellation | Worker enabled, host shutdown requested | Worker completes current run (if active), exits loop, logs "stopped" |
| Invalid interval (< 1) | `Worker:IntervalMinutes = 0` | Worker logs warning at startup, does not execute runs, remains idle |
| Worker trigger is "worker" | Worker enabled, run completes | `signal_run.trigger` column contains `"worker"` |
| Worker creates scoped service | Worker enabled, multiple runs | Each run uses a fresh `IServiceScope`; no `DbContext` lifetime issues |
| First run is immediate | Worker enabled | First `RunRealtime` call happens without initial delay |
