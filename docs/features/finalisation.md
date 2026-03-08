# Feature: Finalisation

## Feature Name

finalisation

## Purpose

Prepare the C# application for production deployment by hardening the Dockerfile with proper layer caching, NuGet restore isolation, and health-check support; adding a cross-validation test harness that runs both Python and C# implementations against identical fixture inputs and diffs all output artifacts; and ensuring all existing tests pass cleanly in the containerised build.

## Inputs

- Existing `Dockerfile` (basic two-stage build, no restore isolation).
- Existing `BlowingCandles.sln` solution with all projects and tests.
- Python reference implementation in `signals-bot/`.
- Shared fixture data: `config.yaml`, `earnings_calendar.json`, known audit JSONL fixtures.
- Mocked `IMarketDataProvider` and `IEarningsCalendar` test doubles already used in earlier phases.

## Outputs

### Dockerfile improvements

- Production-ready multi-stage Dockerfile with:
  - Isolated NuGet restore layer (copy `.csproj`/`.sln` first, then `dotnet restore`).
  - Publish layer with `--no-restore` for cache efficiency.
  - Non-root user in the runtime stage.
  - `LABEL` metadata (maintainer, description).
  - `.dockerignore` file excluding `signals-bot/`, `bin/`, `obj/`, `logs/`, `data/`, test results.

### Cross-validation test harness

- `tests/BlowingCandles.CrossValidation.Tests/` xUnit project containing fixture-driven golden tests.
- Test fixtures: pre-captured market data and earnings calendar JSON files under `tests/fixtures/cross-validation/`.
- Golden reference output files (signals.txt, signals.json, audit JSONL) captured from the Python implementation using the same fixtures.
- Tests that run the C# `SignalPipeline` with fixture-backed mocks and compare output line-by-line against the golden files.

### Build verification

- All existing unit and integration tests pass inside a Docker build (tests run as a build stage).

## Configuration

| Field | Source | Default | Description |
|-------|--------|---------|-------------|
| No new config fields | — | — | This phase adds no runtime configuration. All changes are build-time and test-time only. |

## Edge Cases

1. **Empty watchlist in cross-validation fixture.** Golden test should verify empty output files are identical between Python and C# (both produce headers-only or empty content).
2. **All tickers WAIT in cross-validation.** When fixture market data triggers all-WAIT (e.g. insufficient price history), both implementations must produce identical WAIT signals with matching reasons.
3. **Docker build with no network access after restore.** The publish and runtime stages must not require network access. Only the restore stage needs NuGet feeds.
4. **Test stage failure blocks image build.** If any test fails during the Docker test stage, the build must fail and no runtime image is produced.
5. **Platform-specific line endings.** Golden file comparisons must normalize line endings (trim trailing `\r`) to avoid false mismatches between Linux containers and Windows-generated fixtures.
6. **Floating-point differences in scores.** Cross-validation tests compare FinalSignal action and reason strings, not raw floating-point scores, since both implementations use the same thresholds and simple moving averages.
7. **Audit JSONL field ordering.** JSON field order may differ between Python (`json.dumps`) and C# (`System.Text.Json`). Comparison should parse each line as JSON and compare field values, not raw strings.
8. **Prefixed enum format divergence.** Python uses `Action.BUY` style in audit output; C# must match exactly. Cross-validation catches any formatting mismatch.

## Implementation Notes

### Dockerfile hardening

1. **Layer caching.** Copy `BlowingCandles.sln` and all `*.csproj` files first, run `dotnet restore`, then copy the rest of the source. This means NuGet restore is cached unless project references change.
2. **Test stage.** Add an intermediate build stage that runs `dotnet test` against the restored and built solution. This stage is not included in the final runtime image but gates the build.
3. **Non-root user.** Create an `app` user in the runtime stage and switch to it via `USER`. The entrypoint runs as non-root.
4. **Working directory.** Set `WORKDIR /app` in the runtime stage. Config and data files are expected to be mounted at `/app/config.yaml`, `/app/earnings_calendar.json`, `/app/data/`, `/app/logs/`.

### .dockerignore

```
signals-bot/
**/bin/
**/obj/
logs/
data/
*.md
.git/
.gitignore
```

### Cross-validation test project

1. **Fixture generation (manual, one-time).** Run the Python implementation with mocked/fixture data for a known date (e.g. `2026-01-15`) and capture `signals.txt`, `signals.json`, and `decisions.jsonl` as golden files. Store under `tests/fixtures/cross-validation/`.
2. **Fixture market data.** Store price history CSVs and earnings calendar JSON that both implementations consume. The C# test loads these via a `FixtureMarketDataProvider` implementing `IMarketDataProvider`.
3. **Test structure.** One test class `GoldenOutputTests` with:
   - `RunAsOf_SignalsTxt_MatchesPythonReference` — compare signals.txt line-by-line.
   - `RunAsOf_SignalsJson_MatchesPythonReference` — parse both JSON files, compare signal arrays element-by-element.
   - `RunAsOf_AuditJsonl_MatchesPythonReference` — parse each JSONL line as JSON, compare field values.
   - `RunRange_MultiDay_MatchesPythonReference` — same comparisons for a 3-day range.
4. **Comparison helpers.** Utility methods for: line-by-line text diff with line-ending normalization; JSON array element comparison ignoring field order; JSONL line-by-line JSON comparison.

### Build verification

- The Dockerfile test stage runs `dotnet test --no-build --configuration Release` against all test projects except `CrossValidation.Tests` (which requires fixture files not present in the Docker context by default).
- Cross-validation tests run locally or in CI, not inside the Docker build, since they require fixture files and potentially the Python runtime for regenerating golden files.

### Components to create

| Component | Path | Description |
|-----------|------|-------------|
| `.dockerignore` | `.dockerignore` | Docker build context exclusions |
| `FixtureMarketDataProvider` | `tests/BlowingCandles.CrossValidation.Tests/FixtureMarketDataProvider.cs` | Test double loading price data from fixture CSVs |
| `GoldenOutputTests` | `tests/BlowingCandles.CrossValidation.Tests/GoldenOutputTests.cs` | Golden file comparison tests |
| `ComparisonHelpers` | `tests/BlowingCandles.CrossValidation.Tests/ComparisonHelpers.cs` | Text and JSON diff utilities |

### Components to modify

| Component | Path | Description |
|-----------|------|-------------|
| `Dockerfile` | `Dockerfile` | Hardened multi-stage build with restore isolation, test stage, non-root user |
| `BlowingCandles.sln` | `BlowingCandles.sln` | Add cross-validation test project reference |

### Dependencies

- All Phase 1–7 components (no new runtime dependencies).
- xUnit, FluentAssertions (test-time only, already referenced by existing test projects).
- `System.Text.Json` for JSONL comparison parsing (already a project dependency).

## Test Scenarios

### Dockerfile tests (manual / CI)

1. **Docker build succeeds.** `docker build -t blowing-candles .` completes without error.
2. **Test stage runs all unit tests.** The intermediate test stage executes `dotnet test` and all tests pass.
3. **Runtime image starts and prints help.** `docker run blowing-candles --help` prints usage text and exits 0.
4. **Runtime image runs check-calendar with mounted config.** Mount `config.yaml` and `earnings_calendar.json`, run `check-calendar`, verify exit code and stdout.
5. **Runtime image runs as non-root.** `docker run blowing-candles whoami` returns `app`, not `root`.
6. **Image size is reasonable.** Runtime image based on `mcr.microsoft.com/dotnet/runtime:8.0` should be under 200 MB.

### Cross-validation golden tests (automated, xUnit)

7. **run-asof signals.txt matches Python reference.** For fixture date `2026-01-15`, C# signals.txt output matches golden file line-by-line after line-ending normalization.
8. **run-asof signals.json matches Python reference.** For fixture date `2026-01-15`, C# signals.json output matches golden file element-by-element (action, news_state, market_action, reason per ticker).
9. **run-asof audit JSONL matches Python reference.** For fixture date `2026-01-15`, each audit JSONL line matches the golden file's corresponding line field-by-field (ticker, action, news_state, reason, timestamp format).
10. **run-range multi-day output matches Python reference.** For fixture range `2026-01-13` to `2026-01-15`, all 3 days' output files and the combined audit JSONL match golden files.
11. **All-WAIT scenario matches.** Fixture with insufficient price data produces identical all-WAIT output from both implementations.
12. **Mixed-action scenario matches.** Fixture with varied technicals (BUY, SELL, WAIT mix) and one earnings-blocked ticker produces identical output from both implementations.

### Existing test regression

13. **All Domain.Tests pass.** No regressions from Dockerfile or project structure changes.
14. **All Infrastructure.Tests pass.** No regressions.
15. **All Application.Tests pass.** No regressions.
