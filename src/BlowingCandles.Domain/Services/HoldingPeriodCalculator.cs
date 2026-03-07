using BlowingCandles.Domain.Models;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Domain.Services;

public sealed class HoldingPeriodCalculator
{
    public HoldingPeriodAnalysis Calculate(IEnumerable<AuditRecord> records)
    {
        var pendingBuysByTicker = new Dictionary<string, Queue<(long Sequence, AuditRecord Record)>>(StringComparer.Ordinal);
        var completed = new List<(long Sequence, HoldingPeriod Period)>();
        long sequence = 0;

        foreach (var record in records)
        {
            switch (record.Action)
            {
                case TradingAction.BUY:
                    GetPendingBuys(record.Ticker).Enqueue((sequence++, record));
                    break;
                case TradingAction.SELL:
                    var pendingBuys = GetPendingBuys(record.Ticker);
                    if (pendingBuys.Count == 0)
                    {
                        continue;
                    }

                    var (buySequence, buyRecord) = pendingBuys.Dequeue();
                    completed.Add((buySequence, new HoldingPeriod(
                        record.Ticker,
                        buyRecord.Timestamp,
                        record.Timestamp,
                        record.Timestamp - buyRecord.Timestamp)));
                    break;
            }
        }

        var openPositions = pendingBuysByTicker.Values
            .SelectMany(queue => queue)
            .OrderBy(entry => entry.Record.Timestamp)
            .ThenBy(entry => entry.Sequence)
            .Select(entry => new OpenPosition(entry.Record.Ticker, entry.Record.Timestamp))
            .ToArray();

        var completedPeriods = completed
            .OrderBy(entry => entry.Period.BuyTimestamp)
            .ThenBy(entry => entry.Sequence)
            .Select(entry => entry.Period)
            .ToArray();

        return new HoldingPeriodAnalysis(completedPeriods, openPositions);

        Queue<(long Sequence, AuditRecord Record)> GetPendingBuys(string ticker)
        {
            if (!pendingBuysByTicker.TryGetValue(ticker, out var queue))
            {
                queue = new Queue<(long Sequence, AuditRecord Record)>();
                pendingBuysByTicker[ticker] = queue;
            }

            return queue;
        }
    }
}
