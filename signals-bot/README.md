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
















OLD:
signals-bot workspace

docker build -t signals-bot .
docker run --rm -v "$PWD:/app" signals-bot
cat signals.txt

docker run --rm -v "$PWD:/app" -w /app signals-bot python -m signals_bot.cli.check_calendar
---------


Perfect. Uite cheat-sheet-ul complet, scurt și clar, cu toate comenzile relevante în stadiul actual al proiectului 👇
(îl poți salva ca COMMANDS.md dacă vrei)

🧱 Build & Run (agenții)
Build imagine (când schimbi codul)
docker build -t signals-bot .

Run agenți (produce signals + audit + state)
docker run --rm -v "$PWD:/app" signals-bot

Vezi output rapid
cat signals.txt

📅 Earnings calendar
Editezi calendarul manual
nano earnings_calendar.json


Format recomandat:

{
  "NVDA": ["2026-02-09T21:00:00Z", "2026-05-20T21:00:00Z"]
}

Verifici calendarul (helper)

👉 asta e comanda importantă, ruleaz-o regulat

docker run --rm -v "$PWD:/app" -w /app signals-bot \
  python -m signals_bot.cli.check_calendar


Output posibil:

OK → totul bine

EXPIRED → ai tickere cu doar date în trecut

MISSING → ticker din watchlist lipsește din calendar

Exit codes:

0 = ok

2 = calendar problem (bun pt CI / cron mai târziu)

🧠 State & Policy
Vezi state-ul curent (cooldown / buys)
cat data/state.json

Reset manual state (test / zi nouă)
rm -f data/state.json

🧾 Audit log
Vezi ultimele decizii
tail -n 20 logs/decisions.jsonl

Filtrezi doar BUY-uri
grep '"final": "Action.BUY"' logs/decisions.jsonl

Filtrezi blocări de news
grep BLOCKED_BY_NEWS logs/decisions.jsonl

🧪 Debug rapid (fără rebuild)
Test mount volume
docker run --rm -v "$PWD:/app" alpine sh -c "echo ok > /app/_test.txt"
ls _test.txt

Intri în container (debug)
docker run --rm -it -v "$PWD:/app" signals-bot bash

🔁 Flow zilnic recomandat (real life)
docker build -t signals-bot .        # doar când modifici cod
docker run --rm -v "$PWD:/app" signals-bot
cat signals.txt
docker run --rm -v "$PWD:/app" -w /app signals-bot \
  python -m signals_bot.cli.check_calendar

🧠 Ce NU trebuie să faci

----

Poți copia direct secțiunea asta în README.md.

🔁 Simulations / Backtesting (decision-based)

Acest proiect suportă rularea agenților “as-of” (time-travel), pentru a vedea ce semnale ar fi emis sistemul într-o perioadă istorică, fără a folosi date live sau a afecta state-ul real.

🧱 Prerequisite

Imaginea Docker trebuie să fie build-uită:

docker build -t signals-bot .

▶️ Run simulation for a specific day (as-of)

Rulează toți agenții (News + Market + Governor) ca și cum “azi” ar fi o anumită dată:

docker run --rm -v "$PWD:/app" -w /app signals-bot \
  python -m signals_bot.cli.run_asof --asof 2025-09-18


Output:

asof_2025-09-18.signals.txt

asof_2025-09-18.signals.json

audit appended în logs/sim_decisions.jsonl

state separat în data/sim_state.json

▶️ Run simulation for a date range (daily loop)

Exemplu: 15 Sept 2025 → 07 Feb 2026

START=2025-09-15
END=2026-02-07

d="$START"
while [ "$d" != "$(date -I -d "$END + 1 day")" ]; do
  docker run --rm -v "$PWD:/app" -w /app signals-bot \
    python -m signals_bot.cli.run_asof \
      --asof "$d" \
      --out-prefix sim_outputs/AAPL
  d=$(date -I -d "$d + 1 day")
done


Output:

fișiere zilnice în sim_outputs/ (opțional)

audit unic: logs/sim_decisions.jsonl (sursa principală pentru statistici)

📊 Extract statistics: BUY → SELL periods

Pentru a obține perioadele de hold (BUY → SELL) pentru un ticker:

docker run --rm -v "$PWD:/app" -w /app signals-bot \
  python -m signals_bot.cli.stats_periods --ticker AAPL


Exemplu output:

[1] BUY 2025-09-18 -> SELL 2025-10-10 | holding: 22 days
[open] BUY 2026-01-30 -> (no SELL yet) | holding so far: 8 days


Cu lista completă a zilelor:

python -m signals_bot.cli.stats_periods --ticker AAPL --list-days


Cu interval limitat:

python -m signals_bot.cli.stats_periods \
  --ticker AAPL --start 2025-09-15 --end 2026-02-07

🗂 Important notes

Simulările folosesc:

date zilnice (1D) din Yahoo Finance

calendar local de earnings (earnings_calendar.json)

Policy (cooldown / max buys) poate fi dezactivat sau relaxat pentru statistici

Simulările nu afectează state-ul live

Sursa de adevăr pentru analiză este:

logs/sim_decisions.jsonl
