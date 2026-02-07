from datetime import datetime, timedelta, timezone
from typing import List, Optional, Dict
from pathlib import Path
import json

import yfinance as yf
import pandas as pd

from signals_bot.core.models import NewsSignal, NewsState

def _parse_iso_to_utc(s: str) -> Optional[datetime]:
    try:
        dt = datetime.fromisoformat(s.replace("Z", "+00:00"))
        return dt.astimezone(timezone.utc)
    except Exception:
        return None

class NewsSentinel:
    def __init__(
        self,
        earnings_block_hours: int = 48,
        local_calendar_path: Optional[str] = None,
    ):
        self.earnings_block = timedelta(hours=earnings_block_hours)
        self.local_calendar_path = local_calendar_path
        self._local_calendar = self._load_local_calendar(local_calendar_path)

    def _load_local_calendar(self, path: Optional[str]) -> Dict[str, List[datetime]]:
        if not path:
            return {}
        p = Path(path)
        if not p.exists():
            return {}

        raw = json.loads(p.read_text())
        out: Dict[str, List[datetime]] = {}

        for ticker, val in raw.items():
            t = str(ticker).upper()
            dts: List[datetime] = []

            if isinstance(val, str):
                dt = _parse_iso_to_utc(val)
                if dt:
                    dts.append(dt)
            elif isinstance(val, list):
                for item in val:
                    if isinstance(item, str):
                        dt = _parse_iso_to_utc(item)
                        if dt:
                            dts.append(dt)

            dts = sorted(set(dts))
            if dts:
                out[t] = dts

        return out

    def _calendar_has_ticker(self, t: str) -> bool:
        return t.upper() in self._local_calendar

    def _next_from_local(self, t: str, now: datetime) -> Optional[datetime]:
        arr = self._local_calendar.get(t.upper())
        if not arr:
            return None
        for dt in arr:
            if dt > now:
                return dt
        return None

    def _extract_next_earnings_yf(self, t: str) -> Optional[datetime]:
        tk = yf.Ticker(t)
        try:
            df = tk.get_earnings_dates(limit=8)
            if isinstance(df, pd.DataFrame) and not df.empty:
                idx = df.index
                if len(idx) > 0 and isinstance(idx[0], pd.Timestamp):
                    return idx[0].to_pydatetime()
        except Exception:
            pass

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

    def check(self, tickers: List[str]) -> List[NewsSignal]:
        now = datetime.now(timezone.utc)
        out: List[NewsSignal] = []

        for t in tickers:
            state = NewsState.TRADE_OK
            reasons: List[str] = []
            valid_until = None

            try:
                in_calendar = self._calendar_has_ticker(t)
                earnings_dt = self._next_from_local(t, now)

                # If ticker exists but no future earnings -> explicit expired (deterministic)
                if in_calendar and earnings_dt is None:
                    state = NewsState.WAIT
                    reasons.append("CALENDAR_EXPIRED")
                    out.append(NewsSignal(
                        ticker=t,
                        state=state,
                        reason_codes=reasons,
                        valid_until=None,
                        timestamp=now,
                    ))
                    continue

                # If not in calendar, try yfinance fallback
                if not in_calendar and earnings_dt is None:
                    dt_yf = self._extract_next_earnings_yf(t)
                    if dt_yf:
                        if dt_yf.tzinfo is None:
                            dt_yf = dt_yf.replace(tzinfo=timezone.utc)
                        earnings_dt = dt_yf.astimezone(timezone.utc)
                        reasons.append("EARNINGS_FROM_YF")

                if earnings_dt is None:
                    state = NewsState.WAIT
                    reasons.append("DATA_UNAVAILABLE")
                else:
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

            out.append(NewsSignal(
                ticker=t,
                state=state,
                reason_codes=reasons,
                valid_until=valid_until,
                timestamp=now,
            ))

        return out
