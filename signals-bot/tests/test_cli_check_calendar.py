import sys
import json
from datetime import datetime, timezone, timedelta

import pytest

from signals_bot.cli.check_calendar import main


def test_check_calendar_expired_and_missing(tmp_path, monkeypatch):
    # config with two tickers, one present in calendar but expired, one missing
    now = datetime.now(timezone.utc)
    past = (now - timedelta(days=2)).isoformat().replace("+00:00", "Z")
    cal = {"EXPIRED": past}
    cal_path = tmp_path / "cal.json"
    cal_path.write_text(json.dumps(cal))

    cfg = {
        "watchlist": ["EXPIRED", "MISSING"],
        "news": {"local_earnings_calendar": str(cal_path)}
    }
    (tmp_path / "config.yaml").write_text(str(cfg))
    monkeypatch.chdir(tmp_path)

    with pytest.raises(SystemExit) as ei:
        main()
    assert ei.value.code == 2
