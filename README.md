# BlowingCandles

Manual trading signal generator for a small equity watchlist.
Combines earnings proximity gating, technical analysis scoring, and policy rules
to produce BUY, SELL, or WAIT signals. Runs as an API service (default) with an
optional CLI. The operator reads the signals and executes trades manually - there
is no order execution logic.

## Prerequisites

Two options:

- **Docker** (recommended) - Docker 20.10+ with Compose V2 (includes PostgreSQL via docker-compose)
- **Local** - .NET 8 SDK + PostgreSQL 16+

## Quick Start (Docker)

```bash
# Build and start the API service (+ PostgreSQL)
docker compose up -d

# Apply database migrations (required once, needs .NET 8 SDK)
dotnet ef database update \
  --project src/BlowingCandles.Infrastructure \
  --startup-project src/BlowingCandles.Cli

# Check the API is running
curl http://localhost:5000/health/ready

# Get latest signals (the background worker runs automatically on startup)
curl http://localhost:5000/api/signals

# Trigger a realtime signal run manually
curl -X POST http://localhost:5000/api/runs/realtime

# Stop
docker compose down
```

### CLI via Docker

```bash
# CLI commands use the "cli" subcommand (or legacy shortcut names)
docker compose run --rm app cli check-calendar
docker compose run --rm app cli run-realtime
docker compose run --rm app cli run-asof 2026-01-15
docker compose run --rm app cli run-range 2026-01-01 2026-01-31
docker compose run --rm app cli stats-periods

# Legacy shortcuts also work (without "cli" prefix)
docker compose run --rm app check-calendar
```

## Quick Start (Local / .NET SDK)

Requires a running PostgreSQL 16+ instance.

```bash
# Build
dotnet build BlowingCandles.sln

# Apply database migrations (required once)
dotnet ef database update \
  --project src/BlowingCandles.Infrastructure \
  --startup-project src/BlowingCandles.Cli

# Run (from repo root)
dotnet run --project src/BlowingCandles.Cli -- check-calendar
dotnet run --project src/BlowingCandles.Cli -- run-realtime
dotnet run --project src/BlowingCandles.Cli -- run-asof 2026-01-15
dotnet run --project src/BlowingCandles.Cli -- run-range 2026-01-01 2026-01-31
dotnet run --project src/BlowingCandles.Cli -- stats-periods

# Or start the API
dotnet run --project src/BlowingCandles.Api
```

## Commands

| Command | Description | Arguments | Exit Codes |
|---------|-------------|-----------|------------|
| `check-calendar` | Validate that every watchlist ticker has future earnings dates in the local calendar | None | `0` OK, `2` expired or missing |
| `stats-periods` | Parse the JSONL audit log to compute BUY-to-SELL holding periods per ticker | None | `0` success, `1` error |
| `run-realtime` | Generate live trading signals for today's date | None | `0` success, `1` error |
| `run-asof <date>` | Simulate signal generation for a historical date | `date` in `yyyy-MM-dd` format | `0` success, `1` error |
| `run-range <start> <end>` | Batch simulation — runs one as-of simulation per day across the range | `start` and `end` in `yyyy-MM-dd` format | `0` success, `1` error |

`--help`, `-h`, or `help` prints usage and exits with code `0`.

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

The worker is enabled by default in `docker-compose.yml`. It runs immediately on startup (no initial delay), then repeats every `IntervalMinutes`.

## How It Works

1. **Signal pipeline** evaluates each ticker in the watchlist:
   - **Earnings gate** - checks the local earnings calendar (and Yahoo Finance as fallback) to block trading near earnings dates.
   - **Technical scorer** - fetches 365 days of price history from Yahoo Finance, computes SMA50/SMA200/RSI14, and emits a market signal.
   - **Trade governor** - merges signals and applies policy rules (max buys/day, cooldown).
2. **Market data is fetched on-demand** from Yahoo Finance during each signal run. No pre-population or API keys are needed. Fetched data is cached in PostgreSQL with a TTL for subsequent runs.
3. **Earnings calendar** (`earnings_calendar.json`) is optional. If a ticker is missing from it, the pipeline falls back to Yahoo Finance for the next earnings date.
4. **Results are persisted** to PostgreSQL and available via the API immediately after a run completes.

## Configuration

The app reads `config.yaml` from the working directory. All file paths are resolved relative to the config file location.

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

| Section | Key | Type | Default | Description |
|---------|-----|------|---------|-------------|
| `watchlist` | — | `string[]` | **(required)** | Ticker symbols to evaluate. Automatically uppercased and trimmed. |
| `news` | `local_earnings_calendar` | `string?` | `null` | Path to the local earnings calendar JSON file. If omitted, only Yahoo Finance fallback is used. |
| `news` | `block_window_hours` | `int` | `48` | Hours before/after earnings to block trading. |
| `policy` | `max_buys_per_day` | `int` | `2147483647` (unlimited) | Maximum number of BUY signals per day. |
| `policy` | `cooldown_minutes` | `int` | `0` | Minimum minutes between consecutive BUY signals. |
| `audit` | `jsonl_path` | `string?` | `null` | Path for the append-only JSONL audit log. If omitted, no audit log is written. |

`watchlist` is the only required field. All others have defaults.

### Infrastructure Configuration

Database and worker settings are configured via `appsettings.json` or environment variables:

| Variable | Default | Description |
|----------|---------|-------------|
| `ConnectionStrings__AppDb` | *(see appsettings.json)* | PostgreSQL connection string |
| `Worker__Enabled` | `false` | Enable background signal generation |
| `Worker__IntervalMinutes` | `60` | Interval between worker runs |

## Data Storage

Signal results, run metadata, and trade governor state are persisted to PostgreSQL.

| Table | Description |
|-------|-------------|
| `signal_run` | Pipeline run metadata (type, status, timestamps) |
| `signal_run_result` | Individual signals per run (ticker, action, news state) |
| `trade_governor_state` | Buy count and cooldown tracking per mode |
| `market_data_snapshot` | Cached market data from Yahoo Finance |

The JSONL audit log (`logs/decisions.jsonl`) is still written when configured.

## Docker (Manual)

For users who don't want docker-compose (requires a running PostgreSQL instance with migrations applied):

```bash
docker build -t blowing-candles .

# Apply migrations first (requires .NET 8 SDK)
dotnet ef database update \
  --project src/BlowingCandles.Infrastructure \
  --startup-project src/BlowingCandles.Cli

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
```

The Dockerfile runs all tests during the build (test stage). If tests fail, the build fails.

## Running Tests

```bash
# All tests
dotnet test BlowingCandles.sln

# Individual test projects
dotnet test tests/BlowingCandles.Domain.Tests
dotnet test tests/BlowingCandles.Application.Tests
dotnet test tests/BlowingCandles.Infrastructure.Tests
dotnet test tests/BlowingCandles.Api.Tests
dotnet test tests/BlowingCandles.CrossValidation.Tests
```

## Project Structure

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
├── docs/                               # Architecture and design documentation
├── config.yaml                         # Application configuration
├── earnings_calendar.json              # Local earnings calendar data
├── Dockerfile                          # Multi-stage build (restore → test → publish → runtime)
└── docker-compose.yml                  # Simplified Docker workflow
```
