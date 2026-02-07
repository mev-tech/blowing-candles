from __future__ import annotations
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
import json
from typing import Any, Dict, Optional

def _utc_now() -> datetime:
    return datetime.now(timezone.utc)

def _parse_dt(s: Optional[str]) -> Optional[datetime]:
    if not s:
        return None
    return datetime.fromisoformat(s.replace("Z", "+00:00")).astimezone(timezone.utc)

def _dt_iso(dt: Optional[datetime]) -> Optional[str]:
    if not dt:
        return None
    return dt.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")

@dataclass
class StateStore:
    path: Path

    def load(self) -> Dict[str, Any]:
        if not self.path.exists():
            return {
                "day": _utc_now().date().isoformat(),
                "buys_today": 0,
                "last_buy_at": None,
            }
        try:
            return json.loads(self.path.read_text())
        except Exception:
            # fail-safe: treat as empty
            return {
                "day": _utc_now().date().isoformat(),
                "buys_today": 0,
                "last_buy_at": None,
            }

    def save(self, state: Dict[str, Any]) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text(json.dumps(state, indent=2) + "\n")

    def reset_if_new_day(self, state: Dict[str, Any]) -> Dict[str, Any]:
        today = _utc_now().date().isoformat()
        if state.get("day") != today:
            state["day"] = today
            state["buys_today"] = 0
            state["last_buy_at"] = None
        return state

    def get_buys_today(self, state: Dict[str, Any]) -> int:
        return int(state.get("buys_today", 0) or 0)

    def get_last_buy_at(self, state: Dict[str, Any]) -> Optional[datetime]:
        return _parse_dt(state.get("last_buy_at"))

    def record_buy(self, state: Dict[str, Any], when: Optional[datetime] = None) -> Dict[str, Any]:
        when = when or _utc_now()
        state["buys_today"] = self.get_buys_today(state) + 1
        state["last_buy_at"] = _dt_iso(when)
        return state
