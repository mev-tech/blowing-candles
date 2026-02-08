from enum import Enum
from typing import List, Optional
from datetime import datetime
from pydantic import BaseModel, Field

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
    reason_codes: List[str] = Field(default_factory=list)
    confidence: float = 1.0
    valid_until: Optional[datetime] = None
    timestamp: datetime

class MarketSignal(BaseModel):
    ticker: str
    action: Action
    score: int = 0
    confidence: Confidence = Confidence.NA
    tags: List[str] = Field(default_factory=list)
    reason_codes: List[str] = Field(default_factory=list)
    timestamp: datetime

class FinalSignal(BaseModel):
    ticker: str
    action: Action
    news_state: NewsState
    market_action: Action
    score: int = 0
    confidence: Confidence = Confidence.NA
    tags: List[str] = Field(default_factory=list)
    reason_codes: List[str] = Field(default_factory=list)
    timestamp: datetime
