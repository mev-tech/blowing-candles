import json
from datetime import datetime, timezone, timedelta
from pathlib import Path

import pytest

from signals_bot.agents.news_sentinel import NewsSentinel
from signals_bot.core.models import NewsState


def test_news_sentinel_local_calendar(tmp_path):
    now = datetime.now(timezone.utc)
    past = (now - timedelta(days=2)).isoformat().replace("+00:00", "Z")
    soon = (now + timedelta(hours=1)).isoformat().replace("+00:00", "Z")

    cal = {
        "PAST": past,
        "SOON": soon,
    }

    cal_path = tmp_path / "cal.json"
    cal_path.write_text(json.dumps(cal))

    sentinel = NewsSentinel(earnings_block_hours=48, local_calendar_path=str(cal_path))
    results = {r.ticker: r for r in sentinel.check(["PAST", "SOON"], as_of=now)}

    assert results["PAST"].state == NewsState.WAIT
    assert "CALENDAR_EXPIRED" in (results["PAST"].reason_codes or [])

    assert results["SOON"].state == NewsState.NO_TRADE
    assert "EARNINGS_LT_48H" in (results["SOON"].reason_codes or [])
