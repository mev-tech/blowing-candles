using System.Globalization;
using System.Text.Json;
using BlowingCandles.Domain.Interfaces;

namespace BlowingCandles.Infrastructure.Calendar;

public sealed class EarningsCalendarFile : IEarningsCalendar
{
    private readonly string _path;
    private IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>>? _calendar;

    public EarningsCalendarFile(string path)
    {
        _path = path;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> Load()
    {
        return _calendar ??= LoadCore();
    }

    public DateTimeOffset? GetNextFutureEarningsDate(string ticker, DateTimeOffset referenceTimeUtc)
    {
        if (!Load().TryGetValue(NormalizeTicker(ticker), out var dates))
        {
            return null;
        }

        foreach (var date in dates)
        {
            if (date > referenceTimeUtc)
            {
                return date;
            }
        }

        return null;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> LoadCore()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, IReadOnlyList<DateTimeOffset>>(StringComparer.Ordinal);
        }

        try
        {
            using var stream = File.OpenRead(_path);
            using var document = JsonDocument.Parse(stream);
            return ParseCalendar(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Calendar file '{_path}' contains invalid JSON.", exception);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> ParseCalendar(JsonElement rootElement)
    {
        if (rootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Calendar JSON root must be an object.");
        }

        var results = new Dictionary<string, IReadOnlyList<DateTimeOffset>>(StringComparer.Ordinal);

        foreach (var property in rootElement.EnumerateObject())
        {
            var dates = ParseDates(property.Value);
            var ticker = NormalizeTicker(property.Name);

            if (ticker.Length > 0 && dates.Count > 0)
            {
                results[ticker] = dates;
            }
        }

        return results;
    }

    private static IReadOnlyList<DateTimeOffset> ParseDates(JsonElement element)
    {
        var dates = new HashSet<DateTimeOffset>();

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (TryParseUtc(element.GetString(), out var singleDate))
                {
                    dates.Add(singleDate);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && TryParseUtc(item.GetString(), out var arrayDate))
                    {
                        dates.Add(arrayDate);
                    }
                }

                break;
        }

        return dates
            .OrderBy(date => date)
            .ToArray();
    }

    private static bool TryParseUtc(string? value, out DateTimeOffset date)
    {
        if (DateTimeOffset.TryParse(
            value?.Replace("Z", "+00:00", StringComparison.Ordinal),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out date))
        {
            date = date.ToUniversalTime();
            return true;
        }

        date = default;
        return false;
    }

    private static string NormalizeTicker(string ticker)
    {
        return ticker.Trim().ToUpperInvariant();
    }
}
