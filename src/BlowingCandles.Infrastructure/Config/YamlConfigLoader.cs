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
        var rawConfig = _deserializer.Deserialize<RawAppConfig>(reader)
            ?? throw new InvalidDataException($"Unable to deserialize config file '{path}'.");

        return Normalize(rawConfig, path);
    }

    private static AppConfig Normalize(RawAppConfig config, string path)
    {
        if (config.Watchlist is null)
        {
            throw new InvalidDataException("Config file is missing required 'watchlist'.");
        }

        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
        var localEarningsCalendar = NormalizeOptionalString(config.News?.LocalEarningsCalendar);
        var outputTextFile = NormalizeOptionalString(config.Output?.TextFile) ?? "signals.txt";
        var outputJsonFile = NormalizeOptionalString(config.Output?.JsonFile) ?? "signals.json";
        var statePath = NormalizeOptionalString(config.State?.Path) ?? "data/state.json";
        var auditJsonlPath = NormalizeOptionalString(config.Audit?.JsonlPath);

        return new AppConfig
        {
            Watchlist = config.Watchlist
                .Where(ticker => !string.IsNullOrWhiteSpace(ticker))
                .Select(ticker => ticker.Trim().ToUpperInvariant())
                .ToArray(),
            News = (config.News ?? new NewsConfig()) with
            {
                LocalEarningsCalendar = localEarningsCalendar,
                ResolvedLocalEarningsCalendar = localEarningsCalendar is null
                    ? null
                    : ResolvePath(baseDirectory, localEarningsCalendar)
            },
            Output = (config.Output ?? new OutputConfig()) with
            {
                TextFile = ResolvePath(baseDirectory, outputTextFile),
                JsonFile = ResolvePath(baseDirectory, outputJsonFile)
            },
            State = (config.State ?? new StateConfig()) with
            {
                Path = ResolvePath(baseDirectory, statePath)
            },
            Audit = (config.Audit ?? new AuditConfig()) with
            {
                JsonlPath = auditJsonlPath,
                ResolvedJsonlPath = auditJsonlPath is null
                    ? null
                    : ResolvePath(baseDirectory, auditJsonlPath)
            },
            Policy = config.Policy ?? new PolicyConfig()
        };
    }

    private static string? NormalizeOptionalString(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string ResolvePath(string baseDirectory, string value)
    {
        return Path.IsPathRooted(value)
            ? value
            : Path.GetFullPath(Path.Combine(baseDirectory, value));
    }

    private sealed record RawAppConfig
    {
        public string[]? Watchlist { get; init; }

        public NewsConfig? News { get; init; }

        public PolicyConfig? Policy { get; init; }

        public OutputConfig? Output { get; init; }

        public StateConfig? State { get; init; }

        public AuditConfig? Audit { get; init; }
    }
}
