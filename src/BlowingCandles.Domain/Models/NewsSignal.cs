using BlowingCandles.Domain.Enums;

namespace BlowingCandles.Domain.Models;

public sealed record NewsSignal(
    string Ticker,
    NewsState State,
    string Reason,
    DateTimeOffset Timestamp);
