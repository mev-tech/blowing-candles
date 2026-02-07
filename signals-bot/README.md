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