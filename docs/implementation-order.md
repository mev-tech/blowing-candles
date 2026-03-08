# Implementation Order

This document describes the order in which major system capabilities should be implemented in the C# rewrite. Each phase builds on the previous one and adds testable functionality.

## Guiding Principles

- Start with capabilities that are deterministic and do not require external network access.
- Build shared infrastructure (config, state, audit) early so later phases can reuse it.
- Defer Yahoo Finance integration until the internal logic is solid and testable.
- Each phase should produce a working, testable capability.

## Phase 1: Project Scaffold and Calendar Validator ✅

**Status: COMPLETED**

**Capabilities:** Check Calendar command

**What was built:**
- .NET solution structure (Domain, Application, Infrastructure, CLI projects)
- YAML config loader
- Earnings calendar reader
- Clock abstraction (for deterministic testing of date comparisons)
- Check Calendar CLI command with stdout and exit-code parity

**Validation:** Golden tests comparing stdout and exit codes against Python reference output.

## Phase 2: Audit Analysis ✅

**Status: COMPLETED**

**Capabilities:** Stats Periods command

**What was built:**
- JSONL audit log reader (`JsonlAuditReader`, `AuditReadResult`)
- Domain models (`AuditRecord`, `HoldingPeriod`, `OpenPosition`, `HoldingPeriodAnalysis`)
- Holding period calculation logic (`HoldingPeriodCalculator`)
- Stats Periods CLI command (`StatsPeriodsHandler`)
- Extended `AppConfig` with `audit.jsonl_path` field

**Validation:** Output compared against known audit fixtures.

## Phase 3: Shared Infrastructure ✅

**Status: COMPLETED**

**Capabilities:** Config loading, state persistence, audit writing, output rendering

**What was built:**
- Consolidated config loader (extending Phase 1 bootstrap) with all `config.yaml` sections
- File-backed state store (`JsonStateStore`) with JSON read/write and day-reset logic via `IClock`
- JSONL audit writer (`JsonlAuditWriter`) with correct prefixed enum string formatting
- Output renderers (`OutputRenderer`) for `signals.txt` (prefixed enums) and `signals.json` (plain enums)

**Validation:** Unit tests for state reset behavior, enum serialization, and output formatting.

## Phase 4: Trade Governor ✅

**Status: COMPLETED**

**Capabilities:** Signal merging, policy enforcement

**What was built:**
- Trade Governor domain logic (news gating, market action pass-through, buy limits, cooldown)
- `ITradeGovernorStateStore` interface for state abstraction
- `TradeGovernorState` model for governor-specific state
- State integration for buy tracking via `JsonStateStore` implementing `ITradeGovernorStateStore`
- CLI handler updates (`RunAsOfHandler`, `RunRealtimeHandler`, `RunRangeHandler`) to wire the Trade Governor

**Validation:** Focused tests for each gate (missing news, stale news, NO_TRADE, WAIT, TRADE_OK pass-through) and policy constraint (max buys, cooldown).

## Phase 5: Earnings Gate ✅

**Status: COMPLETED**

**Capabilities:** News sentinel / earnings proximity checking

**What was built:**
- `EarningsGate` domain logic for local calendar parsing and earnings proximity checks
- Inclusive block-window enforcement (`0 <= delta <= block_window`) with `NO_TRADE` reason `EARNINGS_LT_48H`
- Yahoo Finance earnings-date fallback behind `IMarketDataProvider` for tickers absent from the local calendar
- Fail-safe handling for expired calendar entries, unavailable earnings data, and exception paths
- CLI wiring to construct `EarningsGate` with config-driven `news.block_window_hours`

**Why fifth:** Depends on config infrastructure from Phase 3. Local calendar path is deterministic and testable; Yahoo fallback is network-dependent and should be behind an interface.

**Validation:** Unit tests cover local calendar happy paths, inclusive boundary behavior, expired calendar handling, Yahoo fallback outcomes, custom block-window configuration, ticker normalization, and fail-safe exception handling.

## Phase 6: Technical Scoring ✅

**Status: COMPLETED**

**Capabilities:** Market analyst / SMA and RSI scoring

**What was built:**
- `TechnicalScorer` domain service with SMA50, SMA200, RSI14 calculations using simple moving averages (not EMA/Wilder's smoothing)
- Composite scoring logic: close vs SMA50 (+20), close vs SMA200 (+20), golden cross (+20), RSI oversold/neutral (+40/+20)
- Action determination: BUY (score >= 80), SELL (score <= 20), WAIT (otherwise)
- Fail-safe exception handling: all errors produce WAIT with `MARKET_DATA_ERROR`
- Ticker normalization (trim + uppercase) with input-order preservation

**Why sixth:** Most complex numerical logic. Requires careful validation against Python output. Yahoo Finance adapter is network-dependent.

**Validation:** Unit tests covering all scoring thresholds (0, 20, 40, 60, 80, 100), RSI boundary conditions (0, 40, 50, >50, 100), insufficient/empty/null data handling, provider exceptions, ticker normalization, and input order preservation.

## Phase 7: Command Orchestration ✅

**Status: COMPLETED**

**Capabilities:** Run Realtime, Run As-Of, Run Range commands

**What was built:**
- `RunCommandSupport` shared helper for dependency wiring and pipeline execution across all three run commands
- Updated `RunAsOfHandler` with production console output, correct simulation path building, and `FixedClock` wiring
- Updated `RunRealtimeHandler` with production console output, live path usage, and `SystemClock` wiring
- Updated `RunRangeHandler` with production console output, date-loop execution, shared state across days, and per-day output files
- Full pipeline wiring: config → earnings gate → technical scoring → trade governor → output
- In-process date loop for run-range (no subprocesses)

**Why seventh:** Ties everything together. All components must be working before orchestration is meaningful.

**Validation:** End-to-end pipeline tests with mocked providers verifying signal flow, output format tests for signals.txt/signals.json/audit JSONL, and handler tests for path isolation, clock selection, and run-range state accumulation.

## Phase 8: Finalization ✅

**Status: COMPLETED**

**Capabilities:** Docker packaging, cross-validation harness, operational readiness

**What was built:**
- Production-ready multi-stage Dockerfile with isolated NuGet restore layer, test stage that gates the build, `--no-restore` publish, non-root `app` user, and `LABEL` metadata
- `docker-entrypoint.sh` allowing the image to behave as a CLI by default with command override support
- `.dockerignore` excluding `signals-bot/`, build outputs, logs/data, test results, git metadata, and markdown
- `BlowingCandles.CrossValidation.Tests` xUnit project with fixture-driven golden output tests
- `FixtureMarketDataProvider` implementing `IMarketDataProvider` with CSV-based price history and asOfDate filtering
- `ComparisonHelpers` for text (CRLF-normalized), JSON (structural), and JSONL (line-by-line structural) comparison
- `GoldenOutputTests` covering run-asof, run-range, all-WAIT, and empty-watchlist scenarios against Python reference output
- Three fixture sets under `tests/fixtures/cross-validation/`: mixed-actions, all-wait, empty-watchlist with golden signals.txt, signals.json, and audit JSONL artifacts

**Validation:** All six golden output tests pass. All existing Domain, Application, and Infrastructure tests pass. Docker build succeeds with test stage gating.

## Decision Points

The following decisions should be made before or during the indicated phase:

| Decision | Phase | Description |
|----------|-------|-------------|
| .NET target version | 1 ✅ | .NET 8 LTS |
| Test framework | 1 ✅ | xUnit |
| YAML library | 1 ✅ | YamlDotNet |
| Audit reader model | 2 ✅ | Domain models with HoldingPeriodCalculator |
| JSON serializer | 3 ✅ | System.Text.Json |
| State reset behavior | 4 ✅ | Uses `IClock` for day-reset; simulation uses `as_of` (accepted divergence from Python) |
| Run Range architecture | 7 ✅ | In-process loop with shared state (no subprocesses) |
