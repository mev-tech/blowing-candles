from datetime import datetime, timedelta, timezone
from typing import List

from signals_bot.core.models import NewsSignal, NewsState, Action, FinalSignal

class TradeGovernor:
    def __init__(self, news_ttl_minutes: int = 180):
        self.ttl = timedelta(minutes=news_ttl_minutes)

    def gate(self, news_signals: List[NewsSignal]) -> List[FinalSignal]:
        now = datetime.now(timezone.utc)
        final: List[FinalSignal] = []

        for ns in news_signals:
            # Stale -> WAIT
            if (now - ns.timestamp) > self.ttl:
                final.append(FinalSignal(
                    ticker=ns.ticker,
                    action=Action.WAIT,
                    news_state=NewsState.WAIT,
                    reason_codes=["DATA_STALE"],
                    timestamp=now
                ))
                continue

            # MVP: no market analyst yet -> output tradeability only
            if ns.state == NewsState.TRADE_OK:
                action = Action.WAIT
            elif ns.state == NewsState.NO_TRADE:
                action = Action.IGNORE
            else:
                action = Action.WAIT

            final.append(FinalSignal(
                ticker=ns.ticker,
                action=action,
                news_state=ns.state,
                reason_codes=ns.reason_codes,
                timestamp=now
            ))

        return final
