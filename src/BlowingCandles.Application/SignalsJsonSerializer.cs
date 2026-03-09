using System.Text.Json;
using BlowingCandles.Domain.Models;

namespace BlowingCandles.Application;

public sealed record SignalFileEntry(
    string Ticker,
    string Action,
    string NewsState,
    string MarketAction,
    string Reason,
    string Timestamp);

public static class SignalsJsonSerializer
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Serialize(IEnumerable<FinalSignal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        var payload = signals
            .Select(signal => new SignalFileEntry(
                signal.Ticker,
                signal.Action.ToString(),
                signal.NewsState.ToString(),
                signal.MarketAction.ToString(),
                signal.Reason,
                signal.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz")))
            .ToArray();

        return JsonSerializer.Serialize(payload, JsonOptions) + Environment.NewLine;
    }

    public static IReadOnlyList<SignalFileEntry> Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return JsonSerializer.Deserialize<SignalFileEntry[]>(json, JsonOptions)
            ?? throw new JsonException("signals.json did not contain a JSON array.");
    }
}
