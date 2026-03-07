# Python System Analysis

Analysis of the `signals-bot/` Python application to inform a clean C# reimplementation.

## System Purpose

A manual trading signal generator for a small equity watchlist (currently AAPL, MSFT, NVDA, TSLA). It combines earnings/news gating with technical analysis (SMA/RSI scoring) and policy rules (buy limits, cooldowns) to produce BUY/SELL/WAIT signals. The operator reads the output and executes trades manually. There is no order execution logic in the codebase.

## Major Workflows

| Workflow | Description |
|----------|-------------|
| Live signal generation | Run once (Docker or CLI), evaluate all watchlist tickers against today's market data and earnings calendar, emit signals to files |
| As-of simulation | Same pipeline but time-shifted to a past date; uses separate state/audit files to avoid polluting live state |
| Range simulation | Batch loop over a date range, spawning one `run_asof` subprocess per day |
| Calendar health check | Validate that every watchlist ticker has future earnings dates in the local calendar |
| Stats/periods analysis | Parse audit JSONL to compute BUY-to-SELL holding periods per ticker |

## Entry Points

| Entry Point | Invocation | Type |
|-------------|------------|------|
| `signals_bot.cli.run_realtime` | `python -m signals_bot.cli.run_realtime` (Docker CMD default) | Live, one-shot |
| `signals_bot.cli.run_asof` | `--asof YYYY-MM-DD [--out-prefix X] [--sim-state X] [--sim-audit X]` | Simulation, one-shot |
| `signals_bot.cli.run_range` | `--start YYYY-MM-DD --end YYYY-MM-DD [--prefix X]` | Batch, subprocess loop |
| `signals_bot.cli.check_calendar` | No args | Validator, exit code 0 or 2 |
| `signals_bot.cli.stats_periods` | `--ticker X [--jsonl X] [--start X] [--end X] [--list-days]` | Analysis, read-only |

All entry points are synchronous, single-process, CLI-only. No API, no scheduler, no background workers.

## Modules and Responsibilities

### Domain Models — `core/models.py`

- Enums: `NewsState` (TRADE_OK, WAIT, NO_TRADE, MANAGE, EXIT_RECOMMENDED, EXIT_NOW) and `Action` (BUY, SELL, WAIT, IGNORE, MANAGE, EXIT_RECOMMENDED, EXIT_NOW).
- Pydantic v2 models: `NewsSignal`, `MarketSignal`, `FinalSignal`.
- MANAGE, EXIT_RECOMMENDED, and EXIT_NOW are defined but never emitted by any current code path.

### NewsSentinel — `agents/news_sentinel.py`

- Loads and parses the local `earnings_calendar.json`.
- For each ticker: checks proximity to next earnings date.
- If the ticker is present in the local calendar but has no future dates, returns WAIT with `CALENDAR_EXPIRED`.
- If the ticker is absent from the local calendar, falls back to Yahoo Finance `get_earnings_dates()` / `calendar` and tags the result with `EARNINGS_FROM_YF`.
- Returns NO_TRADE with `EARNINGS_LT_48H` when the next earnings event is within the configured block window (default 48 hours).
- Returns WAIT with `DATA_UNAVAILABLE` when neither local data nor Yahoo data produces a date.

### MarketAnalyst — `agents/market_analyst.py`

- Downloads one year of daily OHLCV from Yahoo Finance via `yf.download()`.
- Computes SMA50, SMA200, and RSI14 using pandas rolling operations.
- Scoring: Close > SMA200 (+40), SMA50 > SMA200 (+30), RSI14 > 50 (+30).
- Returns SELL if Close < SMA200 OR RSI14 < 40.
- Returns BUY if score >= 80, otherwise WAIT.
- Requires at least 210 closing prices or returns WAIT with `NOT_ENOUGH_HISTORY`.

### TradeGovernor — `agents/trade_governor.py`

- Merges news and market signals into final decisions.
- Gate priority: missing news -> WAIT; stale news -> WAIT; NO_TRADE -> IGNORE; WAIT news -> WAIT.
- If news is TRADE_OK, passes through the market action.
- Policy constraints applied only to BUY actions: `max_buys_per_day` checked first, then `cooldown_minutes`.
- Persists buy count via StateStore; records at most one buy increment per run.
- Processes tickers in sorted (alphabetical) order; this determines which ticker receives the first BUY in a run.

### StateStore — `shared/state_store.py`

- JSON file with fields: `day`, `buys_today`, `last_buy_at`.
- Auto-resets counters when the current day changes (compares against real UTC clock, not as_of).
- Fail-safe: corrupted or missing file silently resets to empty state.

### Audit — `shared/audit.py`

- Appends JSONL rows to the audit log file.
- Provides `utc_now_iso()` helper for wall-clock timestamps.

### CLI Orchestrators — `cli/run_realtime.py`, `cli/run_asof.py`

- Load `config.yaml`, instantiate all three agents, run the pipeline, format output, write files.
- Nearly identical code with two key differences: `run_asof` passes a historical datetime via `as_of`; uses separate state and audit paths.
- Output formatting logic is duplicated between the two modules.

## Data Flow

```
config.yaml --> CLI orchestrator
                    |
                    +--> NewsSentinel.check(watchlist, [as_of])
                    |       |-- reads earnings_calendar.json
                    |       +-- optionally queries Yahoo Finance (earnings dates)
                    |       => List[NewsSignal]
                    |
                    +--> MarketAnalyst.analyze(watchlist, [as_of])
                    |       +-- downloads daily prices from Yahoo Finance
                    |       => List[MarketSignal]
                    |
                    +--> TradeGovernor.decide(news, market, [as_of])
                            |-- reads/writes state.json (or sim_state.json)
                            => List[FinalSignal]
                                    |
                                    +--> signals.txt (human-readable)
                                    +--> signals.json (structured)
                                    +--> logs/decisions.jsonl (append-only audit)
```

## External Dependencies

| Dependency | Usage | Notes |
|------------|-------|-------|
| Yahoo Finance (yfinance) | Daily prices (MarketAnalyst), earnings dates fallback (NewsSentinel) | Network-dependent, non-deterministic, rate-limited |
| pandas / numpy | SMA and RSI calculations, DataFrame handling | Computation library |
| pydantic v2 | Domain model validation and serialization | `model_dump(mode="json")` used for output |
| PyYAML | Config loading | |
| Docker | Documented runtime wrapper | Not required; can run directly with Python |

No database, no message queue, no HTTP API, no external notification service.

## Persistent State and Files

| File | Mode | Purpose |
|------|------|---------|
| `config.yaml` | Read | Watchlist, thresholds, output paths |
| `earnings_calendar.json` | Read | Manually maintained earnings dates |
| `data/state.json` | Read/Write | Live buy counter and cooldown tracking |
| `data/sim_state.json` | Read/Write | Simulation buy counter (separate from live) |
| `logs/decisions.jsonl` | Append | Live audit trail |
| `logs/sim_decisions.jsonl` | Append | Simulation audit trail |
| `signals.txt` | Overwrite | Human-readable signal output (live) |
| `signals.json` | Overwrite | JSON signal output (live) |
| `asof_*.signals.txt/json` | Write | Per-date simulation output |

## Hidden Coupling / Global State

1. **CWD dependency.** All CLI modules use `Path("config.yaml")` with no directory qualification. They assume the current working directory is `signals-bot/`. The Dockerfile sets WORKDIR to `/app`.

2. **Real clock leaks into simulation.** `StateStore.reset_if_new_day()` uses `datetime.now(timezone.utc)`, not the `as_of` timestamp. A simulation for 2025-09-15 compares state against today's real date, causing the state to reset unexpectedly when the simulation date differs from today.

3. **One-buy-per-run accounting.** `TradeGovernor` tracks `buy_executed_this_run` as a single boolean, not per-ticker. If multiple tickers qualify for BUY in the same run, only the first (alphabetically) gets the BUY recorded to state. The state persists at most +1 buy per invocation.

4. **Stale-data check is dormant in practice.** The TTL check `(now - ns.timestamp) > self.ttl` compares signals created in the same run. The news timestamp always equals `now`, so TTL never triggers under normal operation.

5. **Duplicated code.** `run_realtime.py` and `run_asof.py` share approximately 80% identical output formatting logic. `check_calendar.py` duplicates the calendar parsing logic from `NewsSentinel`.

6. **Enum string format inconsistency.** `signals.json` uses plain strings (`"BUY"`), while `signals.txt` and audit JSONL use Python enum format (`"Action.BUY"`). `stats_periods` must strip the `"Action."` prefix to function.

## Error Handling Patterns

- **Fail-safe to WAIT.** Every agent defaults to WAIT on exceptions. `MarketAnalyst` catches all exceptions and returns WAIT with `MARKET_DATA_ERROR`. `NewsSentinel` catches all and returns WAIT with `DATA_ERROR`. `StateStore` treats corrupt files as empty state. This is a safety-critical invariant.
- **No retries.** No retry logic anywhere. If Yahoo Finance fails, the ticker gets WAIT.
- **No logging framework.** Uses `print()` only. No structured logging.
- **Exit codes.** `check_calendar` uses exit code 2 for calendar problems (CI-friendly). Other CLIs do not use non-zero exit codes explicitly.

## Edge Cases and Unusual Behavior

- **RSI uses SMA, not EMA.** The RSI calculation uses `pandas.rolling().mean()` (simple moving average), not exponential/Wilder's smoothing. This is a deliberate simplification that produces different values than standard RSI implementations.
- **Multi-index DataFrame handling.** `yfinance` can return either flat or multi-index DataFrames depending on parameters. `_extract_close()` handles both, with case-insensitive ticker fallback.
- **as_of comment vs behavior.** `run_asof` contains a comment saying it will use noon UTC, but the implementation converts `YYYY-MM-DD` to midnight UTC via `datetime.fromisoformat`.
- **run_range subprocess isolation.** Each day runs in a separate Python process, meaning state resets between days are governed by the real clock rather than simulated date progression.
- **Repeated runs append to audit.** Running `run_realtime` multiple times on the same day appends duplicate decision rows. There is no deduplication.
- **valid_until field unused.** Set by NewsSentinel (earnings time + 6 hours) but never consumed by any downstream code.
- **No market calendar.** Simulations run for every calendar day including weekends. yfinance returns the last trading day's data for non-trading days, so weekend signals reflect Friday's close.
- **Checked-in sample artifacts are stale.** The committed `signals.txt` and `signals.json` show `MAX_BUYS_REACHED` even though the current `config.yaml` sets `max_buys_per_day: 99999999`. Treat these as examples, not canonical fixtures.

## Critical Behavior To Preserve

1. **Market scoring algorithm.** SMA200 (+40), SMA50 > SMA200 (+30), RSI14 > 50 (+30), threshold >= 80 for BUY. SELL triggers: Close < SMA200 OR RSI14 < 40. Minimum 210 data points required.

2. **News gating priority.** NO_TRADE maps to IGNORE. WAIT maps to WAIT. TRADE_OK passes through to the market action. Stale data maps to WAIT. Missing news maps to WAIT.

3. **Policy enforcement order.** `max_buys_per_day` is checked before cooldown. Both constraints apply only to BUY actions.

4. **Fail-safe defaults.** Every error condition defaults to WAIT, never to BUY or SELL.

5. **Earnings proximity blocking.** Within the configured block window (default 48 hours) the result is NO_TRADE. Calendar expired results in WAIT.

6. **Audit append-only semantics.** The JSONL audit log is append-only and never truncated. It is the canonical data source for backtesting analysis.

7. **Live/sim state isolation.** Simulations use separate state and audit files and never touch live state.

8. **Output file contracts.** `signals.txt` (one line per ticker), `signals.json` (array of signal objects), and JSONL audit rows are the user-facing contracts.

9. **Ticker processing order.** The governor processes tickers in sorted (alphabetical) order. This determines which ticker receives the first BUY when multiple qualify.

## Optional / Legacy Behavior

1. **Unused enum values.** MANAGE, EXIT_RECOMMENDED, and EXIT_NOW in both Action and NewsState are never emitted. Keep in the domain model but do not implement production paths.

2. **`valid_until` field.** Set but never consumed. Can be omitted from initial implementation.

3. **Stale-data TTL check.** Dormant in current usage because news and decisions share the same timestamp within a run. Could be simplified or redesigned for future use.

4. **`run_range` subprocess model.** Spawning a new process per day is a performance limitation. The C# version should loop dates in-process.

5. **Duplicated output formatting.** Should be unified into a single renderer.

6. **Python enum string format.** The `"Action.BUY"` format is an artifact of Python's `str(enum)`. C# should use a clean format (just `"BUY"`) and adapt `stats_periods` parsing accordingly.

7. **Yahoo Finance earnings fallback.** The yfinance earnings API is fragile and unreliable. The C# version could make the local calendar mandatory and drop the fallback entirely.

8. **Duplicated calendar parsing.** `check_calendar` duplicates parsing logic instead of reusing `NewsSentinel`. Should share a single implementation.

## Risk Areas

1. **RSI calculation method.** Python uses SMA-based RSI via `pandas.rolling().mean()`. A C# port using Wilder's smoothing (EMA) would produce different RSI values and therefore different BUY/SELL signals. The calculation method must match exactly or all backtest comparisons will diverge.

2. **Yahoo Finance data retrieval.** `yfinance` handles auto-adjustment, date ranges, and multi-index DataFrames in specific ways. A different C# Yahoo Finance library may return slightly different adjusted close values or handle date boundaries differently. This is the biggest source of non-determinism between the Python and C# implementations.

3. **State reset clock bug.** `StateStore.reset_if_new_day()` uses the real clock in simulation mode. If the C# version fixes this to use `as_of`, simulation state will behave differently and produce different buy counts during backtests. An explicit decision is needed: preserve the bug for parity or fix it and accept divergence.

4. **Date and timezone handling.** The `run_asof` conversion says noon UTC in a comment but produces midnight UTC. SMA/RSI values depend on which trading day's close is the last data point. An off-by-one in the date range passed to Yahoo Finance could shift the entire analysis window.

5. **Floating-point boundary effects.** Score thresholds (>= 80) and RSI thresholds (< 40, > 50) sit at integer/float boundaries. Minor floating-point differences in SMA/RSI calculations between pandas and C# numeric libraries could flip signals at the boundary.

6. **One-buy-per-run state persistence.** The governor only persists state once at the end if any BUY occurred. If C# changes this to per-ticker persistence, buy counting behavior changes.

7. **Audit timestamp semantics.** Live mode uses wall-clock time (`utc_now_iso()`), simulation mode uses the `as_of` timestamp. The C# version must preserve this distinction or `stats_periods` date parsing will break.

## Architecture Summary

signals-bot is a synchronous, single-process, CLI-first Python application that generates manual trading signals. It follows a three-agent pipeline:

1. **NewsSentinel** gates on earnings proximity using a local calendar (with Yahoo Finance fallback).
2. **MarketAnalyst** scores stocks using SMA200, SMA50, and RSI14 from daily Yahoo Finance data.
3. **TradeGovernor** merges the two signal streams, applies policy constraints (buy limits, cooldown), and emits final BUY/SELL/WAIT decisions.

All state is file-backed: JSON state files, JSONL audit logs, and text/JSON output files. Configuration is a single YAML file. The system runs in Docker as one-shot invocations with no API, no scheduler, and no database. Five CLI entry points cover live signals, historical simulation (single-day and range), calendar validation, and backtest analysis.

The system is deliberately conservative: every error condition defaults to WAIT, and the governor acts as the single authority that can approve or block trades. The architecture is simple and well-bounded, making it a good candidate for a clean C# reimplementation provided the numerical algorithms (especially RSI) and Yahoo Finance data boundaries are matched precisely.
