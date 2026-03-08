using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Domain.Services;

public sealed class TechnicalScorer
{
    private const int SmaPeriodShort = 50;
    private const int SmaPeriodLong = 200;
    private const int RsiPeriod = 14;

    private const int ScoreSmaShort = 20;
    private const int ScoreSmaLong = 20;
    private const int ScoreGoldenCross = 20;
    private const int ScoreRsiOversold = 40;
    private const int ScoreRsiNeutral = 20;

    private const int BuyThreshold = 80;
    private const int SellThreshold = 20;

    private const decimal RsiOversold = 40m;
    private const decimal RsiNeutral = 50m;

    private const string InsufficientDataReason = "INSUFFICIENT_DATA";
    private const string MarketDataErrorReason = "MARKET_DATA_ERROR";
    private const string AboveSma50Reason = "ABOVE_SMA50";
    private const string AboveSma200Reason = "ABOVE_SMA200";
    private const string GoldenCrossReason = "GOLDEN_CROSS";
    private const string RsiOversoldReason = "RSI_OVERSOLD";
    private const string RsiNeutralReason = "RSI_NEUTRAL";

    private readonly IMarketDataProvider _marketDataProvider;

    public TechnicalScorer(IMarketDataProvider marketDataProvider)
    {
        _marketDataProvider = marketDataProvider;
    }

    public IReadOnlyList<MarketSignal> Score(IEnumerable<string> watchlist, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        var asOfDate = DateOnly.FromDateTime(now.UtcDateTime);
        var signals = new List<MarketSignal>();

        foreach (var ticker in watchlist.Select(NormalizeTicker).Where(ticker => ticker.Length > 0))
        {
            signals.Add(ScoreTicker(ticker, asOfDate, now));
        }

        return signals;
    }

    private MarketSignal ScoreTicker(string ticker, DateOnly asOfDate, DateTimeOffset timestamp)
    {
        try
        {
            var priceHistory = _marketDataProvider.GetDailyPriceHistory(ticker, asOfDate);
            if (priceHistory is null || priceHistory.Count == 0)
            {
                return CreateSignal(ticker, TradingAction.WAIT, 0, 0m, 0m, 0m, 0m, MarketDataErrorReason, timestamp);
            }

            var closes = priceHistory
                .OrderBy(priceBar => priceBar.Timestamp)
                .Select(priceBar => priceBar.Close)
                .ToArray();

            if (closes.Length < SmaPeriodLong)
            {
                return CreateSignal(ticker, TradingAction.WAIT, 0, 0m, 0m, 0m, 0m, InsufficientDataReason, timestamp);
            }

            var close = closes[^1];
            var sma50 = CalculateSimpleMovingAverage(closes, SmaPeriodShort);
            var sma200 = CalculateSimpleMovingAverage(closes, SmaPeriodLong);
            var rsi14 = CalculateRsi(closes, RsiPeriod);

            var score = 0;
            var reasonCodes = new List<string>();

            if (close > sma50)
            {
                score += ScoreSmaShort;
                reasonCodes.Add(AboveSma50Reason);
            }

            if (close > sma200)
            {
                score += ScoreSmaLong;
                reasonCodes.Add(AboveSma200Reason);
            }

            if (sma50 > sma200)
            {
                score += ScoreGoldenCross;
                reasonCodes.Add(GoldenCrossReason);
            }

            if (rsi14 <= RsiOversold)
            {
                score += ScoreRsiOversold;
                reasonCodes.Add(RsiOversoldReason);
            }
            else if (rsi14 <= RsiNeutral)
            {
                score += ScoreRsiNeutral;
                reasonCodes.Add(RsiNeutralReason);
            }

            var action = score >= BuyThreshold
                ? TradingAction.BUY
                : score <= SellThreshold
                    ? TradingAction.SELL
                    : TradingAction.WAIT;

            return CreateSignal(ticker, action, score, close, sma50, sma200, rsi14, string.Join(",", reasonCodes), timestamp);
        }
        catch
        {
            return CreateSignal(ticker, TradingAction.WAIT, 0, 0m, 0m, 0m, 0m, MarketDataErrorReason, timestamp);
        }
    }

    private static MarketSignal CreateSignal(
        string ticker,
        TradingAction action,
        int score,
        decimal close,
        decimal sma50,
        decimal sma200,
        decimal rsi14,
        string reason,
        DateTimeOffset timestamp)
    {
        return new MarketSignal(ticker, action, score, close, sma50, sma200, rsi14, reason, timestamp);
    }

    private static decimal CalculateSimpleMovingAverage(IReadOnlyList<decimal> closes, int period)
    {
        var sum = 0m;
        var startIndex = closes.Count - period;

        for (var index = startIndex; index < closes.Count; index++)
        {
            sum += closes[index];
        }

        return sum / period;
    }

    private static decimal CalculateRsi(IReadOnlyList<decimal> closes, int period)
    {
        var startIndex = closes.Count - (period + 1);
        var totalGain = 0m;
        var totalLoss = 0m;

        for (var index = startIndex + 1; index < closes.Count; index++)
        {
            var delta = closes[index] - closes[index - 1];
            if (delta > 0m)
            {
                totalGain += delta;
            }
            else if (delta < 0m)
            {
                totalLoss += -delta;
            }
        }

        if (totalGain == 0m && totalLoss == 0m)
        {
            return 50m;
        }

        if (totalLoss == 0m)
        {
            return 100m;
        }

        if (totalGain == 0m)
        {
            return 0m;
        }

        return 100m * totalGain / (totalGain + totalLoss);
    }

    private static string NormalizeTicker(string ticker)
    {
        return ticker.Trim().ToUpperInvariant();
    }
}
