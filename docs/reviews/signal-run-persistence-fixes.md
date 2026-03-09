# Fixes: signal-run-persistence

## Summary of Issues

Six issues remain unaddressed from the previous review. No code changes were applied.

1. **Silent signal discard on failed runs.** `SignalRunPersistenceService.PersistRun()` at line 37 accepts both `ErrorMessage` and non-empty `Signals`. When `ErrorMessage` is set, signals are silently ignored. The caller gets no indication that data was dropped.

2. **No ticker deduplication before insert.** `SignalRunPersistenceService.PersistRun()` at line 58 passes normalized tickers directly to EF Core. If two `FinalSignal` entries normalize to the same ticker (e.g., `"AAPL"` and `" aapl "`), the unique constraint on `(run_id, ticker)` throws `DbUpdateException`, marking the run as `Failed`. Should deduplicate (last wins), consistent with `MarketDataSnapshotPersistenceService`'s quote deduplication pattern.

3. **`TickerCount` set from raw signal count.** `SignalRunPersistenceService.PersistRun()` at line 30 sets `TickerCount = request.Signals.Count` before any deduplication. After fix #2, this must reflect the actual deduplicated count.

4. **No upper bound on `GetRecentRuns` limit.** `SignalRunReadService.GetRecentRuns()` at line 34 accepts any positive integer. A caller can request millions of rows with no cap.

5. **Missing comment on upsert retry intent.** `TradeGovernorDbStateStore.Save()` at line 51 catches `DbUpdateException` and retries. The logic is correct (re-throws when row not found) but the intent is not documented. Without a comment, future readers may misunderstand the retry as a general error-swallowing pattern.

6. **Execution plan references `signal_run_audit` without deferral note.** `api-first-execution-plan.md` line 123 lists `signal_run_audit` as part of Step 1 schema, but the feature spec and implementation correctly scoped it out. The plan and implementation are misaligned.

## Required Changes

### 1. Validate signal count on failed runs

**File:** `src/BlowingCandles.Infrastructure/Persistence/Services/SignalRunPersistenceService.cs`

Add validation after the `ArgumentNullException.ThrowIfNull(request)` guard at line 20, before constructing the entity:

```csharp
if (!string.IsNullOrWhiteSpace(request.ErrorMessage) && request.Signals.Count > 0)
{
    throw new ArgumentException(
        "A failed run must not include signals. Pass an empty signal list when ErrorMessage is set.",
        nameof(request));
}
```

### 2. Deduplicate normalized tickers before insert

**File:** `src/BlowingCandles.Infrastructure/Persistence/Services/SignalRunPersistenceService.cs`

Replace lines 58-60:

```csharp
var resultEntities = request.Signals
    .Select(signal => CreateSignalResult(run.Id, signal))
    .ToArray();
```

With:

```csharp
var resultEntities = request.Signals
    .Select(signal => (Signal: signal, Ticker: NormalizeTicker(signal.Ticker)))
    .Where(x => x.Ticker.Length > 0)
    .GroupBy(x => x.Ticker, StringComparer.Ordinal)
    .Select(group => CreateSignalResult(run.Id, group.Last().Signal))
    .ToArray();
```

### 3. Update `TickerCount` to use deduplicated count

**File:** `src/BlowingCandles.Infrastructure/Persistence/Services/SignalRunPersistenceService.cs`

Move `TickerCount` assignment from line 30 (before insert) to after deduplication. Remove `TickerCount` from the initial entity construction and set it after building `resultEntities`:

```csharp
run.TickerCount = resultEntities.Length;
```

Place this line immediately before `run.Status = SignalRunStatus.Completed;` at line 69, inside the transaction block.

For the error path (lines 37-46), set `TickerCount = 0` since failed runs have no results.

### 4. Cap `GetRecentRuns` limit

**File:** `src/BlowingCandles.Infrastructure/Persistence/Services/SignalRunReadService.cs`

Add a constant and apply it:

```csharp
private const int MaxRecentRunsLimit = 100;

public IReadOnlyList<SignalRunReadResult> GetRecentRuns(int limit = 20)
{
    if (limit <= 0)
    {
        return [];
    }

    var effectiveLimit = Math.Min(limit, MaxRecentRunsLimit);

    return CreateRunQuery()
        .OrderByDescending(x => x.StartedAtUtc)
        .Take(effectiveLimit)
        .AsEnumerable()
        .Select(Map)
        .ToArray();
}
```

### 5. Add comment to upsert retry block

**File:** `src/BlowingCandles.Infrastructure/Persistence/Services/TradeGovernorDbStateStore.cs`

Add a comment before line 51:

```csharp
// Retry on concurrent insert race: if another caller inserted the row between our
// SingleOrDefault check and our Add, the unique index on mode causes DbUpdateException.
// If the row exists, update it (last writer wins). If not, the error is unrelated — re-throw.
catch (DbUpdateException)
```

### 6. Acknowledge audit deferral in the execution plan

**File:** `docs/features/api-first-execution-plan.md`

Replace the `signal_run_audit` row in the Step 1 table (line 123) with:

```
| `signal_run_audit` | **Deferred.** Audit entries continue to be written to `decisions.jsonl`. DB-backed audit logging will be added as a separate additive migration in a future step. |
```

## Affected Modules or Files

| File | Change |
|------|--------|
| `src/BlowingCandles.Infrastructure/Persistence/Services/SignalRunPersistenceService.cs` | Validate failed-run signals, deduplicate tickers, move `TickerCount` assignment |
| `src/BlowingCandles.Infrastructure/Persistence/Services/SignalRunReadService.cs` | Cap `GetRecentRuns` limit at 100 |
| `src/BlowingCandles.Infrastructure/Persistence/Services/TradeGovernorDbStateStore.cs` | Add explanatory comment to retry block |
| `docs/features/api-first-execution-plan.md` | Mark `signal_run_audit` as deferred |
| `tests/BlowingCandles.Infrastructure.Tests/SignalRunPersistenceServiceTests.cs` | Add 2 new tests, replace 1 existing test |
| `tests/BlowingCandles.Infrastructure.Tests/SignalRunReadServiceTests.cs` | Add 1 new test |

## Edge Cases to Address

1. **Failed run with non-empty signals.** Must throw `ArgumentException` before persisting anything. Currently silently ignores signals and writes a `Failed` run with `TickerCount` reflecting the dropped signals.

2. **Duplicate normalized tickers in a single run.** Must deduplicate (last wins) and complete successfully. Currently throws `DbUpdateException` and marks the run as `Failed`.

3. **`TickerCount` after deduplication.** Must equal the number of persisted `signal_run_result` rows, not the raw input signal count. Currently set before deduplication occurs.

4. **`GetRecentRuns` with excessive limit.** Must cap at 100 to prevent unbounded queries. Currently passes any positive integer directly to `Take()`.

## Test Updates Required

### New tests in `SignalRunPersistenceServiceTests`

| Test | Scenario | Expected |
|------|----------|----------|
| `PersistRun_FailedRunWithSignals_ThrowsArgumentException` | `ErrorMessage = "failed"` + `Signals` contains one `FinalSignal` | `ArgumentException` thrown. No `signal_run` row persisted (validation runs before any DB write). |
| `PersistRun_DuplicateNormalizedTickers_DeduplicatesLastWins` | Two signals: `"AAPL"` with action `BUY`, then `" aapl "` with action `WAIT` | One `signal_run_result` row with ticker `"AAPL"`, action `WAIT`. `signal_run.ticker_count = 1`. Run status `Completed`. |

### Replaced test in `SignalRunPersistenceServiceTests`

| Test | Change |
|------|--------|
| `PersistRun_DuplicateNormalizedTickers_RollsBackResultsAndMarksRunFailed` | **Remove.** After fix #2, duplicate tickers no longer cause failure. Replaced by `PersistRun_DuplicateNormalizedTickers_DeduplicatesLastWins` above. |

### New test in `SignalRunReadServiceTests`

| Test | Scenario | Expected |
|------|----------|----------|
| `GetRecentRuns_ExcessiveLimit_CapsAtMaximum` | Seed 3 runs, call `GetRecentRuns(500)` | Returns all 3 runs. No exception. Confirms limit is capped internally without affecting results when seeded count is below the cap. |
