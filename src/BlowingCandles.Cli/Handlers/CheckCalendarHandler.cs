using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Infrastructure.Calendar;
using BlowingCandles.Infrastructure.Config;

namespace BlowingCandles.Cli.Handlers;

public sealed class CheckCalendarHandler
{
    private readonly YamlConfigLoader _configLoader;
    private readonly IClock _clock;

    public CheckCalendarHandler(YamlConfigLoader configLoader, IClock clock)
    {
        _configLoader = configLoader;
        _clock = clock;
    }

    public int Handle(string configPath)
    {
        var config = _configLoader.Load(configPath);
        var calendar = new EarningsCalendarFile(config.News.LocalEarningsCalendar).Load();
        var now = _clock.UtcNow;

        var ok = new List<(string Ticker, DateTimeOffset NextDate)>();
        var expired = new List<string>();
        var missing = new List<string>();

        foreach (var ticker in config.Watchlist.OrderBy(ticker => ticker, StringComparer.Ordinal))
        {
            if (!calendar.TryGetValue(ticker, out var dates))
            {
                missing.Add(ticker);
                continue;
            }

            var nextDate = dates
                .Where(date => date > now)
                .Select(date => (DateTimeOffset?)date)
                .FirstOrDefault();

            if (nextDate is null)
            {
                expired.Add(ticker);
                continue;
            }

            ok.Add((ticker, nextDate.Value));
        }

        Console.WriteLine($"Now (UTC): {now:O}");
        Console.WriteLine($"Calendar file: {config.News.LocalEarningsCalendar}");
        Console.WriteLine();

        if (ok.Count > 0)
        {
            Console.WriteLine("OK (next earnings found):");
            foreach (var entry in ok)
            {
                Console.WriteLine($"  {entry.Ticker}: {entry.NextDate:O}");
            }

            Console.WriteLine();
        }

        if (expired.Count > 0)
        {
            Console.WriteLine("EXPIRED (no future dates in calendar):");
            foreach (var ticker in expired)
            {
                Console.WriteLine($"  {ticker}: CALENDAR_EXPIRED");
            }

            Console.WriteLine();
        }

        if (missing.Count > 0)
        {
            Console.WriteLine("MISSING (not present in calendar file):");
            foreach (var ticker in missing)
            {
                Console.WriteLine($"  {ticker}");
            }

            Console.WriteLine();
        }

        return expired.Count == 0 && missing.Count == 0 ? 0 : 2;
    }
}
