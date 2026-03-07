namespace BlowingCandles.Infrastructure.Config;

public sealed record AppConfig
{
    public string[] Watchlist { get; init; } = [];

    public NewsConfig News { get; init; } = new();

    public PolicyConfig Policy { get; init; } = new();

    public OutputConfig Output { get; init; } = new();

    public StateConfig State { get; init; } = new();

    public AuditConfig Audit { get; init; } = new();
}

public sealed record NewsConfig
{
    public string? LocalEarningsCalendar { get; init; }

    public string? ResolvedLocalEarningsCalendar { get; init; }

    public int BlockWindowHours { get; init; } = 48;
}

public sealed record PolicyConfig
{
    public int MaxBuysPerDay { get; init; } = int.MaxValue;

    public int CooldownMinutes { get; init; } = 0;
}

public sealed record OutputConfig
{
    public string TextFile { get; init; } = "signals.txt";

    public string JsonFile { get; init; } = "signals.json";
}

public sealed record StateConfig
{
    public string Path { get; init; } = "data/state.json";
}

public sealed record AuditConfig
{
    public string JsonlPath { get; init; } = "logs/decisions.jsonl";
}
