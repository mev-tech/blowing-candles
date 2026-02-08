from datetime import datetime, timezone
from signals_bot.core.models import MarketSignal
from signals_bot.shared.state_store import StateStore


def test_pydantic_mutable_defaults():
    a = MarketSignal(ticker="A", action="WAIT", timestamp=datetime.now(timezone.utc))
    b = MarketSignal(ticker="B", action="WAIT", timestamp=datetime.now(timezone.utc))
    a.tags.append("X")
    assert a.tags == ["X"]
    assert b.tags == []


def test_state_store_atomic(tmp_path):
    p = tmp_path / "state.json"
    store = StateStore(p)
    s = store.load()
    store.save(s)
    assert p.exists()
    # rewrite with updated buys_today
    s2 = store.record_buy(s)
    store.save(s2)
    data = p.read_text()
    assert "buys_today" in data
