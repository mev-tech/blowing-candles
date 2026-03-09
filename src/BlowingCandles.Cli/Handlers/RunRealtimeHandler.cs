using BlowingCandles.Application.Services;
using BlowingCandles.Infrastructure.Persistence.Entities;

namespace BlowingCandles.Cli.Handlers;

public sealed class RunRealtimeHandler
{
    private readonly ISignalRunExecutionService _executionService;

    public RunRealtimeHandler(ISignalRunExecutionService executionService)
    {
        _executionService = executionService;
    }

    public int Handle()
    {
        var result = _executionService.RunRealtime("cli");

        Console.WriteLine($"Generated {result.TickerCount} signals.");
        return result.Status == SignalRunStatus.Completed ? 0 : 1;
    }
}
