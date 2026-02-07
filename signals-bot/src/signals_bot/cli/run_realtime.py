from pathlib import Path
import json
import yaml

from signals_bot.agents.news_sentinel import NewsSentinel
from signals_bot.agents.market_analyst import MarketAnalyst
from signals_bot.agents.trade_governor import TradeGovernor
from signals_bot.core.models import Action, NewsState
from signals_bot.shared.state_store import StateStore
from signals_bot.shared.audit import append_jsonl, utc_now_iso

def main():
    cfg = yaml.safe_load(Path("config.yaml").read_text())
    watchlist = cfg["watchlist"]

    local_cal = cfg["news"].get("local_earnings_calendar")

    sentinel = NewsSentinel(
        earnings_block_hours=cfg["news"]["earnings_block_hours"],
        local_calendar_path=local_cal
    )
    analyst = MarketAnalyst(lookback_days=365)

    state_path = cfg.get("state", {}).get("path", "data/state.json")
    store = StateStore(Path(state_path))

    governor = TradeGovernor(
        news_ttl_minutes=cfg["news"]["ttl_minutes"],
        max_buys_per_day=cfg.get("policy", {}).get("max_buys_per_day", 2),
        cooldown_minutes=cfg.get("policy", {}).get("cooldown_minutes", 240),
        state_store=store
    )

    news = sentinel.check(watchlist)
    market = analyst.analyze(watchlist)
    final = governor.decide(news, market)

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
            "ts": utc_now_iso(),
            "ticker": f.ticker,
            "final": str(f.action),
            "market": str(f.market_action),
            "news": str(f.news_state),
            "score": f.score,
            "flags": flags,
            "reasons": f.reason_codes,
        })

    out_txt = cfg["output"]["text_file"]
    out_json = cfg["output"]["json_file"]
    audit_path = cfg.get("audit", {}).get("jsonl_path", "logs/decisions.jsonl")

    Path(out_txt).write_text("\n".join(lines) + "\n")
    Path(out_json).write_text(json.dumps(payload, indent=2) + "\n")
    append_jsonl(audit_path, audit_rows)

    print("\n".join(lines))
    print(f"\nWrote: {out_txt}, {out_json}")
    print(f"Audit: {audit_path}")

if __name__ == "__main__":
    main()
