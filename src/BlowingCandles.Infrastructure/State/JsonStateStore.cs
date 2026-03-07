using System.Text.Json;
using System.Text.Json.Serialization;
using BlowingCandles.Domain.Interfaces;

namespace BlowingCandles.Infrastructure.State;

public sealed class JsonStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public JsonStateStore(string path)
    {
        _path = path;
    }

    public StateSnapshot Load(IClock clock)
    {
        if (!File.Exists(_path))
        {
            return Empty(clock);
        }

        try
        {
            var json = File.ReadAllText(_path);
            var state = JsonSerializer.Deserialize<StateSnapshot>(json, JsonOptions);
            return ResetIfNewDay(state ?? Empty(clock), clock);
        }
        catch
        {
            return Empty(clock);
        }
    }

    public void Save(StateSnapshot state)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(state, JsonOptions) + Environment.NewLine);
    }

    private StateSnapshot ResetIfNewDay(StateSnapshot state, IClock clock)
    {
        var currentDay = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).ToString("yyyy-MM-dd");
        return state.Day == currentDay
            ? state
            : Empty(clock);
    }

    public StateSnapshot RecordBuy(StateSnapshot state, DateTimeOffset whenUtc)
    {
        return state with
        {
            Day = DateOnly.FromDateTime(whenUtc.UtcDateTime).ToString("yyyy-MM-dd"),
            BuysToday = state.BuysToday + 1,
            LastBuyAt = whenUtc.ToUniversalTime()
        };
    }

    private static StateSnapshot Empty(IClock clock)
    {
        return new StateSnapshot
        {
            Day = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).ToString("yyyy-MM-dd"),
            BuysToday = 0,
            LastBuyAt = null
        };
    }

    public sealed record StateSnapshot
    {
        [JsonPropertyName("day")]
        public string Day { get; init; } = string.Empty;

        [JsonPropertyName("buys_today")]
        public int BuysToday { get; init; }

        [JsonPropertyName("last_buy_at")]
        public DateTimeOffset? LastBuyAt { get; init; }
    }
}
