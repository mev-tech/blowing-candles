using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;
using BlowingCandles.Domain.Services;

namespace BlowingCandles.Application;

public sealed class SignalPipeline
{
    private readonly EarningsGate _earningsGate;
    private readonly TechnicalScorer _technicalScorer;
    private readonly TradeGovernor _tradeGovernor;

    public SignalPipeline(EarningsGate earningsGate, TechnicalScorer technicalScorer, TradeGovernor tradeGovernor)
    {
        _earningsGate = earningsGate;
        _technicalScorer = technicalScorer;
        _tradeGovernor = tradeGovernor;
    }

    public IReadOnlyList<FinalSignal> Run(IEnumerable<string> watchlist, IClock clock)
    {
        var normalizedWatchlist = watchlist
            .Select(ticker => ticker.Trim().ToUpperInvariant())
            .Where(ticker => !string.IsNullOrWhiteSpace(ticker))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var newsSignals = _earningsGate.Check(normalizedWatchlist, clock);
        var marketSignals = _technicalScorer.Score(normalizedWatchlist, clock);
        return _tradeGovernor.Decide(newsSignals, marketSignals, clock);
    }
}
