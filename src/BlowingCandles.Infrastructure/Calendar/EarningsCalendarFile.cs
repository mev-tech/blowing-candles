using System.Text.Json;
using BlowingCandles.Domain.Interfaces;

namespace BlowingCandles.Infrastructure.Calendar;

public sealed class EarningsCalendarFile : IEarningsCalendar
{
    private readonly string _path;

    public EarningsCalendarFile(string path)
    {
        _path = path;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> Load()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, IReadOnlyList<DateTimeOffset>>(StringComparer.OrdinalIgnoreCase);
        }

        using var stream = File.OpenRead(_path);
        using var document = JsonDocument.Parse(stream);
        var results = new Dictionary<string, IReadOnlyList<DateTimeOffset>>(StringComparer.OrdinalIgnoreCase);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return results;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            var dates = ParseDates(property.Value);
            if (dates.Count > 0)
            {
                results[property.Name.Trim().ToUpperInvariant()] = dates;
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
        if (DateTimeOffset.TryParse(value, out date))
        {
            date = date.ToUniversalTime();
            return true;
        }

        date = default;
        return false;
    }
}
