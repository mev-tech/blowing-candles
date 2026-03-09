namespace BlowingCandles.Infrastructure.Persistence.Services;

public sealed class TradeGovernorDbStateStoreFactory
{
    private readonly AppDbContext _dbContext;

    public TradeGovernorDbStateStoreFactory(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public TradeGovernorDbStateStore Create(string mode)
    {
        return new TradeGovernorDbStateStore(_dbContext, mode);
    }
}
