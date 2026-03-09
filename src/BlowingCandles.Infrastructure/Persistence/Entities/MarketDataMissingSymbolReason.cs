namespace BlowingCandles.Infrastructure.Persistence.Entities;

public enum MarketDataMissingSymbolReason
{
    NotReturnedByProvider,
    EmptySeries,
    InvalidSymbol,
    ProviderError,
    ParseError
}
