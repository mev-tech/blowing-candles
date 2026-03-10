namespace BlowingCandles.Api;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public bool Enabled { get; set; }

    public int IntervalMinutes { get; set; } = 60;
}
