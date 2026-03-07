using System.Text.Json.Nodes;

namespace BlowingCandles.Infrastructure.Audit;

public sealed class JsonlAuditReader
{
    private readonly string _path;

    public JsonlAuditReader(string path)
    {
        _path = path;
    }

    public IReadOnlyList<JsonNode?> ReadAll()
    {
        if (!File.Exists(_path))
        {
            return Array.Empty<JsonNode?>();
        }

        return File.ReadLines(_path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonNode.Parse(line))
            .ToArray();
    }
}
