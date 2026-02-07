from pathlib import Path
import json
from datetime import datetime, timezone
import yaml

from signals_bot.agents.news_sentinel import NewsSentinel
from signals_bot.agents.market_analyst import MarketAnalyst
from signals_bot.agents.trade_governor import TradeGovernor
from signals_bot.shared.state_store import StateStore
from signals_bot.shared.audit import append_jsonl

def main():
    cfg = yaml.safe_load(Path("config.yaml").read_text())
    watchlist = cfg["watchlist"]

    local_cal = cfg["news"].get("local_earnings_calendar")
    strategy = cfg.get("strategy", {})
    entry_mode = strategy.get("entry_mode", "balanced")
    buy_high = int(strategy.get("buy_high_score", 80))
    buy_low = int(strategy.get("buy_low_score", 65))

    sentinel = NewsSentinel(
        earnings_block_hours=cfg["news"]["earnings_block_hours"],
        local_calendar_path=local_cal
    )

    analyst = MarketAnalyst(
        lookback_days=365,
        buy_high_score=buy_high,
        buy_low_score=buy_low
    )

    store = StateStore(Path(cfg["state"]["path"]))
    governor = TradeGovernor(
        news_ttl_minutes=cfg["news"]["ttl_minutes"],
        max_buys_per_day=cfg["policy"]["max_buys_per_day"],
        cooldown_minutes=cfg["policy"]["cooldown_minutes"],
        entry_mode=entry_mode,
        state_store=store
    )

    news = sentinel.check(watchlist)
    market = analyst.analyze(watchlist)
    final = governor.decide(news, market)

    lines = []
    payload = []
    audit_rows = []

    for f in final:
        line = f"{f.ticker}  FINAL={f.action}  (market={f.market_action};news={f.news_state};score={f.score};conf={f.confidence}"
        if f.tags:
            line += f";tags={','.join(f.tags)}"
        if f.reason_codes:
            line += f";reasons={','.join(f.reason_codes)}"
        line += ")"
        lines.append(line)

        payload.append(f.model_dump(mode="json"))
        audit_rows.append({
            "ts": f.timestamp.isoformat().replace("+00:00", "Z"),
            "ticker": f.ticker,
            "final": str(f.action),
            "market": str(f.market_action),
            "news": str(f.news_state),
            "score": f.score,
            "confidence": str(f.confidence),
            "tags": f.tags,
            "reasons": f.reason_codes,
        })

    out_txt = cfg["output"]["text_file"]
    out_json = cfg["output"]["json_file"]

    Path(out_txt).write_text("\n".join(lines) + "\n")
    Path(out_json).write_text(json.dumps(payload, indent=2) + "\n")
    append_jsonl(cfg["audit"]["jsonl_path"], audit_rows)

    print("\n".join(lines))
    print(f"\nWrote: {out_txt}, {out_json}")

if __name__ == "__main__":
    main()
