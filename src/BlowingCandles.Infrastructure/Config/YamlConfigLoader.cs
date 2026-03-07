using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BlowingCandles.Infrastructure.Config;

public sealed class YamlConfigLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public AppConfig Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Config path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Config file was not found.", path);
        }

        using var reader = File.OpenText(path);
        var rawConfig = _deserializer.Deserialize<AppConfig>(reader)
            ?? throw new InvalidDataException($"Unable to deserialize config file '{path}'.");

        return Normalize(rawConfig, path);
    }

    private static AppConfig Normalize(AppConfig config, string path)
    {
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();

        return config with
        {
            Watchlist = config.Watchlist
                .Select(ticker => ticker.Trim().ToUpperInvariant())
                .Where(ticker => !string.IsNullOrWhiteSpace(ticker))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            News = (config.News ?? new NewsConfig()) with
            {
                LocalEarningsCalendar = ResolvePath(baseDirectory, config.News?.LocalEarningsCalendar ?? "earnings_calendar.json")
            },
            Output = (config.Output ?? new OutputConfig()) with
            {
                TextFile = ResolvePath(baseDirectory, config.Output?.TextFile ?? "signals.txt"),
                JsonFile = ResolvePath(baseDirectory, config.Output?.JsonFile ?? "signals.json")
            },
            State = (config.State ?? new StateConfig()) with
            {
                Path = ResolvePath(baseDirectory, config.State?.Path ?? "data/state.json")
            },
            Audit = (config.Audit ?? new AuditConfig()) with
            {
                JsonlPath = ResolvePath(baseDirectory, config.Audit?.JsonlPath ?? "logs/decisions.jsonl")
            },
            Policy = config.Policy ?? new PolicyConfig()
        };
    }

    private static string ResolvePath(string baseDirectory, string value)
    {
        return Path.IsPathRooted(value)
            ? value
            : Path.GetFullPath(Path.Combine(baseDirectory, value));
    }
}
