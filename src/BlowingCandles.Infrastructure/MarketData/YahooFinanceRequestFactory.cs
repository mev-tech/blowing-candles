namespace BlowingCandles.Infrastructure.MarketData;

internal sealed class YahooFinanceRequestFactory
{
    private static readonly Uri HistoricalPricesBaseUri = new("https://query1.finance.yahoo.com/v8/finance/chart/");
    private static readonly Uri EarningsBaseUri = new("https://query1.finance.yahoo.com/v10/finance/quoteSummary/");

    public YahooFinanceRequest CreateHistoricalPricesRequest(
        string ticker,
        DateOnly asOfDate,
        DateTimeOffset nowUtc,
        int lookbackDays)
    {
        var todayUtc = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        var effectiveAsOfDate = asOfDate > todayUtc
            ? todayUtc
            : asOfDate;

        var startUtc = CreateUtcStartOfDay(effectiveAsOfDate.AddDays(-lookbackDays));
        var nextDayStartUtc = CreateUtcStartOfDay(effectiveAsOfDate.AddDays(1));

        // Yahoo chart `period2` is treated as an exclusive boundary. For live runs,
        // cap it at "now" so we never request a future range just to include today.
        var endExclusiveUtc = effectiveAsOfDate == todayUtc && nowUtc < nextDayStartUtc
            ? nowUtc
            : nextDayStartUtc;

        if (endExclusiveUtc < startUtc)
        {
            endExclusiveUtc = startUtc;
        }

        return new YahooFinanceRequest(
            OperationName: "historical prices",
            Uri: BuildHistoricalPricesUri(ticker, startUtc, endExclusiveUtc),
            StartUtc: startUtc,
            EndExclusiveUtc: endExclusiveUtc);
    }

    public YahooFinanceRequest CreateNextEarningsRequest(string ticker)
    {
        return new YahooFinanceRequest(
            OperationName: "earnings calendar",
            Uri: new Uri($"{EarningsBaseUri}{Uri.EscapeDataString(ticker)}?modules=calendarEvents&formatted=false"));
    }

    private static Uri BuildHistoricalPricesUri(string ticker, DateTimeOffset startUtc, DateTimeOffset endExclusiveUtc)
    {
        return new Uri(
            $"{HistoricalPricesBaseUri}{Uri.EscapeDataString(ticker)}" +
            $"?interval=1d&includePrePost=false&events=div%2Csplits" +
            $"&period1={startUtc.ToUnixTimeSeconds()}" +
            $"&period2={endExclusiveUtc.ToUnixTimeSeconds()}");
    }

    private static DateTimeOffset CreateUtcStartOfDay(DateOnly date)
    {
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }
}

internal sealed record YahooFinanceRequest(
    string OperationName,
    Uri Uri,
    DateTimeOffset? StartUtc = null,
    DateTimeOffset? EndExclusiveUtc = null);
