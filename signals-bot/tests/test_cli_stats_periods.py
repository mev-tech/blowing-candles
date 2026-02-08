import sys
import json
from pathlib import Path
from datetime import datetime

from signals_bot.cli.stats_periods import main


def test_stats_periods_basic(tmp_path, monkeypatch):
    p = tmp_path / "decisions.jsonl"
    rows = [
        {"ts": "2025-01-01T00:00:00Z", "ticker": "T1", "final": "Action.BUY"},
        {"ts": "2025-01-05T00:00:00Z", "ticker": "T1", "final": "Action.SELL"},
    ]
    p.write_text("\n".join(json.dumps(r) for r in rows) + "\n")

    sys.argv = ["stats_periods", "--jsonl", str(p), "--ticker", "T1"]
    # Should not raise
    main()
