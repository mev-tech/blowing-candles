using BlowingCandles.Application.Services;
using BlowingCandles.Infrastructure.Persistence.Entities;

namespace BlowingCandles.Cli.Handlers;

public sealed class RunAsOfHandler
{
    private readonly ISignalRunExecutionService _executionService;

    public RunAsOfHandler(ISignalRunExecutionService executionService)
    {
        _executionService = executionService;
    }

    public int Handle(DateOnly asOfDate)
    {
        var result = _executionService.RunAsOf("cli", asOfDate);

        Console.WriteLine($"Generated {result.TickerCount} signals for {asOfDate:yyyy-MM-dd}.");
        return result.Status == SignalRunStatus.Completed ? 0 : 1;
    }
}
