namespace BlowingCandles.Domain.Models;

public sealed record HoldingPeriod(
    string Ticker,
    DateTimeOffset BuyTimestamp,
    DateTimeOffset SellTimestamp,
    TimeSpan Duration);
