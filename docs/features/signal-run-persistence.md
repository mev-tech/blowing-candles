# Feature: Signal Run Persistence

## Feature Name

signal-run-persistence

## Purpose

Persist signal pipeline execution results to PostgreSQL. This is the foundational schema for the API-first architecture — all subsequent features (API write endpoints, background worker, DB-backed signal reads) depend on these tables and services. Replaces file-based signal output (`signals.json`, `signals.txt`) and file-based trade governor state (`state.json`) with database-backed equivalents for the signal run lifecycle.

## Inputs

| Input | Source | Format | Required |
|-------|--------|--------|----------|
| `List<FinalSignal>` | `SignalPipeline.Run()` output | Domain model: ticker, action, news state, market action, reason, timestamp | Yes |
| Run metadata | Caller (API endpoint or worker) | Run type (realtime/asof/range), trigger source (api/worker/cli), as-of date for simulations | Yes |
| Trade governor state | `TradeGovernor` | Day string, buys today count, last buy timestamp | Yes |

## Outputs

| Output | Format | Destination |
|--------|--------|-------------|
| `signal_run` row | PostgreSQL row | `signal_run` table |
| `signal_run_result` rows | PostgreSQL rows (one per ticker per run) | `signal_run_result` table |
| `trade_governor_state` row | PostgreSQL row (upserted per mode) | `trade_governor_state` table |
| `SignalRunPersistenceResult` | C# record | Returned to caller |

### Schema: `signal_run`

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | `bigint` (identity) | PK |
| `run_type` | `varchar(16)` | NOT NULL. String-mapped enum: `Realtime`, `AsOf`, `Range`. |
| `trigger` | `varchar(32)` | NOT NULL. Free-text: `"api"`, `"worker"`, `"cli"`. |
| `status` | `varchar(16)` | NOT NULL. String-mapped enum: `Running`, `Completed`, `Failed`. |
| `as_of_date` | `date` | NOT NULL. For realtime runs, this is today's date. For simulations, the requested date. |
| `is_simulation` | `boolean` | NOT NULL. `true` for asof/range runs, `false` for realtime. |
| `ticker_count` | `int` | NOT NULL, >= 0. Number of tickers processed. |
| `started_at_utc` | `timestamptz` | NOT NULL. |
| `completed_at_utc` | `timestamptz` | Nullable. Set on completion or failure. |
| `error_message` | `varchar(1024)` | Nullable. Set on failure. |

Indexes:
- `ix_signal_run_status_started_at_utc` on (`status`, `started_at_utc`)
- `ix_signal_run_run_type_as_of_date` on (`run_type`, `as_of_date`)
- `ix_signal_run_is_simulation_completed_at_utc` on (`is_simulation`, `completed_at_utc` DESC) — for "latest live run" queries

### Schema: `signal_run_result`

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | `bigint` (identity) | PK |
| `run_id` | `bigint` | NOT NULL, FK to `signal_run.id`, CASCADE delete |
| `ticker` | `varchar(16)` | NOT NULL |
| `action` | `varchar(32)` | NOT NULL. String-mapped `Action` enum. |
| `news_state` | `varchar(32)` | NOT NULL. String-mapped `NewsState` enum. |
| `market_action` | `varchar(32)` | NOT NULL. String-mapped `Action` enum. |
| `reason` | `varchar(256)` | NOT NULL (empty string allowed). |
| `timestamp` | `timestamptz` | NOT NULL. The signal timestamp from the pipeline. |

Constraints:
- Unique index on (`run_id`, `ticker`) — one result per ticker per run

Indexes:
- `ix_signal_run_result_run_id` on (`run_id`)

### Schema: `trade_governor_state`

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | `bigint` (identity) | PK |
| `mode` | `varchar(16)` | NOT NULL. `"live"` or `"simulation"`. |
| `day` | `varchar(10)` | NOT NULL. `"yyyy-MM-dd"` format. |
| `buys_today` | `int` | NOT NULL, >= 0. |
| `last_buy_at` | `timestamptz` | Nullable. |

Constraints:
- Unique index on (`mode`) — exactly one row per mode (upsert pattern)

## Configuration

### New entities

- `SignalRunEntity` in `Persistence/Entities/`
- `SignalRunStatus` enum in `Persistence/Entities/`
- `SignalRunType` enum in `Persistence/Entities/`
- `SignalRunResultEntity` in `Persistence/Entities/`
- `TradeGovernorStateEntity` in `Persistence/Entities/`

### New configurations

- `SignalRunConfiguration` in `Persistence/Configurations/`
- `SignalRunResultConfiguration` in `Persistence/Configurations/`
- `TradeGovernorStateConfiguration` in `Persistence/Configurations/`

### New services

- `SignalRunPersistenceService` in `Persistence/Services/`
- `SignalRunReadService` in `Persistence/Services/`
- `TradeGovernorDbStateStore` in `Persistence/Services/` — implements `ITradeGovernorStateStore`

### New models

- `PersistSignalRunRequest` in `Persistence/Models/`
- `SignalRunPersistenceResult` in `Persistence/Models/`
- `SignalRunReadResult` in `Persistence/Models/`

### Updated files

- `AppDbContext.cs` — add `DbSet` properties for `SignalRunEntity`, `SignalRunResultEntity`, `TradeGovernorStateEntity`
- `DependencyInjection.cs` — register new services
- `SignalRunPersistenceLimits` (or extend `MarketDataPersistenceLimits`) — max-length constants for new fields

### EF Core migration

- New migration `AddSignalRunPersistence` via `dotnet ef migrations add`
- Applied to Testcontainers fixture in tests

### NuGet dependencies

None new. Reuses existing `Npgsql.EntityFrameworkCore.PostgreSQL` and `Testcontainers.PostgreSql`.

## Edge Cases

1. **Empty watchlist.** A run with zero tickers produces a `signal_run` row with `ticker_count = 0`, `status = Completed`, and zero `signal_run_result` rows. This is valid.

2. **Pipeline failure.** If `SignalPipeline.Run()` throws, the run is persisted with `status = Failed` and `error_message` set. No `signal_run_result` rows are written. Fail-safe: the pipeline itself should still produce all-WAIT signals before throwing, but if it doesn't, the run is marked failed.

3. **Duplicate run for same as-of date.** Multiple runs for the same date are allowed. The read service returns the latest completed run. No unique constraint on `as_of_date`.

4. **Trade governor state day reset.** When `TradeGovernorDbStateStore.Load()` is called and the stored `day` differs from the clock's current day, the state resets to `buys_today = 0`, `last_buy_at = null`, same as `JsonStateStore`. The row is upserted (not a new row per day).

5. **Concurrent writes to trade governor state.** Two simultaneous runs for the same mode could race on the upsert. The unique index on `mode` prevents duplicate rows. Use `SET buys_today = ...` with the value from the domain, not an increment — last writer wins. This matches the file-based behavior where last writer wins.

6. **Live vs simulation isolation.** `trade_governor_state` has separate rows for `"live"` and `"simulation"` modes. A simulation run never reads or writes the `"live"` row. This preserves the existing file-based isolation (`state.json` vs `sim_state.json`).

7. **Transaction scope.** The `signal_run` row, all `signal_run_result` rows, and the `trade_governor_state` upsert for a single run are committed in one transaction. If any part fails, the entire run is rolled back, then the run is marked `Failed` in a separate save (best-effort, same pattern as `MarketDataSnapshotPersistenceService`).

8. **Long reason strings.** The `reason` column is `varchar(256)`. Reasons from the pipeline are typically short (`"EARNINGS_LT_48H"`, `"MARKET_DATA_ERROR"`, empty string). If a reason exceeds 256 characters, truncate silently.

9. **Ticker normalization.** Tickers are normalized (trim + uppercase) before persistence, consistent with the existing `MarketDataSnapshotPersistenceService` pattern.

## Implementation Notes

### 1. Entity pattern

Follow the existing entity conventions from `MarketDataRefreshRunEntity`:
- Sealed classes with public get/set properties
- `long Id` as identity PK
- String-mapped enums via `HasConversion<string>()`
- Getter-only navigation collection properties (`List<T>` initialized to `[]`)
- Snake_case table and column names via fluent configuration

### 2. SignalRunPersistenceService

```csharp
public sealed class SignalRunPersistenceService
{
    public SignalRunPersistenceResult PersistRun(PersistSignalRunRequest request) { ... }
}
```

`PersistSignalRunRequest` contains:
- `SignalRunType RunType`
- `string Trigger`
- `DateOnly AsOfDate`
- `bool IsSimulation`
- `DateTimeOffset StartedAtUtc`
- `DateTimeOffset CompletedAtUtc`
- `List<FinalSignal> Signals`
- `string? ErrorMessage`

The service:
1. Creates and saves `SignalRunEntity` with `Status = Running`
2. Begins a transaction
3. Creates `SignalRunResultEntity` rows from `FinalSignal` list
4. Updates run status to `Completed`
5. Commits transaction
6. On failure: rolls back, marks run `Failed` (best-effort)

### 3. SignalRunReadService

```csharp
public sealed class SignalRunReadService
{
    public SignalRunReadResult? GetLatestLiveRun() { ... }
    public SignalRunReadResult? GetRunById(long runId) { ... }
    public IReadOnlyList<SignalRunReadResult> GetRecentRuns(int limit = 20) { ... }
}
```

`SignalRunReadResult` contains:
- `long RunId`
- `SignalRunType RunType`
- `string Trigger`
- `SignalRunStatus Status`
- `DateOnly AsOfDate`
- `bool IsSimulation`
- `DateTimeOffset StartedAtUtc`
- `DateTimeOffset? CompletedAtUtc`
- `int TickerCount`
- `IReadOnlyList<SignalRunSignalResult> Signals`
- `string? ErrorMessage`

`GetLatestLiveRun()` queries:
```sql
SELECT ... FROM signal_run
WHERE is_simulation = false AND status = 'Completed'
ORDER BY completed_at_utc DESC
LIMIT 1
```
With eager-loaded results.

### 4. TradeGovernorDbStateStore

Implements `ITradeGovernorStateStore`. Same contract as `JsonStateStore`:

```csharp
public sealed class TradeGovernorDbStateStore : ITradeGovernorStateStore
{
    private readonly AppDbContext _dbContext;
    private readonly string _mode; // "live" or "simulation"

    public TradeGovernorState Load(IClock clock) { ... }
    public void Save(TradeGovernorState state) { ... }
}
```

- `Load`: queries by `mode`, applies day-reset logic (same as `JsonStateStore`), returns empty state if no row exists.
- `Save`: upserts the row for the given `mode`. Uses EF Core's `ExecuteUpdate` or manual attach+update pattern.

### 5. Limits

Add `SignalRunPersistenceLimits` as a separate static class (same pattern as `MarketDataPersistenceLimits`):

```csharp
internal static class SignalRunPersistenceLimits
{
    public const int TickerMaxLength = 16;
    public const int TriggerMaxLength = 32;
    public const int ReasonMaxLength = 256;
    public const int ErrorMessageMaxLength = 1024;
    public const int ModeMaxLength = 16;
    public const int DayMaxLength = 10;
}
```

### 6. DI registration

Register `SignalRunPersistenceService`, `SignalRunReadService`, and `TradeGovernorDbStateStore` in `DependencyInjection.AddPersistence()`. `TradeGovernorDbStateStore` requires a `mode` parameter — register as a factory or use a wrapper that accepts mode at construction time.

### 7. Migration

Generate via `dotnet ef migrations add AddSignalRunPersistence`. The migration adds three tables and their indexes/constraints. It does not alter existing `market_data_*` tables.

## Test Scenarios

All tests run against real PostgreSQL via the existing `PostgresContainerFixture` (Testcontainers). The fixture's `ResetAsync()` must be updated to truncate the new tables.

### SignalRunPersistenceService tests

| Scenario | Input | Expected Outcome |
|----------|-------|-----------------|
| Successful run with signals | Run type realtime, 3 `FinalSignal` objects | `signal_run` row with `status = Completed`, 3 `signal_run_result` rows, correct FK relationships |
| Successful run with empty watchlist | Run type realtime, 0 signals | `signal_run` row with `status = Completed`, `ticker_count = 0`, 0 result rows |
| Failed run | Error message set | `signal_run` row with `status = Failed`, `error_message` populated, 0 result rows |
| Ticker normalization | Signal with ticker `" aapl "` | Result row has ticker `"AAPL"` |
| Reason truncation | Signal with 300-char reason | Result row reason truncated to 256 chars |
| Duplicate ticker in signals | Two signals for `"AAPL"` in same run | Throws or deduplicates (last wins) — validate unique constraint |
| Transaction rollback on failure | DB error during result insert | Run marked `Failed`, no result rows persisted |
| Simulation run metadata | `is_simulation = true`, `run_type = AsOf` | Correct values persisted |

### SignalRunReadService tests

| Scenario | Input | Expected Outcome |
|----------|-------|-----------------|
| Latest live run | Two completed live runs | Returns the more recent one with all signals eager-loaded |
| No completed live runs | Only failed or simulation runs | Returns `null` |
| Run by ID | Valid run ID | Returns run with signals |
| Run by ID not found | Non-existent ID | Returns `null` |
| Recent runs | 5 runs of mixed types | Returns all, ordered by `started_at_utc` descending |
| Simulation runs excluded from latest live | One live, one simulation | `GetLatestLiveRun()` returns only the live run |

### TradeGovernorDbStateStore tests

| Scenario | Input | Expected Outcome |
|----------|-------|-----------------|
| Load with no existing row | Empty table | Returns empty state (day = today, buys = 0, no last_buy) |
| Load with matching day | Row exists with today's day | Returns stored buys_today and last_buy_at |
| Load with stale day | Row exists with yesterday's day | Returns reset state (buys = 0, no last_buy) |
| Save creates row | No existing row for mode | Row inserted with correct values |
| Save updates existing row | Row exists for mode | Row updated (not duplicated) |
| Live and simulation isolation | Save to "live", load from "simulation" | Simulation load returns empty, not live values |
| Concurrent save | Two saves to same mode | Last writer wins, no duplicate rows (unique constraint holds) |

### AppDbContext model tests

| Scenario | Expected Outcome |
|----------|-----------------|
| Migration applies cleanly | Fixture starts without errors |
| Snake_case table names | Tables named `signal_run`, `signal_run_result`, `trade_governor_state` |
| Unique constraints enforced | Duplicate (`run_id`, `ticker`) in results throws; duplicate `mode` in state throws |
| Check constraints enforced | Negative `ticker_count` throws |
| Cascade delete | Deleting a `signal_run` deletes its `signal_run_result` rows |
| String-mapped enums | `status`, `run_type`, `action`, `news_state`, `market_action` stored as strings |
