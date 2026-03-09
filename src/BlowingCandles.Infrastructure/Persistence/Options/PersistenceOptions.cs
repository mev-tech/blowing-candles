namespace BlowingCandles.Infrastructure.Persistence.Options;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public int CommandTimeoutSeconds { get; init; } = 30;

    public bool EnableDetailedErrors { get; init; }

    public bool EnableSensitiveDataLogging { get; init; }
}
