using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Domain.Models;

public sealed record MarketSignal(
    string Ticker,
    TradingAction Action,
    int Score,
    decimal Close,
    decimal Sma50,
    decimal Sma200,
    decimal Rsi14,
    string Reason,
    DateTimeOffset Timestamp);
