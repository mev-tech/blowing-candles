namespace BlowingCandles.Infrastructure.Persistence;

internal static class MarketDataPersistenceLimits
{
    public const int SymbolMaxLength = 16;
    public const int TriggerMaxLength = 32;
    public const int ProviderMaxLength = 64;
    public const int ErrorCodeMaxLength = 64;
    public const int ErrorMessageMaxLength = 1024;
    public const int DetailMaxLength = 1024;
}
