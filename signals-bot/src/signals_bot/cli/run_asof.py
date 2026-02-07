from __future__ import annotations
from pathlib import Path
from datetime import datetime, timezone
import json
import yaml

from signals_bot.agents.news_sentinel import NewsSentinel
from signals_bot.agents.market_analyst import MarketAnalyst
from signals_bot.agents.trade_governor import TradeGovernor
from signals_bot.core.models import Action, NewsState
from signals_bot.shared.state_store import StateStore
from signals_bot.shared.audit import append_jsonl, utc_now_iso

def _as_utc_dt(date_str: str) -> datetime:
    # interpret YYYY-MM-DD as end-of-day UTC? we'll use noon UTC to avoid edge timezone issues
    dt = datetime.fromisoformat(date_str)
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt.astimezone(timezone.utc)

def main():
    cfg = yaml.safe_load(Path("config.yaml").read_text())

    # read as_of from env or config? simplest: file "asof.txt" or env
    # but we will parse CLI args via module -m by reading argv quickly
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument("--asof", required=True, help="YYYY-MM-DD (treated as UTC date)")
    ap.add_argument("--out-prefix", default="asof", help="prefix for outputs")
    ap.add_argument("--sim-state", default="data/sim_state.json", help="state file for simulations (keeps your real state intact)")
    ap.add_argument("--sim-audit", default="logs/sim_decisions.jsonl", help="audit jsonl for simulations")
    args = ap.parse_args()

    as_of = _as_utc_dt(args.asof)

    watchlist = cfg["watchlist"]
    local_cal = cfg["news"].get("local_earnings_calendar")

    sentinel = NewsSentinel(
        earnings_block_hours=cfg["news"]["earnings_block_hours"],
        local_calendar_path=local_cal
    )
    analyst = MarketAnalyst(lookback_days=365)

    # Use separate state/audit so you don't pollute live state
    store = StateStore(Path(args.sim_state))

    governor = TradeGovernor(
        news_ttl_minutes=cfg["news"]["ttl_minutes"],
        max_buys_per_day=cfg.get("policy", {}).get("max_buys_per_day", 2),
        cooldown_minutes=cfg.get("policy", {}).get("cooldown_minutes", 240),
        state_store=store
    )

    news = sentinel.check(watchlist, as_of=as_of)
    market = analyst.analyze(watchlist, as_of=as_of)
    final = governor.decide(news, market, as_of=as_of)

    lines = []
    payload = []
    audit_rows = []

    for f in final:
        flags = []
        if "DATA_STALE" in (f.reason_codes or []):
            flags.append("BLOCKED_BY_STALE_DATA")

        if f.market_action in (Action.BUY, Action.SELL) and f.action != f.market_action:
            if f.news_state in (NewsState.NO_TRADE, NewsState.WAIT):
                flags.append("BLOCKED_BY_NEWS")

        line = (
            f"{f.ticker}  FINAL={f.action}  "
            f"(market={f.market_action};news={f.news_state};score={f.score}"
        )
        if flags:
            line += f";flags={','.join(flags)}"
        if f.reason_codes:
            line += f";{','.join(f.reason_codes)}"
        line += ")"
        lines.append(line)

        d = f.model_dump(mode="json")
        d["flags"] = flags
        payload.append(d)

        audit_rows.append({
            "ts": as_of.isoformat().replace("+00:00", "Z"),
            "ticker": f.ticker,
            "final": str(f.action),
            "market": str(f.market_action),
            "news": str(f.news_state),
            "score": f.score,
            "flags": flags,
            "reasons": f.reason_codes,
        })

    out_txt = f"{args.out_prefix}_{args.asof}.signals.txt"
    out_json = f"{args.out_prefix}_{args.asof}.signals.json"

    Path(out_txt).write_text("\n".join(lines) + "\n")
    Path(out_json).write_text(json.dumps(payload, indent=2) + "\n")
    append_jsonl(args.sim_audit, audit_rows)

    print("\n".join(lines))
    print(f"\nWrote: {out_txt}, {out_json}")
    print(f"Sim audit: {args.sim_audit}")
    print(f"Sim state: {args.sim_state}")

if __name__ == "__main__":
    main()
