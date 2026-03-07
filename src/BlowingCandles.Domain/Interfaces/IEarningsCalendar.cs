namespace BlowingCandles.Domain.Interfaces;

public interface IEarningsCalendar
{
    IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> Load();

    DateTimeOffset? GetNextFutureEarningsDate(string ticker, DateTimeOffset referenceTimeUtc);
}
