import sys
from pathlib import Path
from datetime import datetime, timezone

import pytest

from signals_bot.core.models import FinalSignal


def test_run_realtime_cli(monkeypatch, tmp_path):
    cfg = {
        "watchlist": ["AAPL"],
        "news": {"earnings_block_hours": 48, "local_earnings_calendar": None, "ttl_minutes": 180},
        "policy": {"max_buys_per_day": 2, "cooldown_minutes": 240},
        "output": {"text_file": "signals.txt", "json_file": "signals.json"},
        "state": {"path": "data/state.json"},
        "audit": {"jsonl_path": "logs/decisions.jsonl"},
    }
    (tmp_path / "config.yaml").write_text(str(cfg))
    monkeypatch.chdir(tmp_path)

    # fake decide returns FinalSignal list
    def fake_decide(self, news, market):
        return [FinalSignal(ticker="AAPL", action="BUY", news_state="TRADE_OK", market_action="BUY", timestamp=datetime.now(timezone.utc))]

    monkeypatch.setattr("signals_bot.agents.trade_governor.TradeGovernor.decide", fake_decide)

    from signals_bot.cli.run_realtime import main
    # run main (should write signals.txt/json)
    main()

    assert (tmp_path / "signals.txt").exists()
    assert (tmp_path / "signals.json").exists()
