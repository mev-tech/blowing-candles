from datetime import datetime, timedelta, timezone
from typing import List, Optional, Dict
from pathlib import Path
import json

import yfinance as yf
import pandas as pd

from signals_bot.core.models import NewsSignal, NewsState

class NewsSentinel:
    def __init__(self, earnings_block_hours: int = 48, local_calendar_path: Optional[str] = None):
        self.earnings_block = timedelta(hours=earnings_block_hours)
        self.local_calendar_path = local_calendar_path
        self._local_calendar = self._load_local_calendar(local_calendar_path)

    def _load_local_calendar(self, path: Optional[str]) -> Dict[str, datetime]:
        if not path:
            return {}
        p = Path(path)
        if not p.exists():
            return {}
        raw = json.loads(p.read_text())
        out: Dict[str, datetime] = {}
        for ticker, iso in raw.items():
            try:
                dt = datetime.fromisoformat(iso.replace("Z", "+00:00"))
                out[ticker.upper()] = dt.astimezone(timezone.utc)
            except Exception:
                # skip bad entries
                continue
        return out

    def _extract_next_earnings_yf(self, t: str) -> Optional[datetime]:
        tk = yf.Ticker(t)

        # Prefer earnings dates table
        try:
            df = tk.get_earnings_dates(limit=8)
            if isinstance(df, pd.DataFrame) and not df.empty:
                idx = df.index
                if len(idx) > 0:
                    dt = idx[0]
                    if isinstance(dt, pd.Timestamp):
                        return dt.to_pydatetime()
        except Exception:
            pass

        # Fallback: old calendar field
        try:
            cal = tk.calendar
            if cal is not None and hasattr(cal, "empty") and not cal.empty:
                val = cal.iloc[0, 0]
                if isinstance(val, pd.Timestamp):
                    return val.to_pydatetime()
                if isinstance(val, datetime):
                    return val
        except Exception:
            pass

        return None

    def _get_next_earnings(self, t: str) -> Optional[datetime]:
        # 1) local calendar first (deterministic)
        dt = self._local_calendar.get(t.upper())
        if dt:
            return dt

        # 2) yfinance fallback (best-effort)
        dt2 = self._extract_next_earnings_yf(t)
        if dt2:
            if dt2.tzinfo is None:
                dt2 = dt2.replace(tzinfo=timezone.utc)
            return dt2.astimezone(timezone.utc)

        return None

    def check(self, tickers: List[str]) -> List[NewsSignal]:
        now = datetime.now(timezone.utc)
        out: List[NewsSignal] = []

        for t in tickers:
            state = NewsState.TRADE_OK
            reasons: List[str] = []
            valid_until = None

            try:
                earnings_dt = self._get_next_earnings(t)

                if earnings_dt is None:
                    state = NewsState.WAIT
                    reasons.append("DATA_UNAVAILABLE")
                else:
                    if earnings_dt.tzinfo is None:
                        earnings_dt = earnings_dt.replace(tzinfo=timezone.utc)

                    delta = earnings_dt - now
                    if timedelta(0) <= delta <= self.earnings_block:
                        state = NewsState.NO_TRADE
                        reasons.append("EARNINGS_LT_48H")
                        valid_until = earnings_dt + timedelta(hours=6)
                    else:
                        state = NewsState.TRADE_OK

            except Exception:
                state = NewsState.WAIT
                reasons.append("DATA_ERROR")

            out.append(
                NewsSignal(
                    ticker=t,
                    state=state,
                    reason_codes=reasons,
                    valid_until=valid_until,
                    timestamp=now,
                )
            )

        return out
