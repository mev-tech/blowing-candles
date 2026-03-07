namespace BlowingCandles.Domain.Interfaces;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
