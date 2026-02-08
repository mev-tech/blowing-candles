import json
from pathlib import Path
import logging
import sys

import yaml

from signals_bot.agents.market_analyst import MarketAnalyst
from signals_bot.agents.news_sentinel import NewsSentinel
from signals_bot.agents.trade_governor import TradeGovernor
from signals_bot.shared.audit import append_jsonl
from signals_bot.shared.state_store import StateStore


logger = logging.getLogger(__name__)


def _atomic_write(path: Path, text: str, encoding: str = "utf-8") -> None:
    tmp = path.parent / (path.name + ".tmp")
    tmp.write_text(text, encoding=encoding)
    tmp.replace(path)


def _validate_cfg(cfg: dict) -> None:
    required = [
        ("watchlist",),
        ("news", "earnings_block_hours"),
        ("news", "ttl_minutes"),
        ("state", "path"),
        ("policy", "max_buys_per_day"),
        ("policy", "cooldown_minutes"),
        ("output", "text_file"),
        ("output", "json_file"),
        ("audit", "jsonl_path"),
    ]
    for keys in required:
        node = cfg
        for k in keys:
            if not isinstance(node, dict) or k not in node:
                raise KeyError(f"Missing required config key: {'.'.join(keys)}")
            node = node[k]


def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    try:
        cfg_text = Path("config.yaml").read_text(encoding="utf-8")
    except FileNotFoundError:
        logger.error("config.yaml not found in working directory")
        sys.exit(2)

    try:
        cfg: dict = yaml.safe_load(cfg_text)
    except Exception:
        logger.exception("Failed to parse config.yaml")
        sys.exit(2)

    try:
        _validate_cfg(cfg)
    except KeyError as e:
        logger.error(str(e))
        sys.exit(2)

    watchlist: list[str] = cfg["watchlist"]

    local_cal: str | None = cfg["news"].get("local_earnings_calendar")
    strategy: dict = cfg.get("strategy", {})
    entry_mode: str = strategy.get("entry_mode", "balanced")
    buy_high: int = int(strategy.get("buy_high_score", 80))
    buy_low: int = int(strategy.get("buy_low_score", 65))

    sentinel = NewsSentinel(
        earnings_block_hours=cfg["news"]["earnings_block_hours"],
        local_calendar_path=local_cal,
    )

    analyst = MarketAnalyst(
        lookback_days=365, buy_high_score=buy_high, buy_low_score=buy_low
    )

    store = StateStore(Path(cfg["state"]["path"]))
    governor = TradeGovernor(
        news_ttl_minutes=cfg["news"]["ttl_minutes"],
        max_buys_per_day=cfg["policy"]["max_buys_per_day"],
        cooldown_minutes=cfg["policy"]["cooldown_minutes"],
        entry_mode=entry_mode,
        state_store=store,
    )

    news = sentinel.check(watchlist)
    market = analyst.analyze(watchlist)
    final = governor.decide(news, market)

    lines: list[str] = []
    payload: list[dict] = []
    audit_rows: list[dict] = []

    for sig in final:
        line = (
            f"{sig.ticker}  FINAL={sig.action}  (market={sig.market_action};news={sig.news_state};"
            f"score={sig.score};conf={sig.confidence}"
        )
        if getattr(sig, "tags", None):
            line += f";tags={','.join(sig.tags)}"
        if getattr(sig, "reason_codes", None):
            line += f";reasons={','.join(sig.reason_codes)}"
        line += ")"
        lines.append(line)

        try:
            payload.append(sig.model_dump(mode="json"))
        except Exception:
            logger.exception("model_dump failed for %s, falling back to __dict__", getattr(sig, "ticker", "<unknown>"))
            # best-effort fallback
            try:
                payload.append({k: v for k, v in sig.__dict__.items() if not k.startswith("_")})
            except Exception:
                payload.append({})

        audit_rows.append(
            {
                "ts": sig.timestamp.isoformat().replace("+00:00", "Z"),
                "ticker": sig.ticker,
                "final": str(sig.action),
                "market": str(sig.market_action),
                "news": str(sig.news_state),
                "score": sig.score,
                "confidence": str(sig.confidence),
                "tags": sig.tags,
                "reasons": sig.reason_codes,
            }
        )

    out_txt: str = cfg["output"]["text_file"]
    out_json: str = cfg["output"]["json_file"]

    try:
        _atomic_write(Path(out_txt), "\n".join(lines) + "\n")
        _atomic_write(Path(out_json), json.dumps(payload, indent=2) + "\n")
    except Exception:
        logger.exception("Failed to write output files")
        sys.exit(3)

    # append_jsonl accepts a list of rows
    append_jsonl(cfg["audit"]["jsonl_path"], audit_rows)

    logger.info("\n".join(lines))
    logger.info("Wrote: %s, %s", out_txt, out_json)


if __name__ == "__main__":
    main()
