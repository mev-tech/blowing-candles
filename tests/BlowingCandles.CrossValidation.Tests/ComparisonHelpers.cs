using System.Text.Json;

namespace BlowingCandles.CrossValidation.Tests;

public static class ComparisonHelpers
{
    public static void AssertTextContentMatches(string expectedPath, string actualText)
    {
        Assert.True(File.Exists(expectedPath), $"Golden file not found: {expectedPath}");
        Assert.Equal(ReadNormalizedLines(expectedPath), ReadNormalizedLinesFromContent(actualText));
    }

    public static void AssertJsonContentMatches(string expectedPath, string actualJson)
    {
        Assert.True(File.Exists(expectedPath), $"Golden file not found: {expectedPath}");
        using var expectedDocument = JsonDocument.Parse(File.ReadAllText(expectedPath));
        using var actualDocument = JsonDocument.Parse(actualJson);

        Assert.Equal(
            Canonicalize(expectedDocument.RootElement),
            Canonicalize(actualDocument.RootElement));
    }

    public static void AssertJsonlContentMatches(string expectedPath, string actualJsonl)
    {
        Assert.True(File.Exists(expectedPath), $"Golden file not found: {expectedPath}");
        var expectedLines = ReadNormalizedLines(expectedPath);
        var actualLines = ReadNormalizedLinesFromContent(actualJsonl);

        Assert.Equal(expectedLines.Length, actualLines.Length);

        for (var index = 0; index < expectedLines.Length; index++)
        {
            using var expectedDocument = JsonDocument.Parse(expectedLines[index]);
            using var actualDocument = JsonDocument.Parse(actualLines[index]);

            Assert.Equal(
                Canonicalize(expectedDocument.RootElement),
                Canonicalize(actualDocument.RootElement));
        }
    }

    private static string[] ReadNormalizedLines(string path)
    {
        return File.Exists(path)
            ? File.ReadAllLines(path).Select(line => line.TrimEnd('\r')).ToArray()
            : Array.Empty<string>();
    }

    private static string[] ReadNormalizedLinesFromContent(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<string>();
        }

        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToArray();
    }

    private static string Canonicalize(JsonElement element)
    {
        return JsonSerializer.Serialize(Normalize(element));
    }

    private static object? Normalize(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToDictionary(
                    property => property.Name,
                    property => Normalize(property.Value),
                    StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(Normalize)
                .ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
