from enum import Enum
from typing import List, Optional
from datetime import datetime
from pydantic import BaseModel

class NewsState(str, Enum):
    TRADE_OK = "TRADE_OK"
    WAIT = "WAIT"
    NO_TRADE = "NO_TRADE"
    MANAGE = "MANAGE"
    EXIT_RECOMMENDED = "EXIT_RECOMMENDED"
    EXIT_NOW = "EXIT_NOW"

class Action(str, Enum):
    BUY = "BUY"
    SELL = "SELL"
    WAIT = "WAIT"
    IGNORE = "IGNORE"
    MANAGE = "MANAGE"
    EXIT_RECOMMENDED = "EXIT_RECOMMENDED"
    EXIT_NOW = "EXIT_NOW"

class NewsSignal(BaseModel):
    ticker: str
    state: NewsState
    reason_codes: List[str] = []
    confidence: float = 1.0
    valid_until: Optional[datetime] = None
    timestamp: datetime

class FinalSignal(BaseModel):
    ticker: str
    action: Action
    news_state: NewsState
    reason_codes: List[str] = []
    timestamp: datetime
