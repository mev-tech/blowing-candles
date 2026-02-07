from datetime import datetime, timedelta, timezone
from typing import List, Dict

from signals_bot.core.models import (
    NewsSignal, NewsState,
    MarketSignal, Action,
    FinalSignal
)

class TradeGovernor:
    def __init__(self, news_ttl_minutes: int = 180):
        self.ttl = timedelta(minutes=news_ttl_minutes)

    def decide(self, news_signals: List[NewsSignal], market_signals: List[MarketSignal]) -> List[FinalSignal]:
        now = datetime.now(timezone.utc)
        news_by = {n.ticker: n for n in news_signals}
        mkt_by = {m.ticker: m for m in market_signals}

        tickers = sorted(set(news_by.keys()) | set(mkt_by.keys()))
        final: List[FinalSignal] = []

        for t in tickers:
            ns = news_by.get(t)
            ms = mkt_by.get(t)

            # default market
            market_action = ms.action if ms else Action.WAIT
            score = ms.score if ms else 0
            reasons: List[str] = []
            if ms and ms.reason_codes:
                reasons.extend(ms.reason_codes)

            # fail-safe if no news
            if ns is None:
                final.append(FinalSignal(
                    ticker=t,
                    action=Action.WAIT,
                    news_state=NewsState.WAIT,
                    market_action=market_action,
                    score=score,
                    reason_codes=["NO_NEWS_STATE"] + reasons,
                    timestamp=now
                ))
                continue

            # stale news -> WAIT
            if (now - ns.timestamp) > self.ttl:
                final.append(FinalSignal(
                    ticker=t,
                    action=Action.WAIT,
                    news_state=NewsState.WAIT,
                    market_action=market_action,
                    score=score,
                    reason_codes=["DATA_STALE"] + reasons,
                    timestamp=now
                ))
                continue

            # Merge reason codes
            reasons = (ns.reason_codes or []) + reasons

            # POLICY MATRIX:
            # 1) If NO_TRADE -> ignore any BUY/SELL
            if ns.state == NewsState.NO_TRADE:
                action = Action.IGNORE

            # 2) If WAIT -> defer
            elif ns.state == NewsState.WAIT:
                action = Action.WAIT

            # 3) TRADE_OK -> allow market decision
            else:
                action = market_action

            final.append(FinalSignal(
                ticker=t,
                action=action,
                news_state=ns.state,
                market_action=market_action,
                score=score,
                reason_codes=reasons,
                timestamp=now
            ))

        return final
