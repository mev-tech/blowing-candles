import sys
from pathlib import Path
from datetime import datetime, timezone

import pytest

from signals_bot.core.models import FinalSignal


def test_run_asof_cli(monkeypatch, tmp_path):
    # prepare minimal config in cwd
    cfg = {
        "watchlist": ["AAPL"],
        "news": {"earnings_block_hours": 48, "local_earnings_calendar": None, "ttl_minutes": 180},
        "policy": {"max_buys_per_day": 2, "cooldown_minutes": 240},
        "output": {"text_file": "signals.txt", "json_file": "signals.json"},
        "state": {"path": "data/state.json"},
        "audit": {"jsonl_path": "logs/decisions.jsonl"},
    }
    (tmp_path / "config.yaml").write_text(str(cfg))

    # monkeypatch cwd to tmp_path
    monkeypatch.chdir(tmp_path)

    # monkeypatch TradeGovernor.decide to avoid running analyst/sentinel
    def fake_decide(self, news, market, as_of=None):
        return [FinalSignal(ticker="AAPL", action="BUY", news_state="TRADE_OK", market_action="BUY", timestamp=datetime.now(timezone.utc))]

    monkeypatch.setattr("signals_bot.agents.trade_governor.TradeGovernor.decide", fake_decide)

    # run module
    sys.argv = ["run_asof", "--asof", "2025-01-01", "--out-prefix", "testasof", "--sim-state", str(tmp_path/"state.json"), "--sim-audit", str(tmp_path/"audit.jsonl")]
    from signals_bot.cli.run_asof import main

    # Should not raise
    main()

    # outputs created
    assert (tmp_path / "testasof_2025-01-01.signals.txt").exists()
    assert (tmp_path / "testasof_2025-01-01.signals.json").exists()
