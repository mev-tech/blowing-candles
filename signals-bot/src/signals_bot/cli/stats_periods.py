from __future__ import annotations

import argparse
import json
from pathlib import Path
from datetime import date, timedelta
from typing import List, Dict, Any, Optional, Tuple

def parse_jsonl(path: str) -> List[Dict[str, Any]]:
    rows = []
    p = Path(path)
    if not p.exists():
        raise SystemExit(f"JSONL not found: {path}")
    for line in p.read_text().splitlines():
        line = line.strip()
        if not line:
            continue
        rows.append(json.loads(line))
    return rows

def to_date(ts: str) -> date:
    # ts like "2025-09-18T00:00:00Z" or with offset
    # We only need YYYY-MM-DD
    return date.fromisoformat(ts[:10])

def daterange(d1: date, d2: date) -> List[str]:
    out = []
    d = d1
    while d <= d2:
        out.append(d.isoformat())
        d += timedelta(days=1)
    return out

def normalize_action(s: str) -> str:
    # "Action.BUY" -> "BUY"
    if "." in s:
        return s.split(".")[-1]
    return s

def main():
    ap = argparse.ArgumentParser(description="Compute BUY->SELL holding periods from decisions.jsonl")
    ap.add_argument("--jsonl", default="logs/sim_decisions.jsonl", help="Path to audit JSONL")
    ap.add_argument("--ticker", required=True, help="Ticker to analyze (e.g., AAPL)")
    ap.add_argument("--start", default=None, help="Optional start date YYYY-MM-DD")
    ap.add_argument("--end", default=None, help="Optional end date YYYY-MM-DD")
    ap.add_argument("--list-days", action="store_true", help="Include every day in each period")
    args = ap.parse_args()

    rows = parse_jsonl(args.jsonl)
    tkr = args.ticker.upper()

    # Filter + sort by date
    data: List[Tuple[date, str]] = []
    for r in rows:
        if str(r.get("ticker", "")).upper() != tkr:
            continue
        ts = r.get("ts")
        if not ts:
            continue
        d = to_date(ts)
        act = normalize_action(str(r.get("final", "")))
        data.append((d, act))

    data.sort(key=lambda x: x[0])

    if args.start:
        sd = date.fromisoformat(args.start)
        data = [(d,a) for d,a in data if d >= sd]
    if args.end:
        ed = date.fromisoformat(args.end)
        data = [(d,a) for d,a in data if d <= ed]

    if not data:
        print(f"No data for {tkr} in {args.jsonl} within selected range.")
        raise SystemExit(0)

    periods = []
    open_buy: Optional[date] = None
    last_date = data[-1][0]

    for d, act in data:
        if act == "BUY":
            # If already in a position, ignore repeated BUYs (common in daily signals)
            if open_buy is None:
                open_buy = d
        elif act == "SELL":
            if open_buy is not None:
                buy_d = open_buy
                sell_d = d
                days = (sell_d - buy_d).days
                periods.append((buy_d, sell_d, days))
                open_buy = None

    # Print results
    print(f"Ticker: {tkr}")
    print(f"Range analyzed: {data[0][0].isoformat()} -> {data[-1][0].isoformat()}")
    print("")

    if not periods and open_buy is None:
        print("No BUY->SELL periods found.")
        raise SystemExit(0)

    for i, (b, s, days) in enumerate(periods, start=1):
        print(f"[{i}] BUY {b.isoformat()} -> SELL {s.isoformat()} | holding: {days} days")
        if args.list_days:
            print("    days:", ", ".join(daterange(b, s)))
        print("")

    if open_buy is not None:
        days_open = (last_date - open_buy).days
        print(f"[open] BUY {open_buy.isoformat()} -> (no SELL yet; last={last_date.isoformat()}) | holding so far: {days_open} days")
        if args.list_days:
            print("    days:", ", ".join(daterange(open_buy, last_date)))
        print("")

if __name__ == "__main__":
    main()
