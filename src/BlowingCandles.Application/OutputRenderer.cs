using System.Text.Json;
using BlowingCandles.Domain.Models;

namespace BlowingCandles.Application;

public sealed class OutputRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public void WriteSignals(string textFilePath, string jsonFilePath, IEnumerable<FinalSignal> signals)
    {
        var materializedSignals = signals.ToArray();
        WriteText(textFilePath, materializedSignals);
        WriteJson(jsonFilePath, materializedSignals);
    }

    private static void WriteText(string path, IReadOnlyList<FinalSignal> signals)
    {
        EnsureDirectory(path);
        var lines = signals.Select(signal =>
            $"{signal.Ticker}: Action.{signal.Action} | NewsState.{signal.NewsState} | Action.{signal.MarketAction} | {signal.Reason}");
        File.WriteAllLines(path, lines);
    }

    private static void WriteJson(string path, IReadOnlyList<FinalSignal> signals)
    {
        EnsureDirectory(path);

        var payload = signals.Select(signal => new
        {
            ticker = signal.Ticker,
            action = signal.Action.ToString(),
            newsState = signal.NewsState.ToString(),
            marketAction = signal.MarketAction.ToString(),
            reason = signal.Reason,
            timestamp = signal.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz")
        });

        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions) + Environment.NewLine);
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
