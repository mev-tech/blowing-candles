# Feature: API Endpoints and DB-Backed Reads

## Summary

Extend the REST API with write endpoints that trigger signal pipeline runs (`POST /api/runs/realtime`, `POST /api/runs/asof`, `POST /api/runs/range`) and switch existing read endpoints from file-backed (`SignalsFileReader`) to DB-backed (`SignalRunReadService`). Add run history endpoints (`GET /api/runs`, `GET /api/runs/{id}`) and health/readiness probes (`GET /health/live`, `GET /health/ready`).

## Purpose

Transform the API from a passive file reader into an active service that can trigger signal generation and serve results from PostgreSQL. This eliminates the dependency on `signals.json` for reads and enables API-triggered pipeline execution alongside CLI and future worker triggers.

## Inputs

| Input | Source | Format | Required |
|-------|--------|--------|----------|
| `ISignalRunExecutionService` | DI (Application layer) | Interface | Yes |
| `SignalRunReadService` | DI (Infrastructure layer) | Class | Yes |
| `AppDbContext` | DI (Infrastructure layer) | EF Core DbContext | Yes |
| `AppConfig` | `config.yaml` via `YamlConfigLoader` | Strongly-typed record | Yes |
| `POST /api/runs/asof` request body | HTTP JSON | `{ "asOfDate": "2026-03-01" }` | Yes (for as-of) |
| `POST /api/runs/range` request body | HTTP JSON | `{ "startDate": "2026-03-01", "endDate": "2026-03-05" }` | Yes (for range) |

## Outputs

| Output | Format | Destination |
|--------|--------|-------------|
| Latest signals | JSON array of signal objects | `GET /api/signals` response body |
| Single ticker signal | JSON signal object or 404 | `GET /api/signals/{ticker}` response body |
| Run history list | JSON array of run summary objects | `GET /api/runs` response body |
| Single run detail | JSON run object with signals or 404 | `GET /api/runs/{id}` response body |
| Triggered run result | JSON run result object (202 Accepted) | `POST /api/runs/*` response body |
| Health status | HTTP 200 or 503 | `GET /health/live`, `GET /health/ready` |

## Configuration

- `config.yaml` — provides watchlist, earnings calendar path, policy, output paths (still used for write-behind)
- `ConnectionStrings:AppDb` — PostgreSQL connection string for `AppDbContext`
- No new configuration fields required; existing `PersistenceOptions` and `AppConfig` are sufficient

## Edge Cases

1. **No completed runs in DB** — `GET /api/signals` returns HTTP 200 with empty array `[]`; `GET /api/signals/{ticker}` returns HTTP 404
2. **Run ID not found** — `GET /api/runs/{id}` returns HTTP 404 with `{ "error": "Run not found" }`
3. **Invalid as-of date** — `POST /api/runs/asof` with missing or unparseable `asOfDate` returns HTTP 400
4. **Invalid date range** — `POST /api/runs/range` with `startDate > endDate` returns HTTP 400
5. **Pipeline failure** — `POST /api/runs/*` still returns HTTP 202 with the failed run result (status `Failed`, error message populated)
6. **Concurrent run requests** — serialized by `SignalRunExecutionService` per-mode semaphore; second request blocks until first completes
7. **Database unavailable** — `GET /health/ready` returns HTTP 503; read endpoints return HTTP 503
8. **Non-numeric run ID** — `GET /api/runs/{id}` returns HTTP 400 or 404 depending on route constraint

## Implementation Notes

### DI Wiring

- Register `AddPersistence()` in `ApiHost.Build()` to wire `AppDbContext`, `SignalRunReadService`, `SignalRunPersistenceService`, and `TradeGovernorDbStateStoreFactory`
- Construct and register `ISignalRunExecutionService` (`SignalRunExecutionService`) with all dependencies: `AppConfig`, `IMarketDataProvider`, `IEarningsCalendar`, `SignalRunPersistenceService`, `TradeGovernorDbStateStoreFactory`, `JsonlAuditWriter`, `OutputRenderer`
- The API project gains a reference to `BlowingCandles.Infrastructure` for persistence DI and `BlowingCandles.Application` for the execution service

### Read Endpoint Migration

- `GET /api/signals` switches from `SignalsFileReader` to `SignalRunReadService.GetLatestLiveRun()`
- Map `SignalRunSignalResult` to the existing `SignalFileEntry` JSON shape for backward compatibility (camelCase, plain enum strings)
- `GET /api/signals/{ticker}` uses the same DB-backed latest run, filters by ticker (case-insensitive)
- `SignalsFileReader` remains in the codebase but is no longer wired into endpoints (removal deferred to Step 5)

### Write Endpoints

- `POST /api/runs/realtime` — calls `ISignalRunExecutionService.RunRealtime("api")`, returns HTTP 202 with `SignalRunExecutionResult` serialized as JSON
- `POST /api/runs/asof` — parses `{ "asOfDate": "YYYY-MM-DD" }` from request body, calls `RunAsOf("api", asOfDate)`, returns HTTP 202
- `POST /api/runs/range` — parses `{ "startDate": "YYYY-MM-DD", "endDate": "YYYY-MM-DD" }` from request body, calls `RunRange("api", startDate, endDate)`, returns HTTP 202 with array of results
- Trigger source is always `"api"` for API-initiated runs
- Write endpoints are synchronous (block until pipeline completes) despite HTTP 202 status — this matches the current execution model and avoids premature async complexity

### Health Endpoints

- `GET /health/live` — always returns HTTP 200 `{ "status": "Healthy" }` (liveness: process is running)
- `GET /health/ready` — checks PostgreSQL connectivity via `PostgreSqlHealthCheck`, returns HTTP 200 if healthy, HTTP 503 if unhealthy (readiness: can serve DB-backed requests)

### JSON Serialization

- All endpoints use `SignalsJsonSerializer.JsonOptions` for consistent camelCase serialization with plain enum strings
- Run result responses include: `runId`, `runType`, `trigger`, `status`, `asOfDate`, `isSimulation`, `startedAtUtc`, `completedAtUtc`, `tickerCount`, `signals`, `errorMessage`

### What NOT to Build

- No authentication or authorization
- No async pipeline execution (runs block the HTTP request)
- No WebSocket or SSE for run progress
- No pagination for `GET /api/runs` (capped at 100 by `SignalRunReadService`)
- No removal of `SignalsFileReader` (deferred to Step 5)

## Test Scenarios

| Scenario | Input | Expected Output |
|----------|-------|-----------------|
| GET /api/signals — DB has completed live run with signals | DB seeded with run | HTTP 200, JSON array matching seeded signals in ticker order |
| GET /api/signals — no completed runs | Empty DB | HTTP 200, empty array `[]` |
| GET /api/signals/{ticker} — ticker exists | DB seeded, ticker = "AAPL" | HTTP 200, single signal JSON object |
| GET /api/signals/{ticker} — case insensitive | DB seeded with "AAPL", request "aapl" | HTTP 200, single signal JSON object |
| GET /api/signals/{ticker} — ticker not found | DB seeded, ticker = "ZZZZ" | HTTP 404, `{ "error": "Ticker not found", "ticker": "ZZZZ" }` |
| GET /api/runs — runs exist | DB seeded with multiple runs | HTTP 200, JSON array ordered by startedAtUtc descending |
| GET /api/runs — no runs | Empty DB | HTTP 200, empty array `[]` |
| GET /api/runs/{id} — run exists | DB seeded with run ID 1 | HTTP 200, full run JSON with signals |
| GET /api/runs/{id} — run not found | Request ID 999 | HTTP 404, `{ "error": "Run not found" }` |
| POST /api/runs/realtime — success | No body needed | HTTP 202, run result JSON with status Completed |
| POST /api/runs/asof — success | `{ "asOfDate": "2026-03-01" }` | HTTP 202, run result JSON with status Completed |
| POST /api/runs/asof — missing date | `{}` or no body | HTTP 400 |
| POST /api/runs/range — success | `{ "startDate": "2026-03-01", "endDate": "2026-03-03" }` | HTTP 202, array of run results |
| POST /api/runs/range — invalid range | `startDate > endDate` | HTTP 400 |
| GET /health/live | None | HTTP 200, `{ "status": "Healthy" }` |
| GET /health/ready — DB available | PostgreSQL running | HTTP 200, `{ "status": "Healthy" }` |
| GET /health/ready — DB unavailable | No PostgreSQL | HTTP 503, `{ "status": "Unhealthy" }` |
| Enum serialization | Run with BUY signal | JSON contains `"BUY"` not `"Action.BUY"` |
