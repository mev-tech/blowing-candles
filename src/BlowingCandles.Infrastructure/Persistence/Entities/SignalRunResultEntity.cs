using BlowingCandles.Domain.Enums;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Infrastructure.Persistence.Entities;

public sealed class SignalRunResultEntity
{
    public long Id { get; set; }

    public long RunId { get; set; }

    public string Ticker { get; set; } = null!;

    public TradingAction Action { get; set; }

    public NewsState NewsState { get; set; }

    public TradingAction MarketAction { get; set; }

    public string Reason { get; set; } = null!;

    public DateTimeOffset Timestamp { get; set; }

    public SignalRunEntity Run { get; set; } = null!;
}
