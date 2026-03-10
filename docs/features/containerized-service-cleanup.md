# Feature: Containerized Service and Cleanup

## Purpose

Make the API the default and only runtime surface in Docker. Remove legacy file-backed code (`SignalsFileReader`, `JsonStateStore`, file write-behind in `SignalRunExecutionService`) that has been superseded by PostgreSQL-backed equivalents. Update `Dockerfile`, `docker-compose.yml`, and `docker-entrypoint.sh` so the container runs the API by default instead of requiring an explicit `api` subcommand. The CLI project remains in the solution for local development and one-off commands but is no longer the primary Docker entrypoint.

## Inputs

| Input | Source | Format | Required |
|-------|--------|--------|----------|
| Existing `Dockerfile` | Repository root | Multi-stage Dockerfile | Yes |
| Existing `docker-compose.yml` | Repository root | Docker Compose v3 | Yes |
| Existing `docker-entrypoint.sh` | Repository root | Shell script | Yes |
| `SignalsFileReader.cs` | `src/BlowingCandles.Api/Services/` | C# class | Yes (to delete) |
| `JsonStateStore.cs` | `src/BlowingCandles.Infrastructure/State/` | C# class | Yes (to delete) |
| `OutputRenderer.cs` | `src/BlowingCandles.Application/` | C# class | Yes (to remove file write-behind) |
| `SignalRunExecutionService.cs` | `src/BlowingCandles.Application/Services/` | C# class | Yes (to remove file output calls) |

## Outputs

| Output | Format | Description |
|--------|--------|-------------|
| Updated `Dockerfile` | Dockerfile | API as default entrypoint; CLI available via override |
| Updated `docker-compose.yml` | Docker Compose | `app` service runs API by default with worker enabled |
| Updated `docker-entrypoint.sh` | Shell script | Default command is API; CLI commands still supported |
| Deleted `SignalsFileReader.cs` | — | Removed from codebase |
| Deleted `JsonStateStore.cs` | — | Removed from codebase |
| Deleted `JsonStateStoreTests.cs` | — | Removed from codebase |
| Updated `SignalRunExecutionService` | C# | File write-behind (`OutputRenderer.WriteSignals`, `JsonlAuditWriter`) removed |
| Updated `OutputRenderer` | C# | `WriteSignals` method removed; class retained only if other methods remain, otherwise deleted |
| Updated tests | C# | Tests referencing removed code updated or removed |

## Configuration

### docker-compose.yml

```yaml
services:
  app:
    build: .
    image: blowing-candles
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      - ConnectionStrings__AppDb=Host=postgres;Port=5432;Database=blowing_candles;Username=postgres;Password=postgres
      - Worker__Enabled=true
      - Worker__IntervalMinutes=60
    ports:
      - "5000:5000"
    volumes:
      - ./config.yaml:/app/config.yaml:ro
      - ./earnings_calendar.json:/app/earnings_calendar.json:ro
```

Key changes from current:
- `Worker__Enabled=true` enables the background worker by default in Docker
- `ports: "5000:5000"` exposes the API port
- `data` and `logs` volume mounts removed (no more file-backed state/audit/output)

### Dockerfile

- Default entrypoint runs the API (no `api` subcommand needed)
- CLI commands available via `docker run blowing-candles cli <command>`
- `HEALTHCHECK` uses `curl` or `wget` against `/health/live` instead of `--help`
- Labels updated to reflect API service

### docker-entrypoint.sh

- Default (no arguments) starts the API on `0.0.0.0:5000`
- `cli` subcommand delegates to `BlowingCandles.Cli.dll`
- Legacy CLI commands (`check-calendar`, `run-realtime`, etc.) still work as shortcuts

## Edge Cases

1. **Existing deployments using CLI entrypoint** — The `cli` subcommand provides backward compatibility. Users running `docker run blowing-candles run-realtime` will still work via the entrypoint script's command matching. Document the migration in the PR description.

2. **File write-behind removal breaks cross-validation tests** — `GoldenOutputTests` read from `signals.txt` and `signals.json` files written by `OutputRenderer`. These tests must be updated to validate pipeline output in-memory rather than from files, or the tests must be marked as CLI-only and use a separate code path.

3. **JsonStateStore still used by CLI handlers** — Verify that CLI handlers have been fully migrated to `ISignalRunExecutionService` (which uses `TradeGovernorDbStateStore`). If any CLI path still depends on `JsonStateStore`, keep it until that path is migrated.

4. **OutputRenderer used outside write-behind** — Check if `OutputRenderer` is used by CLI handlers directly. If so, keep the class but remove the file-writing method only.

5. **Audit JSONL file writes** — `SignalRunExecutionService` writes audit JSONL as a side effect. If audit is now fully captured in PostgreSQL `signal_run_result` rows, remove the JSONL write-behind. If not, keep it as an optional file output behind configuration.

6. **Docker healthcheck without curl** — The `aspnet:8.0` base image may not include `curl`. Use `dotnet` or a custom health binary, or install `curl` in the Dockerfile.

## Implementation Notes

### 1. Update docker-entrypoint.sh

```sh
#!/bin/sh
set -eu

case "${1:-api}" in
    api)
        shift 2>/dev/null || true
        if [ "${ASPNETCORE_URLS:-}" = "" ]; then
            set -- --urls http://0.0.0.0:5000 "$@"
        fi
        exec dotnet /app/api/BlowingCandles.Api.dll "$@"
        ;;
    cli)
        shift
        exec dotnet /app/BlowingCandles.Cli.dll "$@"
        ;;
    check-calendar|stats-periods|run-realtime|run-asof|run-range|--help|-h|help)
        exec dotnet /app/BlowingCandles.Cli.dll "$@"
        ;;
    *)
        exec "$@"
        ;;
esac
```

### 2. Update Dockerfile

- Change `HEALTHCHECK` from `--help` to a proper HTTP health check
- Update `LABEL description` to reflect API service
- Keep both CLI and API publish stages

### 3. Update docker-compose.yml

- Add `ports: ["5000:5000"]`
- Add `Worker__Enabled=true` and `Worker__IntervalMinutes=60` environment variables
- Remove `data` and `logs` volume mounts
- Keep `config.yaml` and `earnings_calendar.json` mounts (still needed by pipeline)

### 4. Delete SignalsFileReader

- Delete `src/BlowingCandles.Api/Services/SignalsFileReader.cs`
- Remove any references in `ApiHost.cs` or DI wiring
- Remove any test files that test `SignalsFileReader` directly

### 5. Remove file write-behind from SignalRunExecutionService

- Remove `OutputRenderer` dependency and `WriteSignals` call
- Remove `JsonlAuditWriter` dependency and audit file writes
- Remove path-building helpers (`ResolveAuditPath`, `BuildSimulationAuditPath`, `BuildSimulationOutputPath`)
- Simplify constructor parameters (remove `outputRenderer`, `auditWriterFactory`, `configPath`)
- Update all callers (CLI handlers, API DI wiring, tests)

### 6. Delete JsonStateStore

- Delete `src/BlowingCandles.Infrastructure/State/JsonStateStore.cs`
- Delete `tests/BlowingCandles.Infrastructure.Tests/JsonStateStoreTests.cs`
- Remove `ITradeGovernorStateStore` implementation registration if `JsonStateStore` was registered anywhere
- Verify no CLI handler references remain

### 7. Update cross-validation tests

- `GoldenOutputTests` currently write to temp files via the pipeline. Update them to capture output in-memory or to use the execution service's return value for validation instead of reading files from disk.

### What NOT to Change

- Do not remove the CLI project — it remains for local development and one-off commands
- Do not remove `OutputRenderer` if it is still used by cross-validation or other test infrastructure
- Do not remove `JsonlAuditWriter` if audit JSONL is still considered a useful file artifact (decision: check if audit data is fully captured in DB)
- Do not change any domain logic, pipeline behavior, or API endpoints
- Do not modify PostgreSQL schema or persistence services

## Test Scenarios

| Scenario | Input | Expected Output |
|----------|-------|-----------------|
| Docker default command starts API | `docker run blowing-candles` (no args) | API starts on port 5000, `/health/live` returns 200 |
| Docker CLI subcommand works | `docker run blowing-candles cli check-calendar` | CLI executes `check-calendar` command |
| Docker legacy CLI shortcuts work | `docker run blowing-candles run-realtime` | CLI executes `run-realtime` command |
| Worker enabled in docker-compose | `docker compose up` | Worker starts and logs interval; API serves requests |
| Health check passes | Container running | `HEALTHCHECK` succeeds via `/health/live` |
| SignalsFileReader removed | Build solution | No compilation errors referencing `SignalsFileReader` |
| JsonStateStore removed | Build solution | No compilation errors referencing `JsonStateStore` |
| File write-behind removed | Execute pipeline via API | No `signals.txt`, `signals.json`, or audit JSONL files created |
| Cross-validation tests pass | `dotnet test` | All golden output tests pass without file write-behind |
| API endpoints unchanged | `GET /api/signals`, `POST /api/runs/realtime` | Same HTTP responses as before cleanup |
| Execution service works without file output | `POST /api/runs/realtime` | Run persisted to PostgreSQL; no file artifacts |
