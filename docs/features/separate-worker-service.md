# Feature: Separate Worker into its Own Container Service

## Summary

Extract the background worker (`SignalGenerationWorker`) from the API process into a dedicated container service. Currently the worker runs in-process inside the API host, registered as a `BackgroundService` when `Worker__Enabled=true` (see `ApiHost.cs:45-48`). This couples the worker lifecycle to the API — if one crashes, both go down. Separating them into independent containers improves fault isolation, independent scaling, and operational visibility (each service appears separately in Portainer).

## Current State

The worker is embedded in the API via:

1. **`WorkerOptions`** (`src/BlowingCandles.Api/WorkerOptions.cs`) — reads `Worker:Enabled` and `Worker:IntervalMinutes` from configuration.
2. **`ApiHost.cs:40-48`** — reads `WorkerOptions` from config; if `Enabled == true`, registers `SignalGenerationWorker` as a hosted service.
3. **`SignalGenerationWorker`** (`src/BlowingCandles.Api/Workers/SignalGenerationWorker.cs`) — extends `BackgroundService`, loops on `IntervalMinutes`, calls `ISignalRunExecutionService.RunRealtime("worker")` each cycle.
4. **`docker-compose.yml`** — single `app` service with `Worker__Enabled=true` and `Worker__IntervalMinutes=60` env vars.

The API process hosts both HTTP endpoints and the background loop in the same process.

## Acceptance Criteria

- [ ] A new `worker` service defined in `docker-compose.yml` using the same `blowing-candles` image
- [ ] The `worker` service runs the API host with worker enabled and API endpoints not exposed externally (no published ports)
- [ ] The `app` service runs with `Worker__Enabled=false` (API only, no background worker)
- [ ] Both `app` and `worker` depend on `postgres` healthy
- [ ] Both `app` and `worker` have `restart: always`
- [ ] Worker and API can crash/restart independently without affecting each other
- [ ] No C# code changes required — separation achieved purely through configuration and docker-compose

## Inputs

| Input | Source | Format | Required |
|-------|--------|--------|----------|
| Existing `docker-compose.yml` | Repository root | Docker Compose v3 | Yes |
| Existing `docker-entrypoint.sh` | Repository root | Shell script | Yes (no changes, reference only) |

## Outputs

| Output | Format | Destination |
|--------|--------|-------------|
| Updated `docker-compose.yml` | Docker Compose | Repository root |

## Configuration

### docker-compose.yml — target state

```yaml
services:
  app:
    build: .
    image: blowing-candles
    restart: always
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      - ConnectionStrings__AppDb=Host=postgres;Port=5432;Database=blowing_candles;Username=postgres;Password=postgres
      - Worker__Enabled=false
    ports:
      - "5000:5000"
    volumes:
      - ./config.yaml:/app/config.yaml:ro
      - ./earnings_calendar.json:/app/earnings_calendar.json:ro

  worker:
    image: blowing-candles
    restart: always
    depends_on:
      app:
        condition: service_started
      postgres:
        condition: service_healthy
    environment:
      - ConnectionStrings__AppDb=Host=postgres;Port=5432;Database=blowing_candles;Username=postgres;Password=postgres
      - Worker__Enabled=true
      - Worker__IntervalMinutes=60
    volumes:
      - ./config.yaml:/app/config.yaml:ro
      - ./earnings_calendar.json:/app/earnings_calendar.json:ro

  postgres:
    image: postgres:16-alpine
    restart: always
    environment:
      POSTGRES_DB: blowing_candles
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d blowing_candles"]
      interval: 5s
      timeout: 3s
      retries: 5

volumes:
  pgdata:
```

### Key decisions

- **No port published on `worker`**: The worker service runs the full API host (needed for DI wiring and health checks) but does not expose port 5000 externally. This prevents conflicts with the `app` service.
- **`worker` depends on `app: service_started`**: Ensures the image is built by the `app` service before the `worker` tries to use it (since only `app` has `build: .`).
- **Same image, different config**: Both services use the `blowing-candles` image. The only difference is the `Worker__Enabled` env var — `false` on `app`, `true` on `worker`.
- **No C# changes**: The existing `ApiHost.cs` conditional registration (`if (workerOptions.Enabled)`) already supports this pattern. Setting `Worker__Enabled=false` on the API means no `SignalGenerationWorker` is registered.

## Domain Rules

- The worker container still starts the full API host (it's a .NET `WebApplication`). It just doesn't publish its HTTP port. This is acceptable because the worker needs the same DI container (db context, config, services).
- The worker's `RunRealtime("worker")` trigger tag distinguishes worker-initiated runs from API-initiated runs (`"api"`) in the database.

## Error Handling

- If the worker crashes, `restart: always` brings it back. The API remains unaffected.
- If the API crashes, `restart: always` brings it back. The worker remains unaffected.
- If postgres crashes, both services will fail their DB calls but will retry on the next cycle/request once postgres recovers.

## Dependencies

- Depends on the portainer feature for visibility (but not functionally).
- Shares the `blowing-candles` image — the `app` service must build first.

## Test Scenarios

| Scenario | Input | Expected Output |
|----------|-------|-----------------|
| API starts without worker | `docker compose up app` | API serves on port 5000; no worker log messages |
| Worker starts independently | `docker compose up worker` | Worker logs "started with interval 60 minutes"; runs `RunRealtime` each cycle |
| Worker crash doesn't affect API | Kill worker container | API still serves requests; worker restarts automatically |
| API crash doesn't affect worker | Kill app container | Worker continues running; API restarts automatically |
| Both use same DB | `docker compose up` | Both connect to postgres; worker writes runs, API reads them |
| No port conflict | `docker compose up` | Only port 5000 from `app` is published; worker has no port conflict |
| Image build order | `docker compose up --build` | `app` builds the image; `worker` reuses it |

## What NOT to Change

- No changes to `SignalGenerationWorker.cs`, `WorkerOptions.cs`, or `ApiHost.cs` — the conditional registration already supports this.
- No changes to `Dockerfile` or `docker-entrypoint.sh` — both services use the default `api` entrypoint.
- No changes to domain logic, persistence, or API endpoints.

## Implementation Notes

This is a docker-compose-only change. The .NET code already supports running with or without the worker via configuration. The separation is achieved entirely by running two containers from the same image with different environment variables.
