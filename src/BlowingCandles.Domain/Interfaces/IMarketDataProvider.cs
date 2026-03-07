namespace BlowingCandles.Domain.Interfaces;

public readonly record struct PriceBar(
    DateTimeOffset Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

public interface IMarketDataProvider
{
    IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate);

    DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc);
}
