# Fix Specification: signal-run-execution-service

## Summary of Issues

1. **Missing test: empty watchlist.** The feature spec requires a test for empty watchlist producing `Status = Completed`, `TickerCount = 0`, zero signals. No test covers this.
2. **Missing test: concurrent live runs serialized.** The feature spec requires a test verifying two parallel `RunRealtime` calls are serialized by the `SemaphoreSlim`. No test covers this.
3. **`startedAtUtc` / `completedAtUtc` use domain clock instead of wall-clock.** For simulation runs with `FixedClock`, both timestamps are the fixed as-of date, not when the run actually executed. Use `DateTimeOffset.UtcNow` for run timing so simulation runs record actual execution time.
4. **`implementation-order.md` not updated.** Step 2 (Shared Execution Service) still shows `← NEXT` instead of being marked completed.
5. **`Application.Tests.csproj` missing explicit `Infrastructure` project reference.** Test file uses `Infrastructure.Persistence.Entities` and `Infrastructure.Persistence.Models` types resolved only transitively.

## Required Changes

### 1. Use wall-clock for run timestamps

In `SignalRunExecutionService.ExecuteSingleRun()`, replace `clock.UtcNow` for `startedAtUtc` and `completedAtUtc` with `DateTimeOffset.UtcNow`. The domain `clock` should only be used for pipeline execution (earnings gate, technical scorer, trade governor). Run metadata should reflect when the run actually happened.

- `var startedAtUtc = clock.UtcNow;` → `var startedAtUtc = DateTimeOffset.UtcNow;`
- `var completedAtUtc = clock.UtcNow;` → `var completedAtUtc = DateTimeOffset.UtcNow;`
- `var failedAtUtc = clock.UtcNow;` → `var failedAtUtc = DateTimeOffset.UtcNow;`

### 2. Update `implementation-order.md`

Change Step 2 heading from `← NEXT` to `✅` and add a brief summary of what was built, matching the style of previous completed steps.

### 3. Add explicit `Infrastructure` project reference to test project

Add `<ProjectReference Include="..\..\src\BlowingCandles.Infrastructure\BlowingCandles.Infrastructure.csproj" />` to `tests/BlowingCandles.Application.Tests/BlowingCandles.Application.Tests.csproj`.

### 4. Add missing test: empty watchlist

Add a test `RunRealtime_EmptyWatchlist_ReturnsCompletedWithZeroSignals` that creates a config with an empty watchlist array and verifies:
- `Status == SignalRunStatus.Completed`
- `TickerCount == 0`
- `Signals` is empty
- `PersistRun` called once with zero signals
- File output written (empty signals files)

### 5. Add missing test: concurrent live runs serialized

Add a test `RunRealtime_ConcurrentCalls_AreSerialized` that:
- Uses a `ManualResetEventSlim` or similar to block the first `RunRealtime` call mid-execution (e.g., in the market data provider factory)
- Starts a second `RunRealtime` call on a background thread
- Verifies the second call does not begin pipeline execution until the first completes
- Asserts both calls succeed and `PersistRun` is called twice

## Affected Modules or Files

| File | Change |
|------|--------|
| `src/BlowingCandles.Application/Services/SignalRunExecutionService.cs` | Replace `clock.UtcNow` with `DateTimeOffset.UtcNow` for `startedAtUtc`, `completedAtUtc`, `failedAtUtc` |
| `tests/BlowingCandles.Application.Tests/SignalRunExecutionServiceTests.cs` | Add empty watchlist test, add concurrency serialization test, update timestamp assertions in existing tests to use `DateTimeOffset.UtcNow` range checks instead of exact fixed-clock values |
| `tests/BlowingCandles.Application.Tests/BlowingCandles.Application.Tests.csproj` | Add explicit `Infrastructure` project reference |
| `docs/implementation-order.md` | Mark Step 2 as completed |

## Edge Cases to Address

1. **Empty watchlist with persistence failure.** The empty-watchlist test should verify the service handles the zero-signal case end-to-end including persistence. The `PersistSignalRunRequest` should have `Signals = []` and `GovernorState` reflecting a clean state with zero buys.
2. **Concurrent simulation runs do not block live runs.** The concurrency test should also verify that a `RunAsOf` call on the `SimulationSemaphore` does not block a `RunRealtime` call on the `LiveSemaphore` (and vice versa).
3. **Wall-clock timestamps in tests.** After switching to `DateTimeOffset.UtcNow`, existing tests that assert exact timestamp values from `FixedClock` must be updated to assert a reasonable range (e.g., `startedAtUtc <= completedAtUtc`, both within a few seconds of `DateTimeOffset.UtcNow`).

## Test Updates Required

| Test | Change |
|------|--------|
| `RunAsOf_WritesSimulationArtifacts_PersistsSimulationRun_AndLeavesLivePathsUntouched` | Timestamp assertions on the result's `StartedAtUtc`/`CompletedAtUtc` must use range checks instead of relying on fixed-clock values |
| `RunRealtime_WritesLiveArtifacts_AndPersistsLiveRun` | Same timestamp assertion update |
| `RunRealtime_PipelineThrows_PersistsFailedRun_AndSkipsArtifacts` | Same timestamp assertion update |
| `RunRealtime_PersistenceFails_ReturnsFailedResult_AndStillWritesArtifacts` | Same timestamp assertion update |
| `RunRange_UsesSharedSimulationStateAcrossDays_AndWritesPerDayOutputs` | Same timestamp assertion update |
| **NEW** `RunRealtime_EmptyWatchlist_ReturnsCompletedWithZeroSignals` | Add |
| **NEW** `RunRealtime_ConcurrentCalls_AreSerialized` | Add |
| **NEW** `RunRealtime_SimulationDoesNotBlockLive` | Add (verifies cross-mode independence) |
