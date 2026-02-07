from __future__ import annotations
from pathlib import Path
from datetime import datetime, timezone
from typing import Dict, List, Optional, Tuple
import json
import yaml

def _parse_iso_to_utc(s: str) -> Optional[datetime]:
    try:
        dt = datetime.fromisoformat(s.replace("Z", "+00:00"))
        return dt.astimezone(timezone.utc)
    except Exception:
        return None

def _load_calendar(path: str) -> Dict[str, List[datetime]]:
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

def _next_future(dts: List[datetime], now: datetime) -> Optional[datetime]:
    for dt in dts:
        if dt > now:
            return dt
    return None

def main():
    cfg = yaml.safe_load(Path("config.yaml").read_text())
    watchlist = [str(t).upper() for t in cfg.get("watchlist", [])]
    cal_path = cfg.get("news", {}).get("local_earnings_calendar")

    if not cal_path:
        print("ERROR: config.yaml -> news.local_earnings_calendar missing")
        raise SystemExit(1)

    cal = _load_calendar(cal_path)
    now = datetime.now(timezone.utc)

    expired: List[Tuple[str, str]] = []
    missing: List[str] = []
    ok: List[Tuple[str, str]] = []

    for t in watchlist:
        if t not in cal:
            missing.append(t)
            continue
        nxt = _next_future(cal[t], now)
        if nxt is None:
            expired.append((t, "CALENDAR_EXPIRED"))
        else:
            ok.append((t, nxt.isoformat().replace("+00:00", "Z")))

    print(f"Now (UTC): {now.isoformat().replace('+00:00','Z')}")
    print(f"Calendar file: {cal_path}")
    print("")

    if ok:
        print("OK (next earnings found):")
        for t, dt in ok:
            print(f"  {t}: {dt}")
        print("")

    if expired:
        print("EXPIRED (no future dates in calendar):")
        for t, reason in expired:
            print(f"  {t}: {reason}")
        print("")

    if missing:
        print("MISSING (not present in calendar file):")
        for t in missing:
            print(f"  {t}")
        print("")

    # Exit code: 0 if all ok, 2 if any issues
    if expired or missing:
        raise SystemExit(2)

if __name__ == "__main__":
    main()
