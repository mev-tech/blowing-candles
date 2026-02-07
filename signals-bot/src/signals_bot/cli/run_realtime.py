from pathlib import Path
import json
import yaml

from signals_bot.agents.news_sentinel import NewsSentinel
from signals_bot.agents.market_analyst import MarketAnalyst
from signals_bot.agents.trade_governor import TradeGovernor
from signals_bot.core.models import Action, NewsState

def main():
    cfg = yaml.safe_load(Path("config.yaml").read_text())
    watchlist = cfg["watchlist"]

    local_cal = cfg["news"].get("local_earnings_calendar")

    sentinel = NewsSentinel(
        earnings_block_hours=cfg["news"]["earnings_block_hours"],
        local_calendar_path=local_cal
    )
    analyst = MarketAnalyst(lookback_days=365)
    governor = TradeGovernor(news_ttl_minutes=cfg["news"]["ttl_minutes"])

    news = sentinel.check(watchlist)
    market = analyst.analyze(watchlist)
    final = governor.decide(news, market)

    lines = []
    payload = []

    for f in final:
        flags = []

        # Blocked by stale data (governor forced WAIT due to stale/news missing)
        if "DATA_STALE" in (f.reason_codes or []):
            flags.append("BLOCKED_BY_STALE_DATA")

        # Blocked by news (market wanted action, final differs, and news isn't TRADE_OK)
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

    out_txt = cfg["output"]["text_file"]
    out_json = cfg["output"]["json_file"]

    Path(out_txt).write_text("\n".join(lines) + "\n")
    Path(out_json).write_text(json.dumps(payload, indent=2) + "\n")

    print("\n".join(lines))
    print(f"\nWrote: {out_txt}, {out_json}")

if __name__ == "__main__":
    main()
