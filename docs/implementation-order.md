# Implementation Order

This document describes the order in which major system capabilities should be implemented in the C# rewrite. Each phase builds on the previous one and adds testable functionality.

## Guiding Principles

- Start with capabilities that are deterministic and do not require external network access.
- Build shared infrastructure (config, state, audit) early so later phases can reuse it.
- Defer Yahoo Finance integration until the internal logic is solid and testable, then validate the live path before marking the phase complete.
- Use mock-container contract tests for HTTP integrations when the external service cannot be run locally; keep those tests separate from live smoke validation.
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

## Phase 9: Yahoo Finance Adapter — Live Transport Verification

**Status: PARTIALLY COMPLETE (contract suite done; live acceptance still blocked on the future refresh workflow)**

**Capabilities:** Verified Yahoo adapter request/response contract via mock HTTP server; live transport verification deferred to refresh workflow

**What landed:**
- `YahooFinanceAdapter` implements `IMarketDataProvider` with internal seams: `YahooFinanceRequestFactory`, `IYahooFinanceTransport`, `YahooFinanceResponseParser`
- Typed exception hierarchy (auth, rate-limit, transport, parsing)
- Boundary clamping, chronological ordering, timestamp normalization
- `GetNextEarningsDate` parses Yahoo `calendarEvents` (raw/fmt/string/date-only)
- 12 adapter-focused offline tests covering mapping, ordering, boundary, earnings, and error handling
- Diagnostic writer plumbed through `EarningsGate` and `TechnicalScorer`
- WireMock.Net in-process contract suite (`YahooFinanceContractTests`) exercising real `YahooFinanceHttpTransport` over real TCP against deterministic Yahoo-shaped responses — 17 scenarios covering historical prices, earnings, HTTP errors, malformed responses, request headers, ticker normalization, and date boundary clamping
- `WireMockServerFixture` (xUnit `IClassFixture`) managing WireMock server lifecycle with per-test stub reset
- Internal base-URI injection seam in `YahooFinanceRequestFactory` for test-time redirection to mock server
- 5 checked-in JSON fixture files under `tests/fixtures/yahoo-finance/`

**What remains:**
- Verify or fix `YahooFinanceHttpTransport` against live Yahoo endpoints
- If the refresh workflow is async, add async transport support
- Run a network-enabled smoke validation proving the refresh workflow persists a valid snapshot
- Migrate WireMock from in-process to `WireMock.Net.Testcontainers` for consistency with Phase 11 (deferred — see `docs/reviews/yahoo-finance-adapter-testcontainers-fixes.md`)

**Why not fully closed:** The adapter's primary consumer is the refresh worker built on top of snapshot persistence, not the synchronous CLI pipeline. The contract suite validates request/response shape without network access, but Phase 9 cannot close until the live HTTP path has been proven end-to-end by the future refresh workflow.

**Validation:** 17 contract tests pass without network access, 12 adapter-focused offline tests pass, all existing Domain, Application, Infrastructure, CrossValidation, and persistence tests pass, solution builds with zero warnings.

## Phase 10: Market Data Persistence ✅

**Status: COMPLETED**

**Capabilities:** PostgreSQL-backed market-data snapshots with explicit refresh persistence

**What was built:**
- PostgreSQL schema with `market_data_refresh_run`, `market_data_snapshot`, `market_data_snapshot_quote`, and `market_data_snapshot_missing_symbol` tables via EF Core migration (`AddMarketDataPersistence`)
- EF Core entities (`MarketDataRefreshRunEntity`, `MarketDataSnapshotEntity`, `MarketDataSnapshotQuoteEntity`, `MarketDataSnapshotMissingSymbolEntity`) with string-mapped enums, `DateOnly` trading dates, `decimal` prices, and getter-only navigation collection properties for immutability
- Fluent `IEntityTypeConfiguration` classes enforcing snake_case table names, unique constraints (`refresh_run_id`, `snapshot_id + symbol + quote_date`, `snapshot_id + symbol`), cascade/restrict delete behaviors, and composite indexes
- `AppDbContext` with `ApplyConfigurationsFromAssembly` and `DbSet` properties for all four entity types
- `MarketDataSnapshotPersistenceService` implementing the full refresh persistence workflow: symbol normalization, quote deduplication, negative-price/volume validation, coverage enforcement (no symbol both persisted and missing), transactional snapshot writes, and best-effort run failure marking on rollback
- `MarketDataSnapshotReadService` with `GetLivePriceHistory` (freshness-gated via `FreshUntilUtc`) and `GetHistoricalPriceHistory` (as-of-date ordered, no TTL rejection) returning `PriceBar[]` for downstream technical scoring
- `LiveSnapshotMarketDataProvider` and `HistoricalSnapshotMarketDataProvider` implementing `IMarketDataProvider` via the read service
- Request/result models (`PersistMarketDataRefreshRequest`, `PersistedMarketDataSymbol`, `PersistedMarketDataQuote`, `PersistedMarketDataMissingSymbol`, `MarketDataRefreshPersistenceResult`)
- `MarketDataPersistenceLimits` centralizing max-length constants for trigger, provider, error, and detail fields
- `DesignTime/AppDbContextFactory` for EF Core migration tooling
- `appsettings.json` and `appsettings.Development.json` with PostgreSQL connection strings
- NuGet dependencies: `Npgsql.EntityFrameworkCore.PostgreSQL` and `Microsoft.EntityFrameworkCore.Design`
- `docker-compose.yml` PostgreSQL service (`postgres:16-alpine`) with health check, named volume (`pgdata`), and credentials
- `app` service `depends_on: postgres: condition: service_healthy` and `ConnectionStrings__AppDb` environment variable override for Docker networking
- Persistence DI registration (`AddPersistence`) with `PersistenceOptions`, `AppDbContext` using `UseNpgsql`, and `PostgreSqlHealthCheck`
- DI registration wired into CLI composition root (`Program.cs`)

**Validation:** `AppDbContextModelTests` verifying EF Core model snapshot consistency; `MarketDataSnapshotPersistenceServiceTests` covering full success, partial refresh, total failure, quote deduplication, coverage validation, negative-price rejection, and transactional rollback scenarios; `MarketDataSnapshotReadServiceTests` covering live freshness gating, historical as-of-date selection, missing-symbol exclusion, and empty-result paths; `PersistenceDependencyInjectionTests` verifying DI resolution. All 65 tests pass, solution builds with zero warnings.

## Phase 11: Testcontainers Integration ✅

**Status: COMPLETED**

**Capabilities:** Real PostgreSQL integration testing via Testcontainers

**What was built:**
- `Testcontainers.PostgreSql` NuGet package added to `BlowingCandles.Infrastructure.Tests`; `Microsoft.EntityFrameworkCore.InMemory` removed
- Shared xUnit collection fixture (`PostgresContainerFixture`) with `IAsyncLifetime` that starts a single `postgres:16-alpine` container per test run and applies the EF Core migration
- `PostgresContainerCollection` collection definition with `ICollectionFixture<PostgresContainerFixture>` for shared container lifecycle
- `ResetAsync()` table truncation helper (`TRUNCATE TABLE market_data_refresh_run RESTART IDENTITY CASCADE`) for per-test isolation
- `AppDbContextModelTests` migrated to Testcontainers: fixture start verification, snake_case table/constraint validation, unique index validation, check constraint violation (negative TTL), duplicate quote rejection, and cascade delete verification — all against real PostgreSQL
- `MarketDataSnapshotPersistenceServiceTests` migrated to Testcontainers: full success, partial success, total failure, quote deduplication, and coverage validation tests running against real PostgreSQL
- `MarketDataSnapshotReadServiceTests` migrated to Testcontainers: live freshness gating, historical as-of-date selection, missing-symbol exclusion, live snapshot provider delegation, and historical snapshot provider TTL bypass tests running against real PostgreSQL
- `PersistenceDependencyInjectionTests` split into two classes: non-container tests (`PersistenceOptions` defaults, missing connection string, unreachable health check) remain standalone; DI resolution test (`AddPersistence_ValidConnectionString_ResolvesAppDbContext`) moved to `PersistenceDependencyInjectionPostgresTests` using the Testcontainers connection string
- WireMock.Net Testcontainers migration deferred (see `docs/reviews/yahoo-finance-adapter-testcontainers-fixes.md`)

**Why eleventh:** The persistence layer (Phase 10) was implemented with InMemory tests as a stopgap. InMemory does not enforce check constraints, unique indexes, cascade deletes, or PostgreSQL-specific type mappings. Testcontainers closes this gap by running the exact same migration against real PostgreSQL, validating the schema and service behavior together.

**Validation:** All persistence tests pass against real PostgreSQL via Testcontainers. Migration applies successfully as part of fixture setup. Unique constraints, check constraints, and cascade deletes are exercised by the test suite. No InMemory provider usage remains. Non-container tests (POCO defaults, DI validation, unreachable health check) remain independent and pass without Docker.

## Phase 12: REST API Endpoints ✅

**Status: COMPLETED**

**Capabilities:** RESTful signal retrieval over HTTP

**What was built:**
- Minimal ASP.NET Core Web API project (`BlowingCandles.Api`) with `Program.cs` entry point and `ApiHost` static builder
- `GET /api/signals` — returns the latest generated signals as a JSON array (reads from `signals.json`)
- `GET /api/signals/{ticker}` — returns a single signal for the given ticker (case-insensitive), or HTTP 404
- `SignalsFileReader` service encapsulating file I/O with result-type error handling (`NotFound`, `Corrupt`, `Unavailable` → HTTP 503)
- `SignalsJsonSerializer` extracted to `BlowingCandles.Application` as shared serialization logic between `OutputRenderer` and the API, using `JsonSerializerDefaults.Web` (camelCase) with explicit `JsonSerializerOptions` passed to all `Results.Json()` calls
- JSON responses using plain enum strings consistent with `signals.json` (`"BUY"`, not `"Action.BUY"`)
- No authentication or authorization middleware (local/operator use)
- Default listen URL `http://localhost:5000`, configurable via `--urls` or `ASPNETCORE_URLS`
- Docker support: `api` entrypoint command in `docker-entrypoint.sh`, API published to `/app/api/`, port 5000 exposed
- `BlowingCandles.Api.Tests` xUnit project with in-process hosting via `ApiHost.Build()`
- Solution file updated with both `BlowingCandles.Api` and `BlowingCandles.Api.Tests`

**Why twelfth:** All signal generation and market-data persistence workflows are complete. The API is a thin read layer over existing pipeline output.

**Validation:** Integration tests covering valid signals (order preservation), case-insensitive ticker lookup, unknown ticker (404), missing file (503), empty array (200 with `[]`), and malformed JSON (503). All tests pass, solution builds with zero warnings.

## Phase 13: Signal Run Persistence ✅

**Status: COMPLETED**

**Capabilities:** PostgreSQL-backed signal pipeline result persistence and DB-backed trade governor state

**What was built:**
- PostgreSQL schema with `signal_run`, `signal_run_result`, and `trade_governor_state` tables via EF Core migration (`AddSignalRunPersistence`)
- `SignalRunEntity` and `SignalRunResultEntity` with string-mapped enums (`SignalRunType`: Realtime/AsOf/Range; `SignalRunStatus`: Running/Completed/Failed; domain `Action` and `NewsState` enums), `DateOnly` as-of dates, simulation flag, and getter-only navigation collection property
- `TradeGovernorStateEntity` with unique index on `mode` (`"live"` or `"simulation"`), `day` string, `buys_today` count, and nullable `last_buy_at` timestamp
- Fluent `IEntityTypeConfiguration` classes enforcing snake_case table names, check constraints (`ticker_count >= 0`, `buys_today >= 0`), unique index on `(run_id, ticker)` for results, unique index on `mode` for governor state, cascade delete from `signal_run` to `signal_run_result`, and composite indexes for query performance
- `AppDbContext` extended with `DbSet<SignalRunEntity>`, `DbSet<SignalRunResultEntity>`, and `DbSet<TradeGovernorStateEntity>`
- `SignalRunPersistenceService` implementing the full run persistence workflow: failed-run validation (rejects signals when `ErrorMessage` is set), ticker normalization (trim + uppercase), ticker deduplication (last wins, consistent with `MarketDataSnapshotPersistenceService`), reason truncation (256 chars), transactional writes with governor state upsert, deduplicated `TickerCount`, and best-effort run failure marking on rollback
- `SignalRunReadService` with `GetLatestLiveRun()` (latest completed non-simulation run), `GetRunById()`, and `GetRecentRuns()` (capped at 100, ordered by `started_at_utc` descending) — all with eager-loaded results sorted by ticker
- `TradeGovernorDbStateStore` implementing `ITradeGovernorStateStore` backed by PostgreSQL with day-reset logic matching `JsonStateStore`, upsert pattern with concurrent-insert race handling, and strict mode validation (`"live"` or `"simulation"` only)
- `TradeGovernorDbStateStoreFactory` for DI-compatible mode-parameterized construction
- `TradeGovernorStatePersistence` shared static helper for upsert, normalization, and day formatting — used by both `TradeGovernorDbStateStore.Save()` and `SignalRunPersistenceService.PersistGovernorState()`
- `SignalRunPersistenceLimits` centralizing max-length constants for ticker (16), trigger (32), reason (256), error message (1024), mode (16), and day (10)
- Request/result models: `PersistSignalRunRequest`, `SignalRunPersistenceResult`, `SignalRunReadResult`, `SignalRunSignalResult`
- DI registration of `SignalRunPersistenceService`, `SignalRunReadService`, and `TradeGovernorDbStateStoreFactory` in `AddPersistence()`
- `PostgresContainerFixture.ResetAsync()` updated to truncate `signal_run`, `trade_governor_state`, and `market_data_refresh_run` with `RESTART IDENTITY CASCADE`

**Why thirteenth:** Foundational schema for the API-first architecture. API write endpoints, background worker, and DB-backed signal reads all depend on these tables and services. The market-data persistence layer (Phase 10) and Testcontainers infrastructure (Phase 11) provide the EF Core and testing patterns reused here.

**Validation:** `SignalRunPersistenceServiceTests` covering successful run with signals and governor state, empty watchlist, failed run, failed-run-with-signals rejection, ticker normalization and reason truncation, duplicate ticker deduplication (last wins with correct `TickerCount`), and simulation metadata. `SignalRunReadServiceTests` covering latest live run selection, null when no completed live run, run-by-ID lookup with null for missing, recent runs ordering with limit, and excessive limit capping. `TradeGovernorDbStateStoreTests` covering empty-table load, matching-day load, stale-day reset, single-row-per-mode upsert, and live/simulation isolation. All 130 tests pass across all projects, solution builds with zero warnings.

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
| Yahoo Finance integration strategy | 9 | Offline seams and WireMock.Net contract suite complete; live transport verification deferred to refresh workflow |
| Sync-over-async pattern | 9 | Decided by the refresh worker's pipeline design; synchronous CLI reads shift to snapshots |
| Yahoo HTTP contract testing | 9 ✅ | WireMock.Net in-process contract suite exercises real `YahooFinanceHttpTransport` over TCP — 17 scenarios, no network access required |
| Yahoo verification gate | 9 | Partially met — contract suite passes ✅; live refresh-workflow smoke validation still required before phase closure |
| Snapshot read policy | 10 | Live reads require a fresh snapshot (`FreshUntilUtc > now`); historical reads select the latest snapshot whose `as_of_date` is not newer than the requested simulation date |
| Refresh persistence model | 10 | Persist immutable snapshots with explicit missing-symbol tracking; do not mutate prior snapshot rows in place |
| PostgreSQL ORM | 10 | EF Core with Npgsql provider, fluent configuration, string-mapped enums |
| Integration test infrastructure | 11 ✅ | Testcontainers with `postgres:16-alpine`, xUnit collection fixture, table truncation for isolation; WireMock migration deferred |
| Signal run persistence model | 13 ✅ | Immutable signal runs with deduplicated results; DB-backed governor state with mode isolation; `SignalRunPersistenceService` and `SignalRunReadService`; `TradeGovernorDbStateStore` implementing `ITradeGovernorStateStore` |
| Signal run ticker deduplication | 13 ✅ | Last-wins deduplication consistent with `MarketDataSnapshotPersistenceService` quote deduplication; `TickerCount` reflects deduplicated count |
| Trade governor DB state store | 13 ✅ | Upsert pattern with concurrent-insert race handling; strict mode validation (`"live"` / `"simulation"`); day-reset matching `JsonStateStore` behavior |
| Shared execution service | Step 2 ✅ | `SignalRunExecutionService` in Application layer; wall-clock run timestamps; per-mode `SemaphoreSlim` concurrency; `RunCommandSupport` deleted; CLI handlers as thin wrappers |

## API-First Execution Plan

The following steps transform the application from a CLI-first tool into a long-running API service. The signal pipeline (`EarningsGate` → `TechnicalScorer` → `TradeGovernor`) remains identical. This is a transport and persistence change, not a domain logic change.

### Step 1: Signal Run Persistence Schema ✅ (Phase 13)

PostgreSQL tables for persisting signal pipeline results, trade governor state, and signal run metadata. Foundation for all subsequent steps.

### Step 2: Shared Execution Service ✅

**Status: COMPLETED**

**What was built:**
- `ISignalRunExecutionService` interface and `SignalRunExecutionService` implementation in the Application layer (`src/BlowingCandles.Application/Services/`) as the single entry point for realtime, as-of, and range signal pipeline runs regardless of trigger source (API, worker, CLI)
- Shared orchestration: pipeline execution → PostgreSQL run persistence via `SignalRunPersistenceService` → audit JSONL writes → file write-behind artifact output (signals.txt, signals.json)
- Structured `SignalRunExecutionResult` record returned to callers with run ID, status, signals, timestamps, and error message
- Live vs simulation isolation via per-mode `SemaphoreSlim` concurrency guards (`LiveSemaphore`, `SimulationSemaphore`) and mode-specific governor state, audit, and output paths
- `RunCommandSupport.cs` deleted; path-building helpers (`ResolveAuditPath`, `BuildSimulationAuditPath`, `BuildSimulationOutputPath`) moved into the execution service as private static methods
- CLI handlers (`RunRealtimeHandler`, `RunAsOfHandler`, `RunRangeHandler`) migrated to thin wrappers delegating to `ISignalRunExecutionService`
- `BlowingCandles.Application` project now references `BlowingCandles.Infrastructure` for persistence, audit, clock, calendar, and config types
- Wall-clock timestamps (`DateTimeOffset.UtcNow`) for run metadata (`StartedAtUtc`, `CompletedAtUtc`); domain `IClock` used only for pipeline execution

**Validation:** `SignalRunExecutionServiceTests` covering successful realtime/as-of/range runs, empty watchlist, pipeline failure with failed-run persistence, persistence failure with best-effort file write-behind, concurrent live run serialization, and cross-mode independence (simulation does not block live). `RunCommandHandlerTests` verifying thin handler delegation and exit code mapping. All 20 tests pass, solution builds with zero warnings.

**Feature spec:** `docs/features/signal-run-execution-service.md`

### Step 3: API Endpoints and DB-Backed Reads

Extend the API with write endpoints (`POST /api/runs/realtime`, `POST /api/runs/asof`, `POST /api/runs/range`) and switch read endpoints from file-backed (`SignalsFileReader`) to DB-backed (`SignalRunReadService`). Add run history (`GET /api/runs`, `GET /api/runs/{id}`) and health/readiness endpoints.

### Step 4: Background Worker

`SignalGenerationWorker : BackgroundService` that runs signal generation on a configurable schedule. Replaces the external cron + CLI pattern. Calls `ISignalRunExecutionService.RunRealtime()` with trigger source `"worker"`. Respects the same concurrency semaphore as API-triggered runs.

### Step 5: Containerized Service and Cleanup

Make the API the default and only runtime surface. Update `docker-compose.yml`, `Dockerfile`, and `docker-entrypoint.sh`. Optionally remove the CLI project. Remove `SignalsFileReader`, file write-behind, and `JsonStateStore` once DB equivalents are validated.
