# Requirements Document — BlowingCandles

## System Purpose

BlowingCandles is a .NET 8 trading signal generator that produces manual trading signals for a small equity watchlist. It combines three independent analyses — earnings proximity, technical indicators, and policy rules — to emit BUY, SELL, or WAIT signals. The system runs as a synchronous, single-process CLI and exposes RESTful API endpoints for signal retrieval.

## Users

- **Operator**: A single manual trader who reads generated signals and executes trades. Interacts via CLI commands or REST API.
- **Downstream consumers**: Applications or dashboards that consume signals via REST API endpoints.

## Core Capabilities

### Signal Generation (Implemented)

1. **Earnings Gate** — Blocks trading within a configurable window (default 48 hours) of earnings dates using a local calendar with Yahoo Finance fallback.
2. **Technical Scoring** — Computes SMA50, SMA200, RSI14 from 365 days of daily OHLCV data and produces a 0–100 score mapped to BUY (≥80), SELL (≤20), or WAIT.
3. **Trade Governor** — Merges news and market signals into final signals, enforcing max buys per day, cooldown between buys, and news TTL staleness.
4. **Pipeline Orchestration** — Three run modes: `run-realtime`, `run-asof <date>`, `run-range <start> <end>`.
5. **Calendar Validation** — `check-calendar` command validates that all watchlist tickers have future earnings dates.
6. **Holding Period Stats** — `stats-periods` command computes BUY-to-SELL holding periods from audit logs.

### REST API (New)

7. **Signal Retrieval Endpoints** — RESTful API that returns current and historical signals over HTTP.

## Inputs and Outputs

### Inputs

| Input                  | Format         | Source                        |
|------------------------|----------------|-------------------------------|
| Configuration          | YAML           | `config.yaml`                 |
| Earnings calendar      | JSON           | `earnings_calendar.json`      |
| Historical price data  | OHLCV          | Yahoo Finance API             |
| Run parameters         | CLI arguments  | Command line                  |
| State                  | JSON           | `data/state.json`             |

### Outputs

| Output            | Format    | Destination                          |
|-------------------|-----------|--------------------------------------|
| Signals (text)    | Prefixed  | `signals.txt`                        |
| Signals (JSON)    | Plain     | `signals.json`                       |
| Audit trail       | JSONL     | `logs/decisions.jsonl`               |
| Signals (API)     | JSON      | HTTP response via REST endpoints     |
| Exit codes        | Integer   | Process exit code (0, 2)             |

### REST API Endpoints

| Method | Path                       | Description                                |
|--------|----------------------------|--------------------------------------------|
| GET    | `/api/signals`             | Returns the latest generated signals       |
| GET    | `/api/signals/{ticker}`    | Returns the latest signal for a ticker     |

## External Integrations

| System              | Purpose                          | Protocol   |
|---------------------|----------------------------------|------------|
| Yahoo Finance       | Daily OHLCV price data retrieval | HTTPS      |

## Constraints

1. **Behavioral parity** — Signal logic must match Python `signals-bot` output for identical inputs.
2. **Fail-safe by default** — Errors produce WAIT, never BUY or SELL.
3. **Alphabetical processing** — Trade governor processes tickers in sorted order.
4. **RSI calculation** — Must use simple moving average (SMA), not EMA or Wilder's smoothing.
5. **Audit serialization** — Prefixed enums (`"Action.BUY"`) in JSONL, plain enums (`"BUY"`) in signals.json and API responses.
6. **Single process** — No background workers, no persistent server state beyond file-based state store.
7. **No database** — All persistence is file-based (JSON, JSONL, YAML).
8. **No DI container** — Manual wiring only.

## Non-Functional Requirements

1. **Runtime**: .NET 8 LTS.
2. **Containerisation**: Docker multi-stage build with test gating.
3. **Testability**: All external I/O behind interfaces; xUnit test suite with cross-validation against Python reference outputs.
4. **Logging**: Structured console logging via `Microsoft.Extensions.Logging`.
5. **API response time**: Signal retrieval endpoints should return within 200 ms (serving pre-computed signals from the latest pipeline run).
6. **API format**: JSON responses using `System.Text.Json` with plain enum serialisation (consistent with `signals.json`).
7. **Minimal footprint**: REST API layer adds no new external dependencies beyond `Microsoft.AspNetCore` (included in .NET 8 SDK).
