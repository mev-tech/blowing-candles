from datetime import datetime, timezone, timedelta
from typing import List, Optional

import yfinance as yf
import pandas as pd

from signals_bot.core.models import MarketSignal, Action

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
    MVP swing analyst (Daily):
      BUY if score >= 80 using:
        Close > SMA200 (40)
        SMA50 > SMA200 (30)
        RSI14 > 50 (30)
      SELL if:
        Close < SMA200 OR RSI14 < 40
    """
    def __init__(self, lookback_days: int = 365):
        self.lookback_days = lookback_days

    def analyze(self, tickers: List[str], as_of: Optional[datetime] = None) -> List[MarketSignal]:
        # as_of is treated as "now" for backtests
        now = as_of.astimezone(timezone.utc) if as_of else datetime.now(timezone.utc)

        out: List[MarketSignal] = []
        end_dt = (now + timedelta(days=1)).date().isoformat()  # include as_of day
        start_dt = (now - timedelta(days=self.lookback_days)).date().isoformat()

        for t in tickers:
            reasons = []
            score = 0
            action = Action.WAIT

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
                        ticker=t,
                        action=Action.WAIT,
                        score=0,
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

                # SELL conditions (risk-first)
                if c < s200:
                    reasons.append("BELOW_SMA200")
                if r < 40:
                    reasons.append("RSI_VERY_WEAK")

                if ("BELOW_SMA200" in reasons) or ("RSI_VERY_WEAK" in reasons):
                    action = Action.SELL
                    score = 0
                else:
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

                    action = Action.BUY if score >= 80 else Action.WAIT

            except Exception as e:
                action = Action.WAIT
                score = 0
                reasons = ["MARKET_DATA_ERROR", type(e).__name__]

            out.append(MarketSignal(
                ticker=t,
                action=action,
                score=score,
                reason_codes=reasons,
                timestamp=now
            ))

        return out
