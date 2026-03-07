using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Domain.Models;

public sealed record AuditRecord(
    string Ticker,
    TradingAction Action,
    DateTimeOffset Timestamp);
