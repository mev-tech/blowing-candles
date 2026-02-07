from enum import Enum
from typing import List, Optional
from datetime import datetime
from pydantic import BaseModel

class NewsState(str, Enum):
    TRADE_OK = "TRADE_OK"
    WAIT = "WAIT"
    NO_TRADE = "NO_TRADE"

class Action(str, Enum):
    BUY = "BUY"
    SELL = "SELL"
    WAIT = "WAIT"
    IGNORE = "IGNORE"

class Confidence(str, Enum):
    HIGH = "HIGH"
    LOW = "LOW"
    NA = "NA"

class NewsSignal(BaseModel):
    ticker: str
    state: NewsState
    reason_codes: List[str] = []
    confidence: float = 1.0
    valid_until: Optional[datetime] = None
    timestamp: datetime

class MarketSignal(BaseModel):
    ticker: str
    action: Action
    score: int = 0
    confidence: Confidence = Confidence.NA
    tags: List[str] = []
    reason_codes: List[str] = []
    timestamp: datetime

class FinalSignal(BaseModel):
    ticker: str
    action: Action
    news_state: NewsState
    market_action: Action
    score: int = 0
    confidence: Confidence = Confidence.NA
    tags: List[str] = []
    reason_codes: List[str] = []
    timestamp: datetime
