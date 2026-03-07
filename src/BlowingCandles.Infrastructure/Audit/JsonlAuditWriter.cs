using System.Text.Json;
using BlowingCandles.Domain.Models;

namespace BlowingCandles.Infrastructure.Audit;

public sealed class JsonlAuditWriter
{
    private readonly string _path;

    public JsonlAuditWriter(string path)
    {
        _path = path;
    }

    public void Append(IEnumerable<FinalSignal> signals)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Open(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream);

        foreach (var signal in signals)
        {
            var payload = new
            {
                ticker = signal.Ticker,
                action = $"Action.{signal.Action}",
                news_state = $"NewsState.{signal.NewsState}",
                market_action = $"Action.{signal.MarketAction}",
                reason = signal.Reason,
                timestamp = signal.Timestamp.ToString("O")
            };

            writer.WriteLine(JsonSerializer.Serialize(payload));
        }
    }
}
