# System Overview

## What It Does

Signals Bot is a manual trading signal generator for a small equity watchlist (currently AAPL, MSFT, NVDA, TSLA). It combines earnings proximity gating, technical analysis scoring, and policy rules to produce BUY, SELL, or WAIT signals. The operator reads the output and executes trades manually. There is no order execution logic in the system.

## Major Workflows

### Live Signal Generation

Run once via CLI or Docker. For every ticker on the watchlist:

1. **Earnings gate** -- check proximity to the next earnings date using a local calendar (with optional Yahoo Finance fallback). Block trading near earnings.
2. **Technical scoring** -- download one year of daily prices, compute SMA50, SMA200, and RSI14, and score the ticker for BUY, SELL, or WAIT.
3. **Trade governance** -- merge the earnings gate and technical score, apply policy constraints (daily buy limit, cooldown between buys), and emit a final signal.

Output: human-readable text file, structured JSON file, and an append-only JSONL audit log.

### As-Of Simulation

The same pipeline time-shifted to a historical date. Uses separate state and audit files so simulation never pollutes live data.

### Range Simulation

Batch loop over a date range, running one as-of simulation per day. Used for backtesting signal behavior across a period.

### Calendar Health Check

Validates that every watchlist ticker has future earnings dates in the local calendar. Produces structured OK/EXPIRED/MISSING output with meaningful exit codes for CI use.

### Holding Period Analysis

Parses the JSONL audit log to compute BUY-to-SELL holding periods per ticker. Supports date filtering and day-level detail.

## Entry Points

| Entry Point | Purpose | Mode |
|-------------|---------|------|
| Run Realtime | Live signal generation for the current date | One-shot CLI |
| Run As-Of | Simulation for a specific historical date | One-shot CLI |
| Run Range | Batch simulation across a date range | Batch CLI |
| Check Calendar | Validate earnings calendar completeness | Validator CLI |
| Stats Periods | Analyze BUY-to-SELL holding periods from audit logs | Read-only CLI |

All entry points are synchronous, single-process, CLI-only. No API, no scheduler, no background workers.

## System Boundaries

- **CLI-only.** No API, no scheduler, no background workers.
- **File-backed.** All state is JSON/JSONL files on the local filesystem. No database.
- **Single-process, synchronous.** Each invocation runs to completion and exits.
- **External data.** Yahoo Finance provides daily price data and (optionally) earnings date fallback.
- **Configuration.** A single YAML file defines the watchlist, thresholds, output paths, and policy parameters.

## Data Flow

```
config.yaml --> CLI
                 |
                 +--> Earnings Gate
                 |      reads: earnings_calendar.json
                 |      optional: Yahoo Finance earnings dates
                 |      output: NewsSignal per ticker
                 |
                 +--> Technical Scoring
                 |      reads: Yahoo Finance daily prices
                 |      output: MarketSignal per ticker
                 |
                 +--> Trade Governor
                        reads: NewsSignals + MarketSignals
                        reads/writes: state.json
                        output: FinalSignal per ticker
                               |
                               +--> signals.txt
                               +--> signals.json
                               +--> logs/decisions.jsonl
```

## Output Files

| File | Format | Purpose |
|------|--------|---------|
| `signals.txt` | Plain text, one line per ticker | Human-readable signal output |
| `signals.json` | JSON array of signal objects | Structured signal output |
| `logs/decisions.jsonl` | Append-only JSONL | Audit trail for analysis |
| `data/state.json` | JSON | Buy counter and cooldown tracking |

## External Dependencies

| Dependency | Usage |
|------------|-------|
| Yahoo Finance | Daily OHLCV prices for technical scoring; earnings date fallback when ticker is absent from local calendar |
| Local earnings calendar | Manually maintained JSON file mapping tickers to earnings dates |

No database, no message queue, no HTTP API, no external notification service.
