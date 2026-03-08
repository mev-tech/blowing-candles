# Finalisation — Required Fixes

## Summary of Issues

1. **Dockerfile test stage does not gate the build.** The `test` and `publish` stages are independent siblings derived from `restore`. A plain `docker build .` skips the test stage entirely, producing a runtime image even when tests fail.
2. **FixtureMarketDataProvider ignores asOfDate.** All price bars are returned regardless of the requested date, making the harness fragile to fixture data that extends beyond the test date.
3. **ComparisonHelpers silently treats missing golden files as empty.** A missing or mistyped golden file path produces an empty array, which passes when actual output is also empty.
4. **Output file paths are CWD-relative.** `signals.txt`, `signals.json`, and `data/state.json` in fixture `config.yaml` files resolve relative to the test runner's working directory, not the temp workspace.

## Required Changes

### 1. Dockerfile: make publish depend on test stage

Change the publish stage base from `restore` to `test` so that a test failure prevents the publish (and therefore the final runtime image) from being produced.

```dockerfile
# Before
FROM restore AS publish

# After
FROM test AS publish
```

### 2. FixtureMarketDataProvider: filter by asOfDate

In `GetDailyPriceHistory`, filter the returned price bars to only include bars with `Timestamp` on or before `asOfDate`. Replace the discard assignment with an actual date filter applied after loading and sorting.

```csharp
// Before
_ = asOfDate;
// ...return all bars

// After
var cutoff = asOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
// ...return bars.Where(b => b.Timestamp.UtcDateTime <= cutoff)
```

### 3. ComparisonHelpers: assert golden files exist

In `AssertTextMatches`, `AssertJsonMatches`, and `AssertJsonlMatches`, assert that the expected (golden) file exists before reading it. Only the actual output side may tolerate a missing file for the empty-watchlist scenario.

```csharp
// Add at the start of each Assert* method
Assert.True(File.Exists(expectedPath), $"Golden file not found: {expectedPath}");
```

### 4. ScenarioWorkspace: resolve config paths to temp workspace

After copying `config.yaml` into the temp workspace, the `LoadConfig` call returns relative paths like `signals.txt`. These must resolve inside the workspace, not the CWD. Either:

- (a) Set `Environment.CurrentDirectory` to `RootPath` before the test run (and restore after), or
- (b) Have the test methods prepend `workspace.RootPath` when consuming `config.Output.TextFile`, `config.Output.JsonFile`, `config.State.Path`, and `config.Audit.ResolvedJsonlPath`.

Option (b) is preferred because it avoids global state mutation. Update `RunAsOf` and `RunRange` in `GoldenOutputTests` to resolve all config-relative paths against the workspace root, and ensure `CreatePipeline` receives a workspace-resolved state path (it already does for state — extend to output and audit paths).

## Affected Modules or Files

| File | Change |
|------|--------|
| `Dockerfile` | Change `FROM restore AS publish` to `FROM test AS publish` |
| `tests/BlowingCandles.CrossValidation.Tests/FixtureMarketDataProvider.cs` | Filter `GetDailyPriceHistory` results by `asOfDate` |
| `tests/BlowingCandles.CrossValidation.Tests/ComparisonHelpers.cs` | Add golden file existence assertions |
| `tests/BlowingCandles.CrossValidation.Tests/GoldenOutputTests.cs` | Resolve output/audit paths relative to workspace root |

## Edge Cases to Address

1. **asOfDate before all fixture data.** When the filter returns zero bars after applying the date cutoff, `TechnicalScorer` should produce WAIT with `INSUFFICIENT_DATA`. Verify the all-wait fixture still passes with the date filter applied.
2. **asOfDate on exact bar timestamp.** The cutoff must be inclusive of bars whose date matches `asOfDate` (use `<=`, not `<`).
3. **Parallel test execution.** After fix 4, output files land in isolated temp directories. Verify all six tests can run concurrently without interference (`dotnet test` runs xUnit tests in parallel by default within a single assembly).
4. **Empty-watchlist with golden file assertion.** The empty-watchlist scenario golden directory must contain `signals.txt` (empty or header-only), `signals.json` (`[]`), and `decisions.jsonl` (empty). If `signals.txt` or `decisions.jsonl` are missing, they must be created as empty files in the fixture set. Verify which golden files exist and add any missing ones.

## Test Updates Required

1. **Re-run all six cross-validation tests** after applying fixes 2, 3, and 4 to confirm golden outputs still match.
2. **Verify empty-watchlist golden files are complete.** If `signals.txt` or `decisions.jsonl` are missing from `tests/fixtures/cross-validation/empty-watchlist/golden/run-asof/`, create them as empty files.
3. **Docker build verification.** After fix 1, run `docker build .` with a deliberately broken test (e.g., temporarily corrupt a fixture) and confirm the build fails before producing a runtime image. Then restore and confirm the build succeeds.
4. **No changes to existing Domain, Application, or Infrastructure tests.** These are unaffected.
