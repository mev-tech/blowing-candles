# Fix Specification: update-docs-readme-api-first

## Summary of Issues

The README was written when the app was CLI-only and file-backed. After the API-first migration (Steps 1–5), it is significantly outdated. The `config.yaml` file also contains dead sections, and `AppConfig.cs` / `YamlConfigLoader.cs` still parse config fields that are no longer consumed.

1. **README has no mention of the REST API.** The app's default entrypoint is now the API (`docker compose up`), but the README only shows CLI usage via `docker compose run`.

2. **README has no mention of PostgreSQL.** PostgreSQL is a hard dependency; it's not listed in prerequisites or shown in any usage examples.

3. **README has no mention of the background worker.** `Worker__Enabled` and `Worker__IntervalMinutes` are not documented.

4. **README Configuration section lists dead `output` and `state` blocks.** `OutputConfig` (text_file, json_file) and `StateConfig` (path) are parsed by `YamlConfigLoader` but never consumed by any execution code. `OutputRenderer` and `JsonStateStore` were deleted in Step 5.

5. **README "Output Files" section references deleted file artifacts.** `signals.txt`, `signals.json`, `data/state.json`, `asof_*.signals.*`, `data/sim_state.json` are no longer written. Signal results are now persisted to PostgreSQL.

6. **README Docker Quick Start only shows CLI commands.** The default entrypoint is now the API; `docker compose up` is the primary usage pattern.

7. **README Docker (Manual) section has stale volume mounts.** References `-v "$PWD/data:/app/data"` and `-v "$PWD/logs:/app/logs"` which are no longer needed for core functionality.

8. **README Project Structure missing `BlowingCandles.Api`.** Neither `src/BlowingCandles.Api/` nor `tests/BlowingCandles.Api.Tests/` are listed.

9. **README Running Tests missing Api tests.** `dotnet test tests/BlowingCandles.Api.Tests` is not listed.

10. **`config.yaml` contains dead `output` and `state` sections.** These are parsed but never consumed.

11. **`AppConfig.cs` defines dead `OutputConfig` and `StateConfig` records.** These records and their properties are not referenced outside of config parsing.

12. **`YamlConfigLoader.cs` parses dead `output` and `state` config sections.** The parsing logic for these sections has no consumers.

## Required Changes

### 1. Update README intro paragraph

File: `README.md`, lines 1–6.

Replace:
```
# BlowingCandles

Manual trading signal generator for a small equity watchlist.
Combines earnings proximity gating, technical analysis scoring, and policy rules
to produce BUY, SELL, or WAIT signals. The operator reads the output and executes
trades manually - there is no order execution logic.
```
With:
```
# BlowingCandles

Manual trading signal generator for a small equity watchlist.
Combines earnings proximity gating, technical analysis scoring, and policy rules
to produce BUY, SELL, or WAIT signals. Runs as an API service (default) with an
optional CLI. The operator reads the signals and executes trades manually - there
is no order execution logic.
```

### 2. Update Prerequisites

File: `README.md`, lines 8–12.

Replace:
```
## Prerequisites

Two options:

- **Docker** (recommended) - Docker 20.10+ with Compose V2
- **Local** - .NET 8 SDK
```
With:
```
## Prerequisites

Two options:

- **Docker** (recommended) - Docker 20.10+ with Compose V2 (includes PostgreSQL via docker-compose)
- **Local** - .NET 8 SDK + PostgreSQL 16+
```

### 3. Rewrite Quick Start (Docker) — API-first with CLI subsection

File: `README.md`, lines 14–38.

Replace:
```
## Quick Start (Docker)

\```bash
# Build
docker compose build

# Check earnings calendar health
docker compose run --rm app check-calendar

# Generate live signals for today
docker compose run --rm app run-realtime

# Simulate signals for a specific date
docker compose run --rm app run-asof 2026-01-15

# Simulate a date range
docker compose run --rm app run-range 2026-01-01 2026-01-31

# Analyze holding periods from audit logs
docker compose run --rm app stats-periods

# Show help
docker compose run --rm app --help
\```
```
With:
```
## Quick Start (Docker)

\```bash
# Build and start the API service (+ PostgreSQL)
docker compose up -d

# Check the API is running
curl http://localhost:5000/health/ready

# Get latest signals
curl http://localhost:5000/api/signals

# Trigger a realtime signal run
curl -X POST http://localhost:5000/api/runs/realtime

# Stop
docker compose down
\```

### CLI via Docker

\```bash
# CLI commands use the "cli" subcommand (or legacy shortcut names)
docker compose run --rm app cli check-calendar
docker compose run --rm app cli run-realtime
docker compose run --rm app cli run-asof 2026-01-15
docker compose run --rm app cli run-range 2026-01-01 2026-01-31
docker compose run --rm app cli stats-periods

# Legacy shortcuts also work (without "cli" prefix)
docker compose run --rm app check-calendar
\```
```

### 4. Add API Endpoints section

File: `README.md`. Insert new section after the "Commands" section (after line 64).

```
## API Endpoints

The API listens on port 5000 by default.

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/signals` | Latest signal run results for all tickers |
| `GET` | `/api/signals/{ticker}` | Latest signal for a specific ticker |
| `GET` | `/api/runs` | List all signal runs |
| `GET` | `/api/runs/{id}` | Details of a specific signal run |
| `POST` | `/api/runs/realtime` | Trigger live signal generation |
| `POST` | `/api/runs/asof` | Trigger as-of simulation (body: `{ "asOfDate": "yyyy-MM-dd" }`) |
| `POST` | `/api/runs/range` | Trigger range simulation (body: `{ "startDate": "yyyy-MM-dd", "endDate": "yyyy-MM-dd" }`) |
| `GET` | `/health/live` | Liveness probe (always 200) |
| `GET` | `/health/ready` | Readiness probe (checks PostgreSQL connectivity) |

## Background Worker

When enabled, a background worker runs signal generation on a recurring interval.

| Environment Variable | Default | Description |
|---------------------|---------|-------------|
| `Worker__Enabled` | `false` | Enable the background worker |
| `Worker__IntervalMinutes` | `60` | Minutes between signal runs |

The worker is enabled by default in `docker-compose.yml`.
```

### 5. Update Configuration section — remove dead config

File: `README.md`, lines 66–108.

Remove the `output` and `state` blocks from the example YAML and their rows from the config table. Add a note about infrastructure config (`appsettings.json` / environment variables).

Replace the config YAML example with:
```yaml
watchlist:
  - AAPL
  - MSFT
  - NVDA
  - TSLA

news:
  local_earnings_calendar: earnings_calendar.json
  block_window_hours: 48

policy:
  max_buys_per_day: 99999999
  cooldown_minutes: 0

audit:
  jsonl_path: logs/decisions.jsonl
```

Remove these rows from the config table:
- `output` | `text_file`
- `output` | `json_file`
- `state` | `path`

Add after the config table:
```
### Infrastructure Configuration

Database and worker settings are configured via `appsettings.json` or environment variables:

| Variable | Default | Description |
|----------|---------|-------------|
| `ConnectionStrings__AppDb` | *(see appsettings.json)* | PostgreSQL connection string |
| `Worker__Enabled` | `false` | Enable background signal generation |
| `Worker__IntervalMinutes` | `60` | Interval between worker runs |
```

### 6. Replace "Output Files" section

File: `README.md`, lines 110–127.

Replace the entire "Output Files" section with:
```
## Data Storage

Signal results, run metadata, and trade governor state are persisted to PostgreSQL.

| Table | Description |
|-------|-------------|
| `signal_run` | Pipeline run metadata (type, status, timestamps) |
| `signal_run_result` | Individual signals per run (ticker, action, news state) |
| `trade_governor_state` | Buy count and cooldown tracking per mode |
| `market_data_snapshot` | Cached market data from Yahoo Finance |

The JSONL audit log (`logs/decisions.jsonl`) is still written when configured.
```

### 7. Update Docker (Manual) section

File: `README.md`, lines 128–143.

Replace:
```
## Docker (Manual)

For users who don't want docker-compose:

\```bash
docker build -t blowing-candles .

docker run --rm \
  -v "$PWD/config.yaml:/app/config.yaml:ro" \
  -v "$PWD/earnings_calendar.json:/app/earnings_calendar.json:ro" \
  -v "$PWD/data:/app/data" \
  -v "$PWD/logs:/app/logs" \
  blowing-candles check-calendar
\```

The Dockerfile runs all tests during the build (test stage). If tests fail, the build fails.
```
With:
```
## Docker (Manual)

For users who don't want docker-compose (requires a running PostgreSQL instance):

\```bash
docker build -t blowing-candles .

# Run API (default)
docker run --rm -p 5000:5000 \
  -e ConnectionStrings__AppDb="Host=host.docker.internal;Port=5432;Database=blowing_candles;Username=postgres;Password=postgres" \
  -v "$PWD/config.yaml:/app/config.yaml:ro" \
  -v "$PWD/earnings_calendar.json:/app/earnings_calendar.json:ro" \
  blowing-candles

# Run CLI command
docker run --rm \
  -e ConnectionStrings__AppDb="Host=host.docker.internal;Port=5432;Database=blowing_candles;Username=postgres;Password=postgres" \
  -v "$PWD/config.yaml:/app/config.yaml:ro" \
  -v "$PWD/earnings_calendar.json:/app/earnings_calendar.json:ro" \
  blowing-candles cli check-calendar
\```

The Dockerfile runs all tests during the build (test stage). If tests fail, the build fails.
```

### 8. Update Running Tests

File: `README.md`, lines 145–156.

Add the Api tests line:
```bash
dotnet test tests/BlowingCandles.Api.Tests
```

### 9. Update Project Structure

File: `README.md`, lines 158–176.

Replace:
```
├── src/
│   ├── BlowingCandles.Domain/          # Core domain logic (no dependencies)
│   ├── BlowingCandles.Application/     # Application services
│   ├── BlowingCandles.Infrastructure/  # I/O, config, external APIs
│   └── BlowingCandles.Cli/             # Entry point and command handlers
├── tests/
│   ├── BlowingCandles.Domain.Tests/
│   ├── BlowingCandles.Application.Tests/
│   ├── BlowingCandles.Infrastructure.Tests/
│   └── BlowingCandles.CrossValidation.Tests/
```
With:
```
├── src/
│   ├── BlowingCandles.Domain/          # Core domain logic (no dependencies)
│   ├── BlowingCandles.Application/     # Application services and orchestration
│   ├── BlowingCandles.Infrastructure/  # I/O, config, persistence, external APIs
│   ├── BlowingCandles.Api/             # ASP.NET Core Web API (default entrypoint)
│   └── BlowingCandles.Cli/             # CLI command handlers
├── tests/
│   ├── BlowingCandles.Domain.Tests/
│   ├── BlowingCandles.Application.Tests/
│   ├── BlowingCandles.Infrastructure.Tests/
│   ├── BlowingCandles.Api.Tests/
│   └── BlowingCandles.CrossValidation.Tests/
```

### 10. Remove dead `output` and `state` from `config.yaml`

File: `config.yaml`, lines 15–19.

Remove:
```yaml
output:
  text_file: signals.txt
  json_file: signals.json

state:
  path: data/state.json
```

### 11. Remove dead `OutputConfig` and `StateConfig` from `AppConfig.cs`

File: `src/BlowingCandles.Infrastructure/Config/AppConfig.cs`.

Remove from `AppConfig`:
```csharp
public OutputConfig Output { get; init; } = new();
```
```csharp
public StateConfig State { get; init; } = new();
```

Remove the record definitions:
```csharp
public sealed record OutputConfig
{
    public string TextFile { get; init; } = "signals.txt";
    public string JsonFile { get; init; } = "signals.json";
}

public sealed record StateConfig
{
    public string Path { get; init; } = "data/state.json";
}
```

### 12. Remove dead config parsing from `YamlConfigLoader.cs`

File: `src/BlowingCandles.Infrastructure/Config/YamlConfigLoader.cs`.

Remove the `output` and `state` parsing variables:
```csharp
var outputTextFile = NormalizeOptionalString(config.Output?.TextFile) ?? "signals.txt";
var outputJsonFile = NormalizeOptionalString(config.Output?.JsonFile) ?? "signals.json";
var statePath = NormalizeOptionalString(config.State?.Path) ?? "data/state.json";
```

Remove the `Output` and `State` assignments in the `AppConfig` initializer:
```csharp
Output = (config.Output ?? new OutputConfig()) with
{
    TextFile = ResolvePath(baseDirectory, outputTextFile),
    JsonFile = ResolvePath(baseDirectory, outputJsonFile)
},
State = (config.State ?? new StateConfig()) with
{
    Path = ResolvePath(baseDirectory, statePath)
},
```

Remove from the raw YAML DTO class:
```csharp
public OutputConfig? Output { get; init; }
public StateConfig? State { get; init; }
```

## Validation

- [ ] `dotnet build` — no errors
- [ ] `dotnet test` — all 200 tests pass
- [ ] README accurately reflects current API-first architecture
- [ ] `config.yaml` contains only consumed config sections
- [ ] No references to `OutputConfig`, `StateConfig`, `signals.txt`, `signals.json`, or `state.json` remain in source code (excluding migrations, audit, and test golden files)
