# Fix Specification: containerized-service-cleanup

## Summary of Issues

1. **`docs/architecture.md` line 104 still references `JsonStateStore`.** The `TradeGovernorDbStateStore` description says "day-reset logic matching `JsonStateStore`". `JsonStateStore` was deleted in this feature; the reference is stale.

2. **`docs/architecture.md` line 496 references `signals.json` as a file artifact.** Key constraint #6 reads: "Audit JSONL uses prefixed enum strings (`"Action.BUY"`). `signals.json` uses plain strings (`"BUY"`)." Since `signals.json` files are no longer written, this should reference the API JSON serialization format instead.

3. **`ComparisonHelpers.cs` contains three dead file-path overloads.** `AssertTextMatches` (lines 13–17), `AssertJsonMatches` (lines 30–39), and `AssertJsonlMatches` (lines 60–77) accept two file paths but are never called. Only the `*ContentMatches` variants are used after the in-memory refactoring.

4. **`SignalGenerationWorkerTests.TestWorkspace` config includes vestigial `output`, `state`, and `audit` sections** (lines 140–146). These config keys are no longer consumed by `SignalRunExecutionService`.

5. **`SignalRunExecutionServiceTests.CreateConfig` creates vestigial `OutputConfig`, `StateConfig`, and `AuditConfig`** (lines 457–471). These config sections are no longer consumed by the service under test.

6. **`Dockerfile` HEALTHCHECK missing `--start-period`.** The API needs a few seconds to start. Without `--start-period`, Docker may count startup-time probe failures toward the retry limit.

## Required Changes

### 1. Fix `TradeGovernorDbStateStore` description in `docs/architecture.md`

File: `docs/architecture.md`, line 104.

Replace:
```
day-reset logic matching `JsonStateStore`
```
With:
```
day-reset logic
```

The full line becomes: `TradeGovernorDbStateStore — implements ITradeGovernorStateStore backed by PostgreSQL. Upserts a single row per mode ("live" or "simulation") with day-reset logic. Handles concurrent-insert races via retry on unique constraint violation.`

### 2. Update key constraint #6 in `docs/architecture.md`

File: `docs/architecture.md`, line 496.

Replace:
```
6. Audit JSONL uses prefixed enum strings (`"Action.BUY"`). `signals.json` uses plain strings (`"BUY"`).
```
With:
```
6. Audit JSONL uses prefixed enum strings (`"Action.BUY"`). API JSON responses (via `SignalsJsonSerializer`) use plain strings (`"BUY"`).
```

### 3. Remove dead file-path overloads from `ComparisonHelpers.cs`

File: `tests/BlowingCandles.CrossValidation.Tests/ComparisonHelpers.cs`

Delete the following three methods:

- `AssertTextMatches(string expectedPath, string actualPath)` (lines 13–17)
- `AssertJsonMatches(string expectedPath, string actualPath)` (lines 30–39)
- `AssertJsonlMatches(string expectedPath, string actualPath)` (lines 60–77)

Keep all `*ContentMatches` methods and all private helpers (`ReadNormalizedLines`, `ReadNormalizedLinesFromContent`, `Canonicalize`, `Normalize`).

### 4. Remove vestigial config sections from `SignalGenerationWorkerTests.TestWorkspace`

File: `tests/BlowingCandles.Api.Tests/SignalGenerationWorkerTests.cs`, lines 140–146.

Remove:
```yaml
                output:
                  text_file: output/live.signals.txt
                  json_file: output/live.signals.json
                state:
                  path: data/state.json
                audit:
                  jsonl_path: logs/decisions.jsonl
```

The config becomes:
```yaml
                watchlist: []
                news:
                  local_earnings_calendar: earnings_calendar.json
```

### 5. Remove vestigial config sections from `SignalRunExecutionServiceTests.CreateConfig`

File: `tests/BlowingCandles.Application.Tests/SignalRunExecutionServiceTests.cs`, lines 457–471.

Remove the `Output`, `State`, and `Audit` properties from the `AppConfig` construction in `CreateConfig`. The method becomes:

```csharp
private AppConfig CreateConfig(string[] watchlist, int maxBuysPerDay)
{
    return new AppConfig
    {
        Watchlist = watchlist,
        News = new NewsConfig
        {
            LocalEarningsCalendar = "earnings_calendar.json",
            ResolvedLocalEarningsCalendar = GetPath("earnings_calendar.json"),
            BlockWindowHours = 48
        },
        Policy = new PolicyConfig
        {
            MaxBuysPerDay = maxBuysPerDay,
            CooldownMinutes = 0
        }
    };
}
```

Also remove the file-existence assertions for paths that are no longer configured. In each test method, remove assertions like:
- `Assert.False(File.Exists(GetPath("output/...")))`
- `Assert.False(File.Exists(GetPath("logs/...")))`
- `Assert.False(File.Exists(GetPath("data/...")))`

These assertions verified that file write-behind was removed, which is now structurally guaranteed — the service has no file I/O code paths at all.

### 6. Add `--start-period` to Dockerfile HEALTHCHECK

File: `Dockerfile`, line 56.

Replace:
```dockerfile
HEALTHCHECK CMD ["curl", "-fsS", "http://127.0.0.1:5000/health/live"]
```
With:
```dockerfile
HEALTHCHECK --start-period=10s CMD ["curl", "-fsS", "http://127.0.0.1:5000/health/live"]
```

## Affected Modules or Files

| File | Change Type |
|------|-------------|
| `docs/architecture.md` | Update (2 lines) |
| `tests/BlowingCandles.CrossValidation.Tests/ComparisonHelpers.cs` | Remove dead code (3 methods) |
| `tests/BlowingCandles.Api.Tests/SignalGenerationWorkerTests.cs` | Remove vestigial config |
| `tests/BlowingCandles.Application.Tests/SignalRunExecutionServiceTests.cs` | Remove vestigial config and assertions |
| `Dockerfile` | Update (1 line) |

## Edge Cases to Address

1. **`AppConfig` required properties** — Before removing `Output`, `State`, and `Audit` from `CreateConfig`, verify that `AppConfig` does not require these properties (e.g., via constructor validation or non-nullable properties). If they have required defaults or are non-nullable, keep them with minimal placeholder values rather than removing them entirely.

2. **`ComparisonHelpers` file-path overloads used outside the solution** — These methods are public. Verify no other test project or script references them before deleting. A solution-wide search for `AssertTextMatches`, `AssertJsonMatches`, and `AssertJsonlMatches` (without `Content` in the name) confirms they are unreferenced.

3. **`ReadNormalizedLines(string path)` still needed** — The private `ReadNormalizedLines` method is used by the `*ContentMatches` methods (via `ReadNormalizedLines(expectedPath)` to read golden files). Do not remove it when removing the file-path overloads.

## Test Updates Required

No new tests needed. Changes 3–5 remove dead code and vestigial setup from existing tests. All remaining test logic and assertions are unaffected. Run `dotnet test` after changes to confirm no regressions.
