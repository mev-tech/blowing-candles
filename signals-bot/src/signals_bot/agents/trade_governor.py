from datetime import datetime, timedelta, timezone
from typing import List, Optional

from signals_bot.core.models import (
    NewsSignal, NewsState,
    MarketSignal, Action,
    FinalSignal, Confidence
)
from signals_bot.shared.state_store import StateStore

class TradeGovernor:
    def __init__(
        self,
        news_ttl_minutes: int = 180,
        max_buys_per_day: int = 2,
        cooldown_minutes: int = 240,
        entry_mode: str = "balanced",  # opportunistic | balanced | conservative
        state_store: StateStore | None = None,
    ):
        self.ttl = timedelta(minutes=news_ttl_minutes)
        self.max_buys_per_day = max_buys_per_day
        self.cooldown = timedelta(minutes=cooldown_minutes)
        self.entry_mode = (entry_mode or "balanced").lower()
        self.state_store = state_store

    def _utc_now(self) -> datetime:
        return datetime.now(timezone.utc)

    def decide(
        self,
        news_signals: List[NewsSignal],
        market_signals: List[MarketSignal],
        as_of: Optional[datetime] = None
    ) -> List[FinalSignal]:

        now = as_of.astimezone(timezone.utc) if as_of else self._utc_now()

        news_by = {n.ticker: n for n in news_signals}
        mkt_by = {m.ticker: m for m in market_signals}
        tickers = sorted(set(news_by.keys()) | set(mkt_by.keys()))

        state = None
        if self.state_store:
            state = self.state_store.reset_if_new_day(self.state_store.load())
            buys_today = self.state_store.get_buys_today(state)
            last_buy_at = self.state_store.get_last_buy_at(state)
        else:
            buys_today = 0
            last_buy_at = None

        final: List[FinalSignal] = []
        buy_executed_this_run = False

        for t in tickers:
            ns = news_by.get(t)
            ms = mkt_by.get(t)

            market_action = ms.action if ms else Action.WAIT
            score = ms.score if ms else 0
            conf = ms.confidence if ms else Confidence.NA
            tags = list(ms.tags) if (ms and ms.tags) else []
            reasons: List[str] = []
            if ms and ms.reason_codes:
                reasons.extend(ms.reason_codes)

            if ns is None:
                final.append(FinalSignal(
                    ticker=t,
                    action=Action.WAIT,
                    news_state=NewsState.WAIT,
                    market_action=market_action,
                    score=score,
                    confidence=conf,
                    tags=tags,
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
                    confidence=conf,
                    tags=tags + ["BLOCKED_BY_STALE_DATA"],
                    reason_codes=["DATA_STALE"] + (ns.reason_codes or []) + reasons,
                    timestamp=now
                ))
                continue

            merged_reasons = (ns.reason_codes or []) + reasons

            # NEWS GATE
            if ns.state == NewsState.NO_TRADE:
                action = Action.IGNORE
                tags.append("BLOCKED_BY_NEWS")
            elif ns.state == NewsState.WAIT:
                action = Action.WAIT
                tags.append("BLOCKED_BY_NEWS")
            else:
                # TRADE_OK
                action = market_action

                # entry_mode behavior for LOW confidence BUY
                if action == Action.BUY and conf == Confidence.LOW:
                    if self.entry_mode == "conservative":
                        action = Action.WAIT
                        tags.append("LOW_CONFIDENCE_BLOCKED")
                    else:
                        # balanced/opportunistic: keep BUY, but tagged (already)
                        tags.append("LOW_CONFIDENCE_ALLOWED")

                # POLICY: limit BUY frequency (still applies unless you disable policy in config)
                if action == Action.BUY:
                    if buys_today >= self.max_buys_per_day:
                        action = Action.WAIT
                        merged_reasons = ["MAX_BUYS_REACHED"] + merged_reasons
                    else:
                        if last_buy_at is not None and (now - last_buy_at) < self.cooldown:
                            action = Action.WAIT
                            merged_reasons = ["COOLDOWN_ACTIVE"] + merged_reasons
                        else:
                            if not buy_executed_this_run:
                                buy_executed_this_run = True

            final.append(FinalSignal(
                ticker=t,
                action=action,
                news_state=ns.state,
                market_action=market_action,
                score=score,
                confidence=conf,
                tags=tags,
                reason_codes=merged_reasons,
                timestamp=now
            ))

        if self.state_store and state is not None and buy_executed_this_run:
            state = self.state_store.record_buy(state, when=now)
            self.state_store.save(state)

        return final
