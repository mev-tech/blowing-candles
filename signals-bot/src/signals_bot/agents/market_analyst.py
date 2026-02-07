from datetime import datetime, timezone
from typing import List

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
    """
    yfinance can return:
      A) columns: ['Open','High','Low','Close','Volume',...]
      B) MultiIndex columns: level0=Price field, level1=Ticker
    We normalize to a Series of closes.
    """
    if df is None or df.empty:
        raise ValueError("EMPTY_DF")

    # MultiIndex columns case
    if isinstance(df.columns, pd.MultiIndex):
        # Prefer ('Close', ticker)
        if ("Close", ticker) in df.columns:
            return df[("Close", ticker)].dropna()
        # Sometimes levels are swapped or tickers different case
        upper = ticker.upper()
        candidates = [c for c in df.columns if c[0] == "Close" and str(c[1]).upper() == upper]
        if candidates:
            return df[candidates[0]].dropna()
        raise KeyError("CLOSE_NOT_FOUND_MULTIINDEX")

    # Flat columns case
    if "Close" in df.columns:
        return df["Close"].dropna()

    raise KeyError("CLOSE_NOT_FOUND")

class MarketAnalyst:
    """
    MVP swing analyst (Daily):
      BUY if:
        Close > SMA200
        SMA50 > SMA200
        RSI14 > 50
      else WAIT
    """
    def __init__(self, lookback_days: int = 365):
        self.lookback_days = lookback_days

    def analyze(self, tickers: List[str]) -> List[MarketSignal]:
        now = datetime.now(timezone.utc)
        out: List[MarketSignal] = []

        for t in tickers:
            reasons = []
            score = 0
            action = Action.WAIT

            try:
                df = yf.download(
                    t,
                    period=f"{self.lookback_days}d",
                    interval="1d",
                    auto_adjust=True,
                    progress=False,
                    group_by="column",   # helps but still can be MultiIndex in some versions
                )

                close = _extract_close(df, t)

                # Need enough history for SMA200
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
