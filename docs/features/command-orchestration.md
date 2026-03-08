# Feature: Command Orchestration

## Feature Name

command-orchestration

## Purpose

Wire the full signal pipeline and finalize the three run commands (`run-asof`, `run-realtime`, `run-range`) so they execute the complete earnings gate → technical scoring → trade governor pipeline, write all output artifacts (signals.txt, signals.json, audit JSONL, state.json), and produce correct console output. The handler scaffolds already exist with working dependency wiring; this phase validates end-to-end correctness against Python reference output and fixes any integration issues discovered during testing.

## Inputs

### run-realtime

- `config.yaml` — loaded from the default path.
- No additional arguments.

### run-asof

- `config.yaml` — loaded from the default path.
- `asOfDate` (DateOnly) — the simulation date in `yyyy-MM-dd` format.

### run-range

- `config.yaml` — loaded from the default path.
- `startDate` (DateOnly) — first simulation date in `yyyy-MM-dd` format.
- `endDate` (DateOnly) — last simulation date in `yyyy-MM-dd` format (inclusive).

### Shared pipeline inputs (per run)

- `watchlist` — list of ticker strings from config, normalized (trimmed, uppercased, deduplicated) by `SignalPipeline`.
- `IClock` — `SystemClock` for realtime, `FixedClock` for as-of and range.
- `IMarketDataProvider` — `YahooFinanceAdapter` for price and earnings fallback data.
- `IEarningsCalendar` — `EarningsCalendarFile` loaded from config path.
- `ITradeGovernorStateStore` — `JsonStateStore` backed by live or simulation state file.

## Outputs

### Per run

| Artifact | Live (run-realtime) | Simulation (run-asof, run-range) |
|----------|--------------------|---------------------------------|
| Text signals | `{output.text_file}` (e.g. `signals.txt`) | `asof_{date}.signals.txt` |
| JSON signals | `{output.json_file}` (e.g. `signals.json`) | `asof_{date}.signals.json` |
| Audit log | `{audit.jsonl_path}` (e.g. `logs/decisions.jsonl`) | `logs/sim_decisions.jsonl` |
| State file | `{state.path}` (e.g. `data/state.json`) | `data/sim_state.json` |

### Console output

- `run-realtime`: `Generated {count} signals.` followed by text and JSON output paths.
- `run-asof`: `Generated {count} signals for {date}.` followed by text and JSON output paths.
- `run-range`: `Generated signals for {days} day(s).` followed by simulation audit path.

### Exit codes

- `0` — success.
- `1` — unhandled exception (caught by `Program.Main`).

## Configuration

| Field | Source | Default | Description |
|-------|--------|---------|-------------|
| `watchlist` | config.yaml | required | Ticker symbols to process |
| `news.local_earnings_calendar` | config.yaml | `earnings_calendar.json` | Path to local earnings calendar |
| `news.block_window_hours` | config.yaml | `48` | Earnings proximity block window |
| `policy.max_buys_per_day` | config.yaml | `99999999` | Maximum BUY signals per day |
| `policy.cooldown_minutes` | config.yaml | `0` | Cooldown between BUY signals |
| `output.text_file` | config.yaml | `signals.txt` | Live text output path |
| `output.json_file` | config.yaml | `signals.json` | Live JSON output path |
| `state.path` | config.yaml | `data/state.json` | Live state file path |
| `audit.jsonl_path` | config.yaml | `logs/decisions.jsonl` | Live audit log path |

## Edge Cases

1. **Empty watchlist.** Pipeline returns empty list. Output files are written (empty). Console reports 0 signals.
2. **run-range with start == end.** Single day processed. Functionally identical to run-asof.
3. **run-range with end < start.** `ArgumentOutOfRangeException` thrown. Caught by `Program.Main`, exit code 1.
4. **Config file missing.** Exception thrown during load. Caught by `Program.Main`, exit code 1.
5. **State file missing or corrupt.** `JsonStateStore` silently resets to empty state. Pipeline proceeds normally.
6. **Audit directory does not exist.** `JsonlAuditWriter` creates the directory before writing.
7. **Output directory does not exist.** `OutputRenderer` creates the directory before writing.
8. **All tickers fail (Yahoo Finance down).** Every ticker gets WAIT with error reasons. Output files contain all WAIT signals.
9. **run-range state accumulation.** State file is shared across the loop. Buy counts from day N carry into day N+1 until the day-reset triggers on the next date. This means policy constraints accumulate correctly within a single day and reset on day boundaries.
10. **run-range audit accumulation.** All days append to the same simulation audit file. Order is chronological.
11. **Simulation never touches live paths.** run-asof and run-range use `sim_` prefixed state and audit paths, and `asof_{date}` prefixed output files.
12. **Duplicate tickers in watchlist.** `SignalPipeline` deduplicates after normalization. Each ticker appears once in output.

## Implementation Notes

### Shared dependency wiring

**`RunCommandSupport`** (Cli/Handlers/RunCommandSupport.cs) is the composition root helper shared by all three run handlers. It encapsulates:

- Factory methods for creating `IEarningsCalendar` and `IMarketDataProvider` implementations from config.
- `RunSignalsCore` — constructs `EarningsGate`, `TechnicalScorer`, `TradeGovernor`, and `SignalPipeline`, then calls `pipeline.Run`. This is the single place where domain services are wired to infrastructure implementations.
- Static path-building helpers: `ResolveAuditPath`, `BuildSimulationAuditPath`, `BuildSimulationStatePath`, `BuildSimulationOutputPath`.
- Constructor accepts optional factory overrides for testability (e.g., injecting mock providers in handler tests).

Each run handler delegates signal generation to `RunCommandSupport.RunSignals` and is responsible only for clock selection, path resolution, output writing, audit appending, and console output.

### Components to modify

1. **`RunAsOfHandler`** (Cli/Handlers/RunAsOfHandler.cs) — update console output from "scaffold signals" to production messages. Verify all path-building logic is correct.
2. **`RunRealtimeHandler`** (Cli/Handlers/RunRealtimeHandler.cs) — update console output from "scaffold signals" to production messages.
3. **`RunRangeHandler`** (Cli/Handlers/RunRangeHandler.cs) — update console output from "scaffold signals" to production messages. Verify state sharing across the date loop.

### Components to validate (no changes expected)

4. **`SignalPipeline`** (Application/SignalPipeline.cs) — orchestrates earnings gate → technical scorer → trade governor. Already implemented.
5. **`OutputRenderer`** (Application/OutputRenderer.cs) — writes signals.txt (prefixed enums) and signals.json (plain enums). Already implemented.
6. **`JsonlAuditWriter`** (Infrastructure/Audit/JsonlAuditWriter.cs) — appends JSONL rows with prefixed enums. Already implemented.
7. **`JsonStateStore`** (Infrastructure/State/JsonStateStore.cs) — day-reset via IClock. Already implemented.
8. **`Program.cs`** (Cli/Program.cs) — command routing and argument parsing. Already implemented.

### Pipeline execution sequence

For each run invocation:

1. Load config from `config.yaml`.
2. Create clock (`SystemClock` or `FixedClock`).
3. Wire dependencies: `EarningsCalendarFile`, `YahooFinanceAdapter`, `EarningsGate`, `TechnicalScorer`, `JsonStateStore`, `TradeGovernor`, `SignalPipeline`.
4. Call `pipeline.Run(watchlist, clock)` → `List<FinalSignal>`.
5. Write `signals.txt` and `signals.json` via `OutputRenderer`.
6. Append to audit JSONL via `JsonlAuditWriter`.
7. Print summary to console.
8. Return exit code 0.

### run-range loop specifics

- The pipeline is created once (single `JsonStateStore` instance shared across days).
- Each iteration creates a new `FixedClock` for the current date.
- State day-reset triggers automatically when the clock date changes between iterations.
- All iterations append to the same simulation audit file.
- Each iteration writes its own output files (`asof_{date}.signals.txt`, `asof_{date}.signals.json`).

### Path isolation rules

| Path type | Live | Simulation |
|-----------|------|------------|
| State | `{state.path}` | `sim_{filename}` in same directory |
| Audit | `{audit.jsonl_path}` | `sim_{filename}` in same directory |
| Text output | `{output.text_file}` | `asof_{date}.signals.txt` in same directory |
| JSON output | `{output.json_file}` | `asof_{date}.signals.json` in same directory |

### Decision: Run Range architecture

In-process loop. No subprocesses spawned. The `RunRangeHandler` iterates dates and calls `pipeline.Run` for each date with a new `FixedClock`. This is simpler and allows state to accumulate naturally across days.

### Dependencies

- `SignalPipeline` from Application layer
- `OutputRenderer` from Application layer
- `EarningsGate`, `TechnicalScorer`, `TradeGovernor` from Domain layer
- `YamlConfigLoader`, `JsonStateStore`, `JsonlAuditWriter`, `EarningsCalendarFile`, `YahooFinanceAdapter`, `FixedClock`, `SystemClock` from Infrastructure layer

## Test Scenarios

### End-to-end pipeline tests (Application.Tests)

1. **Full pipeline with mocked providers produces expected FinalSignals.** Wire `SignalPipeline` with mock `IMarketDataProvider` and `IEarningsCalendar`. Verify output signals match expected action, news state, market action, and reason for each ticker.
2. **Pipeline with all-clear earnings and strong technicals produces BUY.** Mock earnings gate to return TRADE_OK for all tickers. Mock market data to produce score >= 80. Verify FinalSignal action is BUY.
3. **Pipeline with earnings block produces NO_TRADE regardless of technicals.** Mock earnings within block window. Verify FinalSignal action is WAIT with news-related reason, even when technicals would produce BUY.
4. **Pipeline with weak technicals produces SELL.** Mock TRADE_OK earnings but score <= 20. Verify FinalSignal action is SELL.
5. **Pipeline with empty watchlist returns empty list.** No signals generated, no exceptions.

### Output format tests (Application.Tests)

6. **signals.txt uses prefixed enums.** Verify text output contains `Action.BUY`, `NewsState.TRADE_OK`, etc.
7. **signals.json uses plain enums.** Verify JSON output contains `"BUY"`, `"TRADE_OK"`, etc.
8. **Audit JSONL uses prefixed enums.** Verify audit rows contain `"Action.BUY"`, `"NewsState.TRADE_OK"`, etc.

### run-asof handler tests

9. **Simulation output paths are correct.** For date `2026-01-15`, verify text path is `asof_2026-01-15.signals.txt` and JSON path is `asof_2026-01-15.signals.json`.
10. **Simulation state path uses sim_ prefix.** Verify state file path is `data/sim_state.json` when live path is `data/state.json`.
11. **Simulation audit path uses sim_ prefix.** Verify audit path is `logs/sim_decisions.jsonl` when live path is `logs/decisions.jsonl`.
12. **FixedClock is set to midnight UTC of the as-of date.** Verify clock time passed to pipeline matches the requested date at 00:00 UTC.

### run-realtime handler tests

13. **Live output paths match config.** Verify text and JSON output use config paths directly (no sim_ prefix, no date prefix).
14. **Live state path matches config.** Verify state file uses `config.State.Path` directly.
15. **SystemClock is used.** Verify handler constructs `SystemClock` (not FixedClock).

### run-range handler tests

16. **Correct number of days processed.** Range `2026-01-01` to `2026-01-03` processes 3 days.
17. **State accumulates across days.** If day 1 produces a BUY that hits max_buys_per_day=1, day 1's subsequent tickers are limited. Day 2 resets and allows buys again.
18. **All days share one audit file.** Verify all signals from all days appear in the same simulation audit JSONL, in chronological order.
19. **Each day gets its own output files.** Verify 3 text files and 3 JSON files for a 3-day range.
20. **end < start throws ArgumentOutOfRangeException.** Verify exception is thrown before any processing.
21. **Single-day range (start == end) produces one day of output.** Functionally identical to run-asof.

### Integration tests (full stack with fixtures)

22. **Golden test: run-asof output matches Python reference.** For a known date and fixture data (mocked Yahoo), compare signals.txt, signals.json, and audit JSONL line-by-line against Python reference output.
23. **Golden test: run-range multi-day output matches Python reference.** For a known date range and fixture data, compare all output artifacts against Python reference.
24. **State.json written correctly after run.** Verify state file contains correct day, buys_today count, and last_buy_at timestamp after a run with BUY signals.
