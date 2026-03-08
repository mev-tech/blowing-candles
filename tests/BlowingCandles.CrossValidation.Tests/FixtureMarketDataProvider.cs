using System.Globalization;
using System.Text.Json;
using BlowingCandles.Domain.Interfaces;

namespace BlowingCandles.CrossValidation.Tests;

public sealed class FixtureMarketDataProvider : IMarketDataProvider
{
    private readonly string _fixtureRoot;
    private readonly IReadOnlyDictionary<string, string> _priceFilesByTicker;
    private readonly IReadOnlyDictionary<string, DateTimeOffset> _earningsDatesByTicker;
    private readonly Dictionary<string, IReadOnlyList<PriceBar>> _priceHistoryCache = new(StringComparer.Ordinal);

    public FixtureMarketDataProvider(string fixtureRoot)
    {
        if (string.IsNullOrWhiteSpace(fixtureRoot))
        {
            throw new ArgumentException("Fixture root is required.", nameof(fixtureRoot));
        }

        _fixtureRoot = fixtureRoot;
        _priceFilesByTicker = LoadStringMap(Path.Combine(fixtureRoot, "manifest.json"));
        _earningsDatesByTicker = LoadDateMap(Path.Combine(fixtureRoot, "yf-earnings.json"));
    }

    public IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate)
    {
        var normalizedTicker = NormalizeTicker(ticker);
        if (!_priceFilesByTicker.TryGetValue(normalizedTicker, out var relativePath))
        {
            throw new InvalidOperationException($"No fixture market data is configured for ticker '{normalizedTicker}'.");
        }

        if (!_priceHistoryCache.TryGetValue(relativePath, out var cachedBars))
        {
            var fullPath = Path.Combine(_fixtureRoot, relativePath);
            cachedBars = File.ReadLines(fullPath)
                .Skip(1)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(ParsePriceBar)
                .OrderBy(priceBar => priceBar.Timestamp)
                .ToArray();

            _priceHistoryCache[relativePath] = cachedBars;
        }

        var cutoff = asOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        return cachedBars
            .Where(priceBar => priceBar.Timestamp.UtcDateTime <= cutoff)
            .ToArray();
    }

    public DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc)
    {
        _ = asOfUtc;

        return _earningsDatesByTicker.GetValueOrDefault(NormalizeTicker(ticker));
    }

    private static PriceBar ParsePriceBar(string line)
    {
        var columns = line.Split(',', StringSplitOptions.TrimEntries);
        if (columns.Length != 6)
        {
            throw new InvalidDataException($"Fixture CSV row must contain 6 columns: '{line}'.");
        }

        return new PriceBar(
            DateTimeOffset.Parse(columns[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            decimal.Parse(columns[1], CultureInfo.InvariantCulture),
            decimal.Parse(columns[2], CultureInfo.InvariantCulture),
            decimal.Parse(columns[3], CultureInfo.InvariantCulture),
            decimal.Parse(columns[4], CultureInfo.InvariantCulture),
            long.Parse(columns[5], CultureInfo.InvariantCulture));
    }

    private static IReadOnlyDictionary<string, string> LoadStringMap(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            map[NormalizeTicker(property.Name)] = property.Value.GetString()
                ?? throw new InvalidDataException($"Fixture manifest entry '{property.Name}' must be a string.");
        }

        return map;
    }

    private static IReadOnlyDictionary<string, DateTimeOffset> LoadDateMap(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            var value = property.Value.GetString()
                ?? throw new InvalidDataException($"Fixture earnings entry '{property.Name}' must be a string.");
            map[NormalizeTicker(property.Name)] = DateTimeOffset.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        return map;
    }

    private static string NormalizeTicker(string ticker)
    {
        return ticker.Trim().ToUpperInvariant();
    }
}
