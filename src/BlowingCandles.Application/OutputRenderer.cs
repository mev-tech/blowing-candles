using BlowingCandles.Domain.Models;

namespace BlowingCandles.Application;

public sealed class OutputRenderer
{
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
        File.WriteAllText(path, SignalsJsonSerializer.Serialize(signals));
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
