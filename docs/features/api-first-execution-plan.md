# Plan: API-First Signal Service

## Summary

Transform the application from a CLI-first tool into a long-running API service. The CLI is retired as a runtime surface. The API becomes the sole entry point for both on-demand signal generation and signal retrieval. A background worker handles automatic scheduled runs. All pipeline results are persisted to PostgreSQL — no file-based output for signal consumers.

This is a transport and persistence change, not a domain logic change. The signal pipeline (`EarningsGate` -> `TechnicalScorer` -> `TradeGovernor`) remains identical. Fail-safe defaults, live-vs-simulation isolation, and Python-aligned output contracts are preserved.

## Vision

```
┌─────────────────────────────────────────────────────────┐
│                  ASP.NET Core Host                       │
│                                                         │
│  ┌─────────────┐   ┌──────────────────────────────┐     │
│  │  REST API    │   │  Background Worker            │     │
│  │  (on-demand) │   │  (scheduled, automatic)       │     │
│  │              │   │                                │     │
│  │ POST /runs/* │   │  Cron: run-realtime every N    │     │
│  │ GET  /signals│   │  minutes during market hours   │     │
│  └──────┬───────┘   └──────────┬─────────────────────┘     │
│         │                      │                         │
│         └──────┬───────────────┘                         │
│                ▼                                         │
│  ┌──────────────────────────────┐                       │
│  │  SignalRunExecutionService   │                       │
│  │  (shared orchestration)     │                       │
│  └──────────┬──────────────────┘                       │
│             ▼                                           │
│  ┌──────────────────────────────┐                       │
│  │  SignalPipeline              │                       │
│  │  (domain logic, unchanged)  │                       │
│  └──────────┬──────────────────┘                       │
│             ▼                                           │
│  ┌──────────────────────────────┐                       │
│  │  PostgreSQL                  │                       │
│  │  signal_runs + market_data_* │                       │
│  └──────────────────────────────┘                       │
└─────────────────────────────────────────────────────────┘
```

Two producers, one consumer path:
- **Worker** generates signals automatically on a schedule (replaces the cron + CLI pattern).
- **API** generates signals on demand via `POST` endpoints when a user or UI requests it.
- **Both** write results to the same PostgreSQL tables via the same `SignalRunExecutionService`.
- **Read endpoints** (`GET /api/signals`) query the DB, not files.

## Python Reference Behavior

The Python reference has no HTTP API, so the transport change is new behavior. The behavioral requirement to preserve is that API-triggered and worker-triggered runs must produce the same signal results as the existing CLI commands for identical inputs.

Relevant rules from `docs/python-reference.md`:

- `run-realtime`, `run-asof`, and `run-range` generate final signals from earnings gate -> technical scoring -> trade governor.
- Live mode writes live artifacts; simulation modes write isolated simulation artifacts and must never pollute live state or live audit files.
- Every error path defaults to `WAIT`.
- `signals.json` uses plain enum strings; audit JSONL uses prefixed enum strings.
- State updates and audit writes are part of command execution behavior, not optional side effects.

Inference:

- `GET` endpoints must remain read-only.
- Signal generation should be exposed via `POST` endpoints because generation mutates state, writes audit rows, and persists results.

## Current State

- Core signal calculation is reusable in `src/BlowingCandles.Application/SignalPipeline.cs`.
- CLI orchestration and path management live in `src/BlowingCandles.Cli/Handlers/RunCommandSupport.cs` and the command handlers.
- The API only reads `signals.json` from disk and serves it over HTTP.
- Container startup is CLI-oriented; the API is an alternative entrypoint, not the default runtime surface.
- PostgreSQL is already wired for market-data snapshots (`market_data_*` tables). The persistence infrastructure (EF Core, Testcontainers, health checks, DI) is mature and reusable.

## Target State

- The API is the only runtime surface. No CLI for signal generation.
- `GET /api/signals` reads from PostgreSQL, not from `signals.json`.
- `POST /api/runs/*` triggers on-demand signal generation and persists results to PostgreSQL.
- A `BackgroundService` runs scheduled signal generation (replaces cron + CLI).
- Trade governor state moves from `state.json` to PostgreSQL.
- Audit log moves from JSONL files to PostgreSQL.
- File output (`signals.json`, `signals.txt`, audit JSONL) is dropped for consumers but can be retained temporarily as a write-behind for debugging.

## Affected Files

New files:
- `src/BlowingCandles.Application/Services/SignalRunExecutionService.cs`
- `src/BlowingCandles.Application/Services/ISignalRunExecutionService.cs`
- `src/BlowingCandles.Infrastructure/Persistence/Entities/SignalRun*.cs` (run + signal result entities)
- `src/BlowingCandles.Infrastructure/Persistence/Configurations/SignalRun*Configuration.cs`
- `src/BlowingCandles.Infrastructure/Persistence/Services/SignalRunPersistenceService.cs`
- `src/BlowingCandles.Infrastructure/Persistence/Services/SignalRunReadService.cs`
- `src/BlowingCandles.Api/Workers/SignalGenerationWorker.cs`
- EF Core migration for signal run tables

Modified files:
- `src/BlowingCandles.Application/SignalPipeline.cs` — no logic changes, possibly minor interface adjustments
- `src/BlowingCandles.Api/ApiHost.cs` — new endpoints, DI registration, worker registration
- `src/BlowingCandles.Api/Program.cs` — may absorb config loading from CLI
- `src/BlowingCandles.Api/Services/SignalsFileReader.cs` — replaced by DB-backed read service
- `src/BlowingCandles.Infrastructure/Persistence/AppDbContext.cs` — new `DbSet` properties
- `src/BlowingCandles.Infrastructure/Persistence/DependencyInjection.cs` — register new services
- `docker-compose.yml` — API as default service, remove CLI entrypoint
- `docker-entrypoint.sh` — simplify to API-only
- `docs/architecture.md` — updated architecture diagram and description
- `docs/implementation-order.md` — new phase entries

Files that become dead code (can be removed or kept for reference):
- `src/BlowingCandles.Cli/` — entire project, once migration is validated
- `src/BlowingCandles.Api/Services/SignalsFileReader.cs` — replaced by DB reads

## Numbered Plan

### Step 1: Signal Run Persistence Schema

Add PostgreSQL tables for persisting signal pipeline results. This is the foundation — everything else writes to or reads from these tables.

**New tables:**

| Table | Purpose |
|-------|---------|
| `signal_run` | One row per pipeline execution. Tracks run type (realtime/asof/range), trigger (api/worker), status, timestamps, as-of date for simulations. |
| `signal_run_result` | One row per ticker per run. Stores `FinalSignal` fields: ticker, action, news_state, market_action, reason, timestamp. FK to `signal_run`. |
| `signal_run_audit` | One row per audit entry per run. Replaces JSONL audit log. FK to `signal_run`. |
| `trade_governor_state` | Replaces `state.json`. Stores day, buys_today, last_buy_at. Separate rows for live vs simulation. |

**Deliverables:**
- EF Core entities, configurations, and migration
- `SignalRunPersistenceService` — write a complete run with results and audit entries in one transaction
- `SignalRunReadService` — query latest run, run by ID, latest signals per ticker
- `TradeGovernorDbStateStore` implementing `ITradeGovernorStateStore` backed by PostgreSQL
- Testcontainers integration tests for all persistence operations

**Why first:** The DB schema is the contract. API endpoints and worker both depend on it. Building this first means the read and write paths can be tested independently before wiring transport.

### Step 2: Shared Execution Service

Extract orchestration from `RunCommandSupport.cs` and the CLI handlers into a reusable `SignalRunExecutionService` in the Application layer. This service is the single entry point for running the signal pipeline, regardless of trigger source.

**Responsibilities:**
- Accept a run request (run type, as-of date if simulation, trigger source)
- Load config, select clock, resolve paths
- Execute `SignalPipeline`
- Persist results via `SignalRunPersistenceService` (replaces file writes)
- Write audit entries via `SignalRunPersistenceService` (replaces JSONL append)
- Update trade governor state via `TradeGovernorDbStateStore` (replaces `state.json`)
- Return a structured `SignalRunResult` with run ID, signals, and metadata

**Deliverables:**
- `ISignalRunExecutionService` interface in Application
- `SignalRunExecutionService` implementation
- Unit tests with mocked pipeline and persistence
- Integration tests with Testcontainers proving end-to-end DB persistence

**Concurrency:** Use a `SemaphoreSlim(1, 1)` per run type (live vs simulation) inside the execution service. Two overlapping live runs are serialized. A simulation run does not block a live run. This replaces the file-locking approach — DB transactions handle data integrity, the semaphore prevents duplicate executions.

**Why second:** The execution service is the core of the refactor. Once it exists, both the API and the worker are thin callers.

### Step 3: API Endpoints and DB-Backed Reads

Extend the existing API with write endpoints and switch read endpoints from file-backed to DB-backed.

**New endpoints:**

| Method | Path | Purpose |
|--------|------|---------|
| `POST` | `/api/runs/realtime` | Trigger live signal generation |
| `POST` | `/api/runs/asof` | Trigger simulation for a specific date |
| `POST` | `/api/runs/range` | Trigger simulation across a date range |
| `GET` | `/api/signals` | Latest live signals from DB |
| `GET` | `/api/signals/{ticker}` | Latest live signal for a ticker from DB |
| `GET` | `/api/runs` | Run history (last N runs) |
| `GET` | `/api/runs/{id}` | Specific run result |
| `GET` | `/health` | Health check (DB connectivity) |
| `GET` | `/ready` | Readiness check (at least one successful run exists) |

**Request/response contracts:**

`POST /api/runs/realtime` — no body required, returns:
```json
{
  "runId": "uuid",
  "runType": "realtime",
  "trigger": "api",
  "status": "completed",
  "startedAt": "2026-03-09T14:30:00Z",
  "completedAt": "2026-03-09T14:30:02Z",
  "signals": [
    { "ticker": "AAPL", "action": "BUY", "newsState": "TRADE_OK", "marketAction": "BUY", "reason": "", "timestamp": "..." }
  ]
}
```

`POST /api/runs/asof` — body: `{ "asOfDate": "2026-03-07" }`, same response shape.

`POST /api/runs/range` — body: `{ "startDate": "2026-03-01", "endDate": "2026-03-07" }`, returns array of run results.

**HTTP status codes:**

| Status | Meaning |
|--------|---------|
| 200 | Successful read |
| 201 | Run completed successfully |
| 400 | Invalid request (missing date, bad format) |
| 404 | Ticker or run not found |
| 409 | Run already in progress for this run type |
| 500 | Pipeline failure (signals still default to WAIT) |
| 503 | DB unavailable |

**Deliverables:**
- Updated `ApiHost.cs` with new endpoints
- `SignalsFileReader` replaced by `SignalRunReadService` queries
- Health and readiness endpoints
- Integration tests for all endpoints

### Step 4: Background Worker

Add a `BackgroundService` that runs signal generation on a configurable schedule. This replaces the external cron + CLI pattern.

**Implementation:** `SignalGenerationWorker : BackgroundService`
- Reads schedule from configuration (e.g., `worker.interval_minutes: 30`, `worker.enabled: true`)
- Calls `ISignalRunExecutionService.RunRealtime()` with trigger source `"worker"`
- Logs run results
- Respects the same concurrency semaphore as API-triggered runs (an API-triggered run in progress will block the worker, and vice versa)
- Graceful shutdown via `CancellationToken`

**Configuration:**
```yaml
worker:
  enabled: true
  interval_minutes: 30
```

**Deliverables:**
- `SignalGenerationWorker` in `BlowingCandles.Api/Workers/`
- Worker registration in `ApiHost.cs` via `builder.Services.AddHostedService<SignalGenerationWorker>()`
- Configuration binding
- Integration test verifying worker triggers execution service
- Logging for scheduled run start, completion, and skip (if run already in progress)

**Why fourth:** The worker is a thin scheduler around the execution service from Step 2. It's independent of the API endpoints from Step 3, but having both steps done means the full service is functional.

### Step 5: Containerized Service and Cleanup

Make the API the default and only runtime surface. Clean up CLI artifacts.

**Deliverables:**
- `docker-compose.yml` updated: `app` service runs the API by default, no CLI entrypoint
- `docker-entrypoint.sh` simplified to API startup only
- Health check in `docker-compose.yml` using `/health` endpoint
- `Dockerfile` updated: single publish target for API
- CLI project can be kept in the solution for backward compatibility or removed entirely (decision point)
- `docs/architecture.md` updated with new architecture diagram
- `docs/implementation-order.md` updated with new phase entries

**Optional cleanup:**
- Remove `SignalsFileReader` (replaced by DB reads)
- Remove file-write paths from execution service once DB persistence is validated
- Remove `JsonStateStore` once `TradeGovernorDbStateStore` is proven

## Risks

| Risk | Mitigation |
|------|------------|
| Overlapping runs corrupt state | DB transactions + `SemaphoreSlim` concurrency guard in execution service |
| `POST /api/runs/range` too slow for HTTP | Return 202 Accepted with run ID; poll `GET /api/runs/{id}` for results. Or keep synchronous for small ranges and add async job model later. |
| Worker and API compete for pipeline execution | Same semaphore — worker waits if API run is in progress, returns 409 if reversed |
| DB migration breaks existing market_data tables | Separate migration for signal run tables; no ALTER on existing tables |
| Losing audit trail during transition | Keep JSONL write-behind as a secondary output during transition; remove once DB audit is validated |
| No authentication on mutation endpoints | Acceptable for local/operator use. Add auth as a follow-up if the service is exposed beyond localhost. |
| Trade governor state migration from file to DB | One-time migration script or accept state reset (buy counter resets to 0 on cutover) |

## Required vs Optional

### Required (this plan)

- Signal run persistence schema (Step 1)
- Shared execution service with DB persistence (Step 2)
- API write endpoints + DB-backed reads (Step 3)
- Background worker for scheduled runs (Step 4)
- Containerized service default (Step 5)

### Optional / Follow-up

- Authentication and authorization
- WebSocket or SSE push for live signal updates
- Async job model for long-running range requests (202 + polling)
- Run retry endpoints
- Signal diff / change detection between runs
- Market hours awareness in worker schedule (skip weekends/holidays)
- Prometheus metrics endpoint
- Rate limiting on POST endpoints

## Validation

1. API-triggered and worker-triggered realtime runs produce identical signals for the same market data inputs.
2. `GET /api/signals` returns the same data as the last successful run's persisted results.
3. Simulation endpoints preserve live-vs-simulation isolation — simulation runs never affect live signal reads or live governor state.
4. A concurrent second run request returns HTTP 409, not corrupted data.
5. Failures degrade to all-WAIT signals, never to missing or partial results.
6. Worker runs are logged and visible in run history via `GET /api/runs`.
7. Health and readiness endpoints reflect actual service state.
8. Existing cross-validation golden tests still pass when run through the execution service (same pipeline, same inputs, same outputs).

## Decision Points

| Decision | When | Options |
|----------|------|---------|
| Keep or remove CLI project | Step 5 | Keep for debugging / remove for simplicity |
| Synchronous vs async range endpoint | Step 3 | Sync first, add 202+polling if ranges are slow |
| File write-behind during transition | Step 2 | Keep temporarily for debugging / drop immediately |
| Trade governor state migration | Step 1 | Migration script / accept reset |
| Worker market-hours awareness | Step 4 | Simple interval first / add market calendar later |

## Files Changed

- `docs/features/api-first-execution-plan.md`
