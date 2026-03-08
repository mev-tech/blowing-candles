using System.Globalization;
using System.Text.Json;

namespace BlowingCandles.Infrastructure.MarketData;

internal sealed class YahooFinanceResponseParser
{
    public IReadOnlyList<YahooHistoricalPriceRecord> ParseHistoricalPriceResponse(string payload)
    {
        using var document = ParseJson(payload, "historical prices");
        var chartElement = RequireProperty(document.RootElement, "chart", "historical prices");

        ThrowIfYahooError(chartElement, "historical prices");

        if (!chartElement.TryGetProperty("result", out var resultElement) ||
            resultElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Array.Empty<YahooHistoricalPriceRecord>();
        }

        if (resultElement.ValueKind != JsonValueKind.Array)
        {
            throw new YahooFinanceParsingException("Yahoo historical prices response did not contain a result array.");
        }

        if (resultElement.GetArrayLength() == 0)
        {
            return Array.Empty<YahooHistoricalPriceRecord>();
        }

        var seriesElement = resultElement[0];
        if (!seriesElement.TryGetProperty("timestamp", out var timestampsElement) ||
            timestampsElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Array.Empty<YahooHistoricalPriceRecord>();
        }

        if (timestampsElement.ValueKind != JsonValueKind.Array)
        {
            throw new YahooFinanceParsingException("Yahoo historical prices response did not contain a timestamp array.");
        }

        if (timestampsElement.GetArrayLength() == 0)
        {
            return Array.Empty<YahooHistoricalPriceRecord>();
        }

        var indicatorsElement = RequireProperty(seriesElement, "indicators", "historical prices");
        var quoteArrayElement = RequireProperty(indicatorsElement, "quote", "historical prices");

        if (quoteArrayElement.ValueKind != JsonValueKind.Array || quoteArrayElement.GetArrayLength() == 0)
        {
            throw new YahooFinanceParsingException("Yahoo historical prices response did not contain quote data.");
        }

        var quoteElement = quoteArrayElement[0];
        var openElement = RequireArrayProperty(quoteElement, "open", "historical prices");
        var highElement = RequireArrayProperty(quoteElement, "high", "historical prices");
        var lowElement = RequireArrayProperty(quoteElement, "low", "historical prices");
        var closeElement = RequireArrayProperty(quoteElement, "close", "historical prices");
        var volumeElement = RequireArrayProperty(quoteElement, "volume", "historical prices");

        var count = timestampsElement.GetArrayLength();
        ValidateParallelArrayLength(openElement, count, "open", "historical prices");
        ValidateParallelArrayLength(highElement, count, "high", "historical prices");
        ValidateParallelArrayLength(lowElement, count, "low", "historical prices");
        ValidateParallelArrayLength(closeElement, count, "close", "historical prices");
        ValidateParallelArrayLength(volumeElement, count, "volume", "historical prices");

        var records = new List<YahooHistoricalPriceRecord>(count);

        for (var index = 0; index < count; index++)
        {
            if (timestampsElement[index].ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
                openElement[index].ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
                highElement[index].ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
                lowElement[index].ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
                closeElement[index].ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            var timestamp = DateTimeOffset.FromUnixTimeSeconds(
                ReadInt64(timestampsElement[index], $"timestamp[{index}]", "historical prices"));

            var volume = volumeElement[index].ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? 0L
                : ReadInt64(volumeElement[index], $"volume[{index}]", "historical prices");

            records.Add(new YahooHistoricalPriceRecord(
                Timestamp: timestamp,
                Open: ReadDecimal(openElement[index], $"open[{index}]", "historical prices"),
                High: ReadDecimal(highElement[index], $"high[{index}]", "historical prices"),
                Low: ReadDecimal(lowElement[index], $"low[{index}]", "historical prices"),
                Close: ReadDecimal(closeElement[index], $"close[{index}]", "historical prices"),
                Volume: volume));
        }

        records.Sort((left, right) => left.Timestamp.CompareTo(right.Timestamp));
        return records;
    }

    public DateTimeOffset? ParseNextEarningsDate(string payload, DateTimeOffset asOfUtc)
    {
        using var document = ParseJson(payload, "earnings calendar");
        var quoteSummaryElement = RequireProperty(document.RootElement, "quoteSummary", "earnings calendar");

        ThrowIfYahooError(quoteSummaryElement, "earnings calendar");

        if (!quoteSummaryElement.TryGetProperty("result", out var resultElement) ||
            resultElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (resultElement.ValueKind != JsonValueKind.Array)
        {
            throw new YahooFinanceParsingException("Yahoo earnings calendar response did not contain a result array.");
        }

        if (resultElement.GetArrayLength() == 0)
        {
            return null;
        }

        var summaryElement = resultElement[0];
        if (!summaryElement.TryGetProperty("calendarEvents", out var calendarEventsElement) ||
            calendarEventsElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (!calendarEventsElement.TryGetProperty("earnings", out var earningsElement) ||
            earningsElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (!earningsElement.TryGetProperty("earningsDate", out var earningsDateElement) ||
            earningsDateElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var upcomingDates = new List<DateTimeOffset>();

        foreach (var candidateElement in EnumerateSingleOrArray(earningsDateElement))
        {
            var candidateDate = ParseEarningsDate(candidateElement);
            if (candidateDate > asOfUtc)
            {
                upcomingDates.Add(candidateDate);
            }
        }

        return upcomingDates.Count == 0
            ? null
            : upcomingDates.Min();
    }

    private static JsonDocument ParseJson(string payload, string operationName)
    {
        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (JsonException exception)
        {
            throw new YahooFinanceParsingException(
                $"Yahoo {operationName} response could not be parsed as JSON.",
                exception);
        }
    }

    private static JsonElement RequireProperty(JsonElement element, string propertyName, string operationName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var propertyElement))
        {
            return propertyElement;
        }

        throw new YahooFinanceParsingException(
            $"Yahoo {operationName} response did not contain `{propertyName}`.");
    }

    private static JsonElement RequireArrayProperty(JsonElement element, string propertyName, string operationName)
    {
        var propertyElement = RequireProperty(element, propertyName, operationName);
        if (propertyElement.ValueKind != JsonValueKind.Array)
        {
            throw new YahooFinanceParsingException(
                $"Yahoo {operationName} response property `{propertyName}` was not an array.");
        }

        return propertyElement;
    }

    private static void ThrowIfYahooError(JsonElement containerElement, string operationName)
    {
        if (!containerElement.TryGetProperty("error", out var errorElement) ||
            errorElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        string? description = null;
        if (errorElement.ValueKind == JsonValueKind.Object)
        {
            if (errorElement.TryGetProperty("description", out var descriptionElement) &&
                descriptionElement.ValueKind == JsonValueKind.String)
            {
                description = descriptionElement.GetString();
            }
            else if (errorElement.TryGetProperty("code", out var codeElement) &&
                     codeElement.ValueKind == JsonValueKind.String)
            {
                description = codeElement.GetString();
            }
        }

        throw new YahooFinanceTransportException(
            $"Yahoo {operationName} response reported an error{(string.IsNullOrWhiteSpace(description) ? "." : $": {description}.")}");
    }

    private static void ValidateParallelArrayLength(
        JsonElement arrayElement,
        int expectedLength,
        string propertyName,
        string operationName)
    {
        if (arrayElement.GetArrayLength() != expectedLength)
        {
            throw new YahooFinanceParsingException(
                $"Yahoo {operationName} response property `{propertyName}` did not match the timestamp length.");
        }
    }

    private static long ReadInt64(JsonElement element, string path, string operationName)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value))
        {
            return value;
        }

        throw new YahooFinanceParsingException(
            $"Yahoo {operationName} response field `{path}` was not a valid integer.");
    }

    private static decimal ReadDecimal(JsonElement element, string path, string operationName)
    {
        if (element.ValueKind != JsonValueKind.Number)
        {
            throw new YahooFinanceParsingException(
                $"Yahoo {operationName} response field `{path}` was not a valid number.");
        }

        if (element.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        return (decimal)element.GetDouble();
    }

    private static IEnumerable<JsonElement> EnumerateSingleOrArray(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        yield return element;
    }

    private static DateTimeOffset ParseEarningsDate(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return DateTimeOffset.FromUnixTimeSeconds(
                ReadInt64(element, "earningsDate", "earnings calendar"));
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return ParseYahooDateString(element.GetString(), "earningsDate");
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new YahooFinanceParsingException(
                "Yahoo earnings calendar response contained an unsupported earnings date value.");
        }

        if (element.TryGetProperty("raw", out var rawElement) &&
            rawElement.ValueKind == JsonValueKind.Number)
        {
            return DateTimeOffset.FromUnixTimeSeconds(
                ReadInt64(rawElement, "earningsDate.raw", "earnings calendar"));
        }

        if (element.TryGetProperty("fmt", out var formattedElement) &&
            formattedElement.ValueKind == JsonValueKind.String)
        {
            return ParseYahooDateString(formattedElement.GetString(), "earningsDate.fmt");
        }

        if (element.TryGetProperty("date", out var dateElement) &&
            dateElement.ValueKind == JsonValueKind.String)
        {
            return ParseYahooDateString(dateElement.GetString(), "earningsDate.date");
        }

        throw new YahooFinanceParsingException(
            "Yahoo earnings calendar response did not contain a usable earnings date.");
    }

    private static DateTimeOffset ParseYahooDateString(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new YahooFinanceParsingException(
                $"Yahoo earnings calendar response field `{path}` was empty.");
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedDateTimeOffset))
        {
            return parsedDateTimeOffset;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            return new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        }

        throw new YahooFinanceParsingException(
            $"Yahoo earnings calendar response field `{path}` did not contain a valid date.");
    }
}

internal readonly record struct YahooHistoricalPriceRecord(
    DateTimeOffset Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);
