from datetime import datetime, timezone, timedelta

from signals_bot.core.models import NewsSignal, MarketSignal, FinalSignal, NewsState, Action, Confidence
from signals_bot.agents.trade_governor import TradeGovernor
from signals_bot.shared.state_store import StateStore


def make_market(ticker: str, action: Action, score: int = 80, conf: Confidence = Confidence.HIGH):
    return MarketSignal(ticker=ticker, action=action, score=score, confidence=conf, timestamp=datetime.now(timezone.utc))


def make_news(ticker: str, state: NewsState, as_of: datetime):
    return NewsSignal(ticker=ticker, state=state, reason_codes=[], timestamp=as_of)


def test_trade_governor_buy_limits(tmp_path):
    store_path = tmp_path / "state.json"
    store = StateStore(store_path)
    tg = TradeGovernor(news_ttl_minutes=180, max_buys_per_day=1, cooldown_minutes=0, entry_mode="balanced", state_store=store)

    now = datetime.now(timezone.utc)
    ms = [make_market("T1", Action.BUY)]
    ns = [make_news("T1", NewsState.TRADE_OK, now)]

    final = tg.decide(ns, ms, as_of=now)
    assert final[0].action == Action.BUY

    # second buy should be blocked by max_buys_per_day
    ms2 = [make_market("T2", Action.BUY)]
    ns2 = [make_news("T2", NewsState.TRADE_OK, now)]
    final2 = tg.decide(ns2, ms2, as_of=now)
    assert final2[0].action != Action.BUY
