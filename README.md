# BlowingCandles

Manual trading signal generator for a small equity watchlist.
Combines earnings proximity gating, technical analysis scoring, and policy rules
to produce BUY, SELL, or WAIT signals. The operator reads the output and executes
trades manually — there is no order execution logic.

## Prerequisites

Two options:

- **Docker** (recommended) — Docker 20.10+ with Compose V2
- **Local** — .NET 8 SDK

## Quick Start (Docker)

```bash
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
```

## Quick Start (Local / .NET SDK)

```bash
# Build
dotnet build BlowingCandles.sln

# Run (from repo root)
dotnet run --project src/BlowingCandles.Cli -- check-calendar
dotnet run --project src/BlowingCandles.Cli -- run-realtime
dotnet run --project src/BlowingCandles.Cli -- run-asof 2026-01-15
dotnet run --project src/BlowingCandles.Cli -- run-range 2026-01-01 2026-01-31
dotnet run --project src/BlowingCandles.Cli -- stats-periods
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

output:
  text_file: signals.txt
  json_file: signals.json

state:
  path: data/state.json

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
| `output` | `text_file` | `string` | `signals.txt` | Path for human-readable signal output. |
| `output` | `json_file` | `string` | `signals.json` | Path for structured JSON signal output. |
| `state` | `path` | `string` | `data/state.json` | Path for the state file tracking daily buy count and cooldown. |
| `audit` | `jsonl_path` | `string?` | `null` | Path for the append-only JSONL audit log. If omitted, no audit log is written. |

`watchlist` is the only required field. All others have defaults.

## Output Files

**Live mode** (`run-realtime`):

| File | Format | Description |
|------|--------|-------------|
| `signals.txt` | Plain text | One line per ticker with signal |
| `signals.json` | JSON array | Structured signal objects |
| `data/state.json` | JSON | Buy counter and cooldown tracking |
| `logs/decisions.jsonl` | JSONL | Append-only audit trail |

**Simulation mode** (`run-asof`, `run-range`):

Uses separate files to never pollute live data:
- `asof_{date}.signals.txt` / `asof_{date}.signals.json`
- `data/sim_state.json`
- `logs/sim_decisions.jsonl`

## Docker (Manual)

For users who don't want docker-compose:

```bash
docker build -t blowing-candles .

docker run --rm \
  -v "$PWD/config.yaml:/app/config.yaml:ro" \
  -v "$PWD/earnings_calendar.json:/app/earnings_calendar.json:ro" \
  -v "$PWD/data:/app/data" \
  -v "$PWD/logs:/app/logs" \
  blowing-candles check-calendar
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
dotnet test tests/BlowingCandles.CrossValidation.Tests
```

## Project Structure

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
├── docs/                               # Architecture and design documentation
├── config.yaml                         # Application configuration
├── earnings_calendar.json              # Local earnings calendar data
├── Dockerfile                          # Multi-stage build (restore → test → publish → runtime)
└── docker-compose.yml                  # Simplified Docker workflow
```
