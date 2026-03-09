namespace BlowingCandles.Infrastructure.Persistence;

internal static class SignalRunPersistenceLimits
{
    public const int TickerMaxLength = 16;
    public const int TriggerMaxLength = 32;
    public const int ReasonMaxLength = 256;
    public const int ErrorMessageMaxLength = 1024;
    public const int ModeMaxLength = 16;
    public const int DayMaxLength = 10;
}
