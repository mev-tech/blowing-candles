using BlowingCandles.Domain.Models;

namespace BlowingCandles.Domain.Interfaces;

public interface ITradeGovernorStateStore
{
    TradeGovernorState Load(IClock clock);

    void Save(TradeGovernorState state);
}
