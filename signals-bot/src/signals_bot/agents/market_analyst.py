from datetime import datetime, timezone, timedelta
from typing import List, Optional

import yfinance as yf
import pandas as pd

from signals_bot.core.models import MarketSignal, Action, Confidence

def _rsi(series: pd.Series, period: int = 14) -> pd.Series:
    delta = series.diff()
    gain = delta.clip(lower=0)
    loss = (-delta.clip(upper=0))

    avg_gain = gain.rolling(period, min_periods=period).mean()
    avg_loss = loss.rolling(period, min_periods=period).mean()

    rs = avg_gain / avg_loss.replace(0, pd.NA)
    rsi = 100 - (100 / (1 + rs))
    return rsi.fillna(0)

def _extract_close(df: pd.DataFrame, ticker: str) -> pd.Series:
    if df is None or df.empty:
        raise ValueError("EMPTY_DF")

    if isinstance(df.columns, pd.MultiIndex):
        if ("Close", ticker) in df.columns:
            return df[("Close", ticker)].dropna()
        upper = ticker.upper()
        candidates = [c for c in df.columns if c[0] == "Close" and str(c[1]).upper() == upper]
        if candidates:
            return df[candidates[0]].dropna()
        raise KeyError("CLOSE_NOT_FOUND_MULTIINDEX")

    if "Close" in df.columns:
        return df["Close"].dropna()

    raise KeyError("CLOSE_NOT_FOUND")

class MarketAnalyst:
    """
    Daily swing analyst.
    Outputs:
      - score (0..100)
      - confidence HIGH/LOW for BUY candidates
      - tags: e.g. LOW_CONFIDENCE_BUY, RSI_WEAK, BELOW_SMA200
    """
    def __init__(self, lookback_days: int = 365, buy_high_score: int = 80, buy_low_score: int = 65):
        self.lookback_days = lookback_days
        self.buy_high_score = buy_high_score
        self.buy_low_score = buy_low_score

    def analyze(self, tickers: List[str], as_of: Optional[datetime] = None) -> List[MarketSignal]:
        now = as_of.astimezone(timezone.utc) if as_of else datetime.now(timezone.utc)
        end_dt = (now + timedelta(days=1)).date().isoformat()  # include as_of day
        start_dt = (now - timedelta(days=self.lookback_days)).date().isoformat()

        out: List[MarketSignal] = []

        for t in tickers:
            reasons: List[str] = []
            tags: List[str] = []
            score = 0
            action = Action.WAIT
            conf = Confidence.NA

            try:
                df = yf.download(
                    t,
                    start=start_dt,
                    end=end_dt,
                    interval="1d",
                    auto_adjust=True,
                    progress=False,
                    group_by="column",
                )
                close = _extract_close(df, t)

                if len(close) < 210:
                    out.append(MarketSignal(
                        ticker=t, action=Action.WAIT, score=0,
                        confidence=Confidence.NA,
                        tags=["NOT_ENOUGH_HISTORY"],
                        reason_codes=["NOT_ENOUGH_HISTORY"],
                        timestamp=now
                    ))
                    continue

                sma50 = close.rolling(50).mean()
                sma200 = close.rolling(200).mean()
                rsi14 = _rsi(close, 14)

                c = float(close.iloc[-1])
                s50 = float(sma50.iloc[-1])
                s200 = float(sma200.iloc[-1])
                r = float(rsi14.iloc[-1])

                # SELL (risk-first) - unchanged
                if c < s200:
                    reasons.append("BELOW_SMA200")
                if r < 40:
                    reasons.append("RSI_VERY_WEAK")

                if ("BELOW_SMA200" in reasons) or ("RSI_VERY_WEAK" in reasons):
                    action = Action.SELL
                    conf = Confidence.NA
                    score = 0
                else:
                    # Score components (unchanged)
                    if c > s200:
                        score += 40
                    else:
                        reasons.append("BELOW_SMA200")

                    if s50 > s200:
                        score += 30
                    else:
                        reasons.append("SMA50_BELOW_SMA200")

                    if r > 50:
                        score += 30
                    else:
                        reasons.append("RSI_WEAK")

                    # Confidence tiers
                    if score >= self.buy_high_score:
                        action = Action.BUY
                        conf = Confidence.HIGH
                    elif score >= self.buy_low_score:
                        # BUY candidate but low confidence
                        action = Action.BUY
                        conf = Confidence.LOW
                        tags.append("LOW_CONFIDENCE_BUY")
                    else:
                        action = Action.WAIT
                        conf = Confidence.NA

                # Helpful tags (optional but nice)
                if "RSI_WEAK" in reasons:
                    tags.append("MOMENTUM_WEAK")
                if "BELOW_SMA200" in reasons:
                    tags.append("TREND_WEAK")

            except Exception as e:
                action = Action.WAIT
                score = 0
                conf = Confidence.NA
                reasons = ["MARKET_DATA_ERROR", type(e).__name__]
                tags = ["MARKET_DATA_ERROR"]

            out.append(MarketSignal(
                ticker=t,
                action=action,
                score=score,
                confidence=conf,
                tags=tags,
                reason_codes=reasons,
                timestamp=now
            ))

        return out
