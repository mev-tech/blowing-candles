namespace BlowingCandles.Domain.Interfaces;

public interface IEarningsCalendar
{
    IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> Load();
}
