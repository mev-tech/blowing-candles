using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlowingCandles.Domain.Models;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Infrastructure.Audit;

public sealed class JsonlAuditReader
{
    private readonly string _path;

    public JsonlAuditReader(string path)
    {
        _path = path;
    }

    public AuditReadResult ReadAll()
    {
        if (!File.Exists(_path))
        {
            return new AuditReadResult(0, Array.Empty<AuditRecord>());
        }

        var totalRecords = 0;
        var buySellRecords = new List<AuditRecord>();

        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryParseRecord(line, out var record))
            {
                totalRecords++;

                if (record.Action is TradingAction.BUY or TradingAction.SELL)
                {
                    buySellRecords.Add(record);
                }
            }
        }

        return new AuditReadResult(totalRecords, buySellRecords);
    }

    private static bool TryParseRecord(string line, out AuditRecord record)
    {
        record = default!;
        JsonNode? node;

        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return false;
        }

        if (node is not JsonObject payload)
        {
            return false;
        }

        var ticker = NormalizeTicker(payload["ticker"]?.ToString());
        if (ticker is null)
        {
            return false;
        }

        if (!TryParseAction(payload["action"]?.ToString(), out var action))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(
                payload["timestamp"]?.ToString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            return false;
        }

        record = new AuditRecord(ticker, action, timestamp);
        return true;
    }

    private static string? NormalizeTicker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToUpperInvariant();
    }

    private static bool TryParseAction(string? value, out TradingAction action)
    {
        action = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var separatorIndex = normalized.LastIndexOf('.');
        if (separatorIndex >= 0 && separatorIndex < normalized.Length - 1)
        {
            normalized = normalized[(separatorIndex + 1)..];
        }

        if (!Enum.TryParse(normalized, ignoreCase: true, out action))
        {
            return false;
        }

        return true;
    }
}
