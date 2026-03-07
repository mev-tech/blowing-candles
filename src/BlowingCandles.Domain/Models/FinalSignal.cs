using BlowingCandles.Domain.Enums;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Domain.Models;

public sealed record FinalSignal(
    string Ticker,
    TradingAction Action,
    NewsState NewsState,
    TradingAction MarketAction,
    string Reason,
    DateTimeOffset Timestamp);
