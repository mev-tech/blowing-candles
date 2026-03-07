# Python Behavioral Reference

Behaviors documented here are derived from the Python implementation under `signals-bot/` and must be preserved in the C# rewrite unless an explicit decision approves a change.

For the full analysis, see [python-system-analysis.md](python-system-analysis.md).

## Earnings Gate (NewsSentinel)

- Loads and parses a local `earnings_calendar.json` file.
- If the ticker is present in the local calendar but has no future dates, returns WAIT with `CALENDAR_EXPIRED`.
- If the ticker is absent from the local calendar, falls back to Yahoo Finance and tags the result with `EARNINGS_FROM_YF`.
- Returns NO_TRADE with `EARNINGS_LT_48H` when the next earnings event is within the configured block window (default 48 hours).
- Returns WAIT with `DATA_UNAVAILABLE` when neither local data nor Yahoo data produces a date.
- On any exception, returns WAIT with `DATA_ERROR`.

## Technical Scoring (MarketAnalyst)

- Downloads one year of daily OHLCV from Yahoo Finance.
- Requires at least 210 closing prices or returns WAIT with `NOT_ENOUGH_HISTORY`.
- Computes SMA50, SMA200, and RSI14 using simple moving averages (not EMA/Wilder's smoothing).
- RSI uses `rolling().mean()` for both gains and losses. This produces different values than standard RSI implementations using exponential smoothing. The C# implementation must match this exactly.
- Scoring: Close > SMA200 (+40), SMA50 > SMA200 (+30), RSI14 > 50 (+30).
- Returns SELL if Close < SMA200 OR RSI14 < 40.
- Returns BUY if score >= 80, otherwise WAIT.
- On any exception, returns WAIT with `MARKET_DATA_ERROR`.

## Trade Governance (TradeGovernor)

- Processes tickers in sorted (alphabetical) order. This determines which ticker receives the first BUY when multiple qualify.
- Gate priority: missing news -> WAIT (`NO_NEWS_STATE`); stale news -> WAIT (`DATA_STALE`); NO_TRADE -> IGNORE; WAIT news -> WAIT.
- If news is TRADE_OK, passes through the market action.
- Policy constraints apply only to BUY actions: `max_buys_per_day` checked first, then `cooldown_minutes`.
- Tracks `buy_executed_this_run` as a single boolean (not per-ticker). Only the first BUY in a run gets recorded to state.
- Persists at most one buy increment per invocation.

## State Management

- JSON file with fields: `day`, `buys_today`, `last_buy_at`.
- Auto-resets counters when the current day changes.
- **Known quirk:** `reset_if_new_day()` compares against the real UTC clock, not the `as_of` timestamp. This means simulation state resets are based on real time, not simulated time.
- Corrupted or missing state files silently reset to empty state.

## Audit Logging

- Appends JSONL rows to the audit log file. The log is append-only and never truncated.
- Live mode uses wall-clock UTC timestamps; simulation mode uses the `as_of` timestamp.
- Enum values in audit JSONL use Python-prefixed format: `"Action.BUY"`, `"NewsState.TRADE_OK"`.
- `signals.json` uses plain unprefixed strings: `"BUY"`, `"TRADE_OK"`.

## Fail-Safe Defaults

Every error condition in every component defaults to WAIT, never to BUY or SELL. This is a safety-critical invariant that must be preserved.

## Domain Model

- Enums: `NewsState` (TRADE_OK, WAIT, NO_TRADE, MANAGE, EXIT_RECOMMENDED, EXIT_NOW) and `Action` (BUY, SELL, WAIT, IGNORE, MANAGE, EXIT_RECOMMENDED, EXIT_NOW).
- MANAGE, EXIT_RECOMMENDED, and EXIT_NOW are defined but never emitted by any current code path. Keep in the domain model but do not emit.
- `NewsSignal.valid_until` is set but never consumed downstream. Can be omitted from initial implementation.
- Signal models: `NewsSignal`, `MarketSignal`, `FinalSignal`.

## Output Contracts

| Output | Format | Key Details |
|--------|--------|-------------|
| `signals.txt` | One line per ticker | Uses `Action.BUY` style enum strings |
| `signals.json` | JSON array | Uses plain `"BUY"` style enum strings |
| `logs/decisions.jsonl` | Append-only JSONL | Uses `Action.BUY` and `NewsState.TRADE_OK` prefixed enum strings |
| `data/state.json` | JSON | Fields: `day`, `buys_today`, `last_buy_at` |

## Live vs Simulation Isolation

- Simulations use separate state and audit files (`sim_state.json`, `sim_decisions.jsonl`).
- Simulation output files use date-stamped names (`asof_*.signals.txt`, `asof_*.signals.json`).
- Simulations never read or write live state or audit files.

## Configuration

A single `config.yaml` defines:
- `watchlist`: list of ticker symbols
- `news.local_earnings_calendar`: path to earnings calendar JSON
- `news.block_window_hours`: hours before earnings to block trading (default 48)
- `policy.max_buys_per_day`: daily buy limit
- `policy.cooldown_minutes`: minimum minutes between buys
- `output.text_file`, `output.json_file`: output paths
- `state.path`: state file path
- `audit.jsonl_path`: audit log path

## Known Quirks to Decide On

1. **State reset uses real clock in simulation.** See State Management above. Decide whether to preserve or fix.
2. **One-buy-per-run accounting.** Only one BUY can be recorded to state per invocation, regardless of how many tickers qualify.
3. **Stale-data TTL is dormant.** The staleness check compares signals created in the same run, so TTL never triggers under normal operation.
4. **No market calendar.** Simulations run for every calendar day including weekends. Weekend signals reflect Friday's close.
