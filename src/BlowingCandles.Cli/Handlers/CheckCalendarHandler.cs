using System.Globalization;
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
        var now = _clock.UtcNow;
        var configuredCalendarPath = config.News.LocalEarningsCalendar;
        var resolvedCalendarPath = config.News.ResolvedLocalEarningsCalendar;

        if (string.IsNullOrWhiteSpace(configuredCalendarPath) || string.IsNullOrWhiteSpace(resolvedCalendarPath))
        {
            Console.WriteLine("ERROR: config.yaml -> news.local_earnings_calendar missing");
            return 1;
        }

        var calendar = new EarningsCalendarFile(resolvedCalendarPath);

        var ok = new List<(string Ticker, DateTimeOffset NextDate)>();
        var expired = new List<string>();
        var missing = new List<string>();

        try
        {
            var knownTickers = calendar.Load();

            foreach (var ticker in config.Watchlist)
            {
                if (!knownTickers.ContainsKey(ticker))
                {
                    missing.Add(ticker);
                    continue;
                }

                var nextDate = calendar.GetNextFutureEarningsDate(ticker, now);

                if (nextDate is null)
                {
                    expired.Add(ticker);
                    continue;
                }

                ok.Add((ticker, nextDate.Value));
            }
        }
        catch (InvalidDataException)
        {
            Console.WriteLine($"ERROR: invalid earnings calendar file: {configuredCalendarPath}");
            return 1;
        }

        Console.WriteLine($"Now (UTC): {FormatUtc(now)}");
        Console.WriteLine($"Calendar file: {configuredCalendarPath}");
        Console.WriteLine();

        if (ok.Count > 0)
        {
            Console.WriteLine("OK (next earnings found):");
            foreach (var entry in ok)
            {
                Console.WriteLine($"  {entry.Ticker}: {FormatUtc(entry.NextDate)}");
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

    private static string FormatUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var wholeSeconds = utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var microseconds = (utc.Ticks % TimeSpan.TicksPerSecond) / 10;

        return microseconds == 0
            ? $"{wholeSeconds}Z"
            : $"{wholeSeconds}.{microseconds:000000}Z";
    }
}
