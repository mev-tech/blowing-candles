namespace BlowingCandles.Domain.Models;

public sealed record TradeGovernorState(
    string Day,
    int BuysToday,
    DateTimeOffset? LastBuyAt);
