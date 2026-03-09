namespace BlowingCandles.Infrastructure.Persistence.Entities;

public sealed class TradeGovernorStateEntity
{
    public long Id { get; set; }

    public string Mode { get; set; } = null!;

    public string Day { get; set; } = null!;

    public int BuysToday { get; set; }

    public DateTimeOffset? LastBuyAt { get; set; }
}
