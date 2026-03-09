namespace BlowingCandles.Infrastructure.Persistence.Entities;

public sealed class SignalRunEntity
{
    public long Id { get; set; }

    public SignalRunType RunType { get; set; }

    public string Trigger { get; set; } = null!;

    public SignalRunStatus Status { get; set; }

    public DateOnly AsOfDate { get; set; }

    public bool IsSimulation { get; set; }

    public int TickerCount { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? ErrorMessage { get; set; }

    public List<SignalRunResultEntity> Results { get; } = [];
}
