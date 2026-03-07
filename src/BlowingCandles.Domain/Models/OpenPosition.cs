namespace BlowingCandles.Domain.Models;

public sealed record OpenPosition(
    string Ticker,
    DateTimeOffset BuyTimestamp);
