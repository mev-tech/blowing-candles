from pathlib import Path
import json
import yaml

from signals_bot.agents.news_sentinel import NewsSentinel
from signals_bot.agents.trade_governor import TradeGovernor

def main():
    cfg = yaml.safe_load(Path("config.yaml").read_text())
    watchlist = cfg["watchlist"]

    local_cal = cfg["news"].get("local_earnings_calendar")

    sentinel = NewsSentinel(
        earnings_block_hours=cfg["news"]["earnings_block_hours"],
        local_calendar_path=local_cal
    )
    governor = TradeGovernor(news_ttl_minutes=cfg["news"]["ttl_minutes"])

    news = sentinel.check(watchlist)
    final = governor.gate(news)

    lines = []
    for f in final:
        line = f"{f.ticker}  {f.action}  ({f.news_state}"
        if f.reason_codes:
            line += f";{','.join(f.reason_codes)}"
        line += ")"
        lines.append(line)

    out_txt = cfg["output"]["text_file"]
    out_json = cfg["output"]["json_file"]

    Path(out_txt).write_text("\n".join(lines) + "\n")
    Path(out_json).write_text(json.dumps([f.model_dump(mode="json") for f in final], indent=2) + "\n")

    print("\n".join(lines))
    print(f"\nWrote: {out_txt}, {out_json}")

if __name__ == "__main__":
    main()
