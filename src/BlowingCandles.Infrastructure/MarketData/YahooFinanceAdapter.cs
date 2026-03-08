using BlowingCandles.Domain.Interfaces;

namespace BlowingCandles.Infrastructure.MarketData;

public sealed class YahooFinanceAdapter : IMarketDataProvider
{
    private const int LookbackDays = 365;

    private readonly YahooFinanceRequestFactory _requestFactory;
    private readonly IYahooFinanceTransport _transport;
    private readonly YahooFinanceResponseParser _responseParser;
    private readonly TimeProvider _timeProvider;

    public YahooFinanceAdapter()
        : this(
            new YahooFinanceRequestFactory(),
            new YahooFinanceHttpTransport(),
            new YahooFinanceResponseParser(),
            TimeProvider.System)
    {
    }

    internal YahooFinanceAdapter(
        YahooFinanceRequestFactory requestFactory,
        IYahooFinanceTransport transport,
        YahooFinanceResponseParser responseParser,
        TimeProvider? timeProvider = null)
    {
        _requestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _responseParser = responseParser ?? throw new ArgumentNullException(nameof(responseParser));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate)
    {
        var normalizedTicker = NormalizeTicker(ticker);
        var request = _requestFactory.CreateHistoricalPricesRequest(
            normalizedTicker,
            asOfDate,
            _timeProvider.GetUtcNow(),
            LookbackDays);
        var payload = _transport.GetString(request);
        var records = _responseParser.ParseHistoricalPriceResponse(payload);

        if (records.Count == 0)
        {
            return Array.Empty<PriceBar>();
        }

        return records
            .OrderBy(record => record.Timestamp)
            .Select(record => new PriceBar(
                record.Timestamp,
                record.Open,
                record.High,
                record.Low,
                record.Close,
                record.Volume))
            .ToArray();
    }

    public DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc)
    {
        var normalizedTicker = NormalizeTicker(ticker);
        var request = _requestFactory.CreateNextEarningsRequest(normalizedTicker);
        var payload = _transport.GetString(request);

        return _responseParser.ParseNextEarningsDate(payload, asOfUtc);
    }

    private static string NormalizeTicker(string ticker)
    {
        ArgumentNullException.ThrowIfNull(ticker);

        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        if (normalizedTicker.Length == 0)
        {
            throw new ArgumentException("Ticker must not be empty.", nameof(ticker));
        }

        return normalizedTicker;
    }
}
