using BlowingCandles.Domain.Enums;
using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;

namespace BlowingCandles.Domain.Services;

public sealed class EarningsGate
{
    private const string CalendarExpiredReason = "CALENDAR_EXPIRED";
    private const string DataUnavailableReason = "DATA_UNAVAILABLE";
    private const string DataErrorReason = "DATA_ERROR";
    private const string EarningsFromYahooReason = "EARNINGS_FROM_YF";
    private const string EarningsWithinBlockWindowReason = "EARNINGS_LT_48H";

    private readonly IEarningsCalendar _earningsCalendar;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly TimeSpan _blockWindow;
    private readonly Action<string>? _diagnosticWriter;

    public EarningsGate(
        IEarningsCalendar earningsCalendar,
        IMarketDataProvider marketDataProvider,
        int blockWindowHours = 48,
        Action<string>? diagnosticWriter = null)
    {
        _earningsCalendar = earningsCalendar;
        _marketDataProvider = marketDataProvider;
        _blockWindow = TimeSpan.FromHours(blockWindowHours);
        _diagnosticWriter = diagnosticWriter;
    }

    public IReadOnlyList<NewsSignal> Check(IEnumerable<string> watchlist, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        var signals = new List<NewsSignal>();
        var tickers = watchlist.Select(NormalizeTicker).Where(ticker => ticker.Length > 0).ToArray();
        IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> calendar;

        try
        {
            calendar = _earningsCalendar.Load();
        }
        catch (Exception exception)
        {
            _diagnosticWriter?.Invoke(
                $"[EarningsGate] earnings calendar load failed: {exception.GetType().Name}: {exception.Message}");
            return tickers
                .Select(ticker => CreateSignal(ticker, NewsState.WAIT, DataErrorReason, now))
                .ToArray();
        }

        foreach (var ticker in tickers)
        {
            try
            {
                var tickerExistsInCalendar = calendar.ContainsKey(ticker);
                DateTimeOffset? earningsDate = tickerExistsInCalendar
                    ? _earningsCalendar.GetNextFutureEarningsDate(ticker, now)
                    : null;

                if (tickerExistsInCalendar && earningsDate is null)
                {
                    signals.Add(CreateSignal(ticker, NewsState.WAIT, CalendarExpiredReason, now));
                    continue;
                }

                var reasons = new List<string>();

                if (!tickerExistsInCalendar && earningsDate is null)
                {
                    earningsDate = _marketDataProvider.GetNextEarningsDate(ticker, now);
                    if (earningsDate is not null)
                    {
                        reasons.Add(EarningsFromYahooReason);
                    }
                }

                if (earningsDate is null)
                {
                    signals.Add(CreateSignal(ticker, NewsState.WAIT, DataUnavailableReason, now));
                    continue;
                }

                var delta = earningsDate.Value.ToUniversalTime() - now;
                if (delta >= TimeSpan.Zero && delta <= _blockWindow)
                {
                    reasons.Add(EarningsWithinBlockWindowReason);
                    signals.Add(CreateSignal(ticker, NewsState.NO_TRADE, string.Join(",", reasons), now));
                    continue;
                }

                signals.Add(CreateSignal(ticker, NewsState.TRADE_OK, string.Join(",", reasons), now));
            }
            catch (Exception exception)
            {
                _diagnosticWriter?.Invoke(
                    $"[EarningsGate] {ticker} earnings lookup failed: {exception.GetType().Name}: {exception.Message}");
                signals.Add(CreateSignal(ticker, NewsState.WAIT, DataErrorReason, now));
            }
        }

        return signals;
    }

    private static NewsSignal CreateSignal(string ticker, NewsState state, string reason, DateTimeOffset timestamp)
    {
        return new NewsSignal(ticker, state, reason, timestamp);
    }

    private static string NormalizeTicker(string ticker)
    {
        return ticker.Trim().ToUpperInvariant();
    }
}
