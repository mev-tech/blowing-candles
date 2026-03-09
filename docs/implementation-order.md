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

## Phase 11: Testcontainers Integration

**Status: NOT STARTED**

**Capabilities:** Real PostgreSQL integration testing via Testcontainers

**What to build:**
- `Testcontainers.PostgreSql` NuGet package added to `BlowingCandles.Infrastructure.Tests`
- Shared xUnit collection fixture (`PostgresContainerFixture`) that starts a single `postgres:16-alpine` container per test run and applies the EF Core migration
- Migration of `MarketDataSnapshotPersistenceServiceTests`, `MarketDataSnapshotReadServiceTests`, and `AppDbContextModelTests` from InMemory/hardcoded-localhost to the Testcontainers-managed PostgreSQL instance
- `PersistenceDependencyInjectionTests` DI resolution test updated to use the Testcontainers connection string
- Table truncation helper (`TRUNCATE ... CASCADE`) for per-test isolation
- Removal of `Microsoft.EntityFrameworkCore.InMemory` package
- Optional: migrate Yahoo Finance contract suite from in-process `WireMock.Net` to `WireMock.Net.Testcontainers` for Testcontainers consistency (see `docs/reviews/yahoo-finance-adapter-testcontainers-fixes.md`)

**Why eleventh:** The persistence layer (Phase 10) is implemented with InMemory tests as a stopgap. InMemory does not enforce check constraints, unique indexes, cascade deletes, or PostgreSQL-specific type mappings. Testcontainers closes this gap by running the exact same migration against real PostgreSQL, validating the schema and service behavior together.

**Validation:** All existing persistence tests pass against real PostgreSQL. Migration applies successfully as part of fixture setup. Unique constraints, check constraints, and cascade deletes are exercised by the test suite. No InMemory provider usage remains.

## Phase 12: REST API Endpoints

**Status: NOT STARTED**

**Capabilities:** RESTful signal retrieval over HTTP

**What to build:**
- Minimal ASP.NET Core Web API project (`BlowingCandles.Api`) or extend the CLI with `Microsoft.AspNetCore` hosting
- `GET /api/signals` — Returns the latest generated signals (reads from `signals.json`)
- `GET /api/signals/{ticker}` — Returns the latest signal for a specific ticker
- JSON responses using plain enum serialisation (consistent with `signals.json`)
- No authentication required (local/operator use)

**Why twelfth:** All signal generation and market-data persistence workflows are complete. The API remains a thin read layer over existing pipeline output.

**Validation:** Integration tests verifying correct HTTP status codes, JSON response structure, and ticker filtering.

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
| Integration test infrastructure | 11 | Testcontainers with `postgres:16-alpine`, xUnit collection fixture, table truncation for isolation |
