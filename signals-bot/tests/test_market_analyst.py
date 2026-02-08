from datetime import datetime, timezone
import pandas as pd
import pytest

from signals_bot.agents.market_analyst import MarketAnalyst
from signals_bot.core.models import MarketSignal, Action


class DummyYF:
    @staticmethod
    def download(tickers, start, end, interval, auto_adjust, progress, group_by):
        # support single ticker or list; return DataFrame with Close column(s)
        periods = 220
        idx = pd.date_range(end=datetime.now(timezone.utc).date(), periods=periods)
        data = {"Close": pd.Series([100 + i * 0.1 for i in range(periods)], index=idx)}
        df = pd.DataFrame(data)
        # if multiple tickers requested, create MultiIndex columns
        if isinstance(tickers, (list, tuple)) and len(tickers) > 1:
            cols = pd.MultiIndex.from_product([["Close"], tickers])
            vals = {cols[i]: df["Close"].values for i in range(len(tickers))}
            return pd.DataFrame(vals, index=idx)
        return df


def test_market_analyst_not_enough_history(monkeypatch):
    # return a short series to trigger NOT_ENOUGH_HISTORY
    def short_download(*args, **kwargs):
        import pandas as pd
        idx = pd.date_range(end=datetime.now(timezone.utc).date(), periods=100)
        return pd.DataFrame({"Close": pd.Series([1.0] * 100, index=idx)})

    monkeypatch.setattr("yfinance.download", short_download)
    ma = MarketAnalyst(lookback_days=365)
    out = ma.analyze(["FAKE"])
    assert isinstance(out, list)
    assert out[0].tags and "NOT_ENOUGH_HISTORY" in out[0].tags


def test_market_analyst_with_enough_history(monkeypatch):
    monkeypatch.setattr("yfinance.download", DummyYF.download)
    ma = MarketAnalyst(lookback_days=365)
    out = ma.analyze(["AAPL"])
    assert isinstance(out, list)
    sig = out[0]
    assert isinstance(sig, MarketSignal)
    assert sig.ticker == "AAPL"
