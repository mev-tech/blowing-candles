signals-bot

Decision-based trading signal system (manual execution)

============================================================

QUICK START

Build image (run only when code changes)
docker build -t signals-bot .

Run agents (live / today)
docker run --rm -v "$PWD:/app" signals-bot

View output
cat signals.txt

============================================================

EARNINGS CALENDAR

Edit calendar manually
nano earnings_calendar.json

Recommended format
{
"NVDA": ["2026-02-09T21:00:00Z", "2026-05-20T21:00:00Z"],
"AAPL": ["2026-04-30T21:00:00Z"]
}

Check calendar health (IMPORTANT – run regularly)
docker run --rm -v "$PWD:/app" -w /app signals-bot
python -m signals_bot.cli.check_calendar

Possible outputs
OK -> calendar is valid
EXPIRED -> ticker has only past earnings
MISSING -> ticker from watchlist missing in calendar

Exit codes
0 -> OK
2 -> calendar problem (CI / cron friendly)

============================================================

STATE & POLICY

View current state (cooldown / buys)
cat data/state.json

Reset state manually (testing / new day)
rm -f data/state.json

============================================================

AUDIT LOG

View last decisions
tail -n 20 logs/decisions.jsonl

Filter BUY signals
grep '"final": "Action.BUY"' logs/decisions.jsonl

Filter news blocks
grep BLOCKED_BY_NEWS logs/decisions.jsonl

============================================================

DEBUG & UTILITIES

Verify volume mount
docker run --rm -v "$PWD:/app" alpine sh -c "echo ok > /app/_test.txt"
ls _test.txt

Enter container (debug)
docker run --rm -it -v "$PWD:/app" signals-bot bash

============================================================

DAILY WORKFLOW (MANUAL TRADING)

docker build -t signals-bot . (only when code changes)
docker run --rm -v "$PWD:/app" signals-bot
cat signals.txt
docker run --rm -v "$PWD:/app" -w /app signals-bot
python -m signals_bot.cli.check_calendar

============================================================

SIMULATIONS / BACKTESTING (DECISION-BASED)

This project supports time-travel (as-of) simulations to see
what signals the system would have emitted on past dates,
without touching live state.

============================================================

RUN SIMULATION FOR A SPECIFIC DAY

docker run --rm -v "$PWD:/app" -w /app signals-bot
python -m signals_bot.cli.run_asof --asof 2025-09-18

Outputs
asof_2025-09-18.signals.txt
asof_2025-09-18.signals.json
audit appended in logs/sim_decisions.jsonl
simulation state in data/sim_state.json

============================================================

RUN SIMULATION FOR A DATE RANGE (DAILY LOOP)

Example: 15 Sep 2025 -> 07 Feb 2026

mkdir -p sim_outputs

START=2025-09-15
END=2026-02-07

d="$START"
while [ "$d" != "$(date -I -d "$END + 1 day")" ]; do
docker run --rm -v "$PWD:/app" -w /app signals-bot
python -m signals_bot.cli.run_asof
--asof "$d"
--out-prefix sim_outputs/AAPL
d=$(date -I -d "$d + 1 day")
done

Results
optional daily files in sim_outputs/
single source of truth for statistics:
logs/sim_decisions.jsonl

============================================================

STATISTICS: BUY -> SELL PERIODS

Compute holding periods for a ticker
docker run --rm -v "$PWD:/app" -w /app signals-bot
python -m signals_bot.cli.stats_periods --ticker AAPL

Example output
[1] BUY 2025-09-18 -> SELL 2025-10-10 | holding: 22 days
[open] BUY 2026-01-30 -> (no SELL yet) | holding so far: 8 days

Include all days in each period
python -m signals_bot.cli.stats_periods --ticker AAPL --list-days

Limit analysis to a date range
python -m signals_bot.cli.stats_periods
--ticker AAPL --start 2025-09-15 --end 2026-02-07

============================================================

IMPORTANT NOTES

Simulations use daily (1D) market data from Yahoo Finance

Earnings gating uses a local calendar (earnings_calendar.json)

Policy constraints (cooldown / max buys) can be disabled or relaxed for statistics

Simulations do not affect live state

Audit log is the canonical source for analysis:
logs/sim_decisions.jsonl


============================================================

TODO (Clear Backlog)

Core Correctness
- B: Entry/Exit = next day open (more realistic execution)
- Complete trade ledger
  - entry price
  - exit price
  - return %
  - max drawdown (optional)

Performance
- Fast backtesting (1-year range in reasonable time)
- Avoid “1 container per day”
- Bulk market data download (multi-ticker)
- Internal loop over dates
- --jobs N parallelization (per ticker)

UX / Product
- React UI (MVP)
  - Select interval (start / end)
  - Select mode (balanced / conservative / opportunistic)
  - Run simulation
  - View trades table (entry / exit / return / holding)
  - View audit timeline per ticker
- Simple API layer
  - run_live
  - run_asof
  - run_range
  - get_trades
  - get_audit

Ops / Runtime
- Long-running container
  - Periodic execution (internal scheduler / cron)
  - On-demand execution triggered from UI
- Notifications (later)
  - Webhook
  - Email
  - Discord
  - Telegram
  - Manual execution first

Architecture Evolution
- Microservices split (after contracts are stable)
  - Market service
  - News service
  - Governor / Policy service
  - Backtester service
- Microfrontends
  - Only if needed (otherwise keep it simple)

Roadmap by Releases

v1.0 — MVP (Current)
- Agents + outputs
- Audit + state
- run_asof + stats_periods
- Earnings calendar helper

v1.1 — Performance & Backtesting Usability
Goal: 1-year resolution in decent time
- run_range (single process, internal date loop)
- Bulk yfinance download (multi-ticker)
- --jobs N parallelization per ticker
- Single audit file + ledger CSV

v1.2 — Realism Upgrade
Goal: execution closer to reality
- B: next day open for entry / exit
- Optional slippage / fees (configurable)

v1.3 — UI & Control Plane
Goal: no more terminal-only usage
- React UI (MVP)
  - Select tickers, interval, entry mode
  - Run simulation / run live
  - Trades table + simple charts
- Minimal FastAPI backend in the same container

v1.4 — Long-Running + Triggers
Goal: always-on agent
- Server-mode container
- UI-triggered runs (run now)
- Periodic scheduler (e.g. daily at 18:00 UTC)
- Persistent volumes for state and audit

v1.5 — Split & Scale (Optional)
- Microservices split (market / news / governor / backtester)
- Microfrontends only if justified
