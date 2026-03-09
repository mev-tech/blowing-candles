using BlowingCandles.Application.Services;
using BlowingCandles.Infrastructure.Persistence.Entities;

namespace BlowingCandles.Cli.Handlers;

public sealed class RunRangeHandler
{
    private readonly ISignalRunExecutionService _executionService;

    public RunRangeHandler(ISignalRunExecutionService executionService)
    {
        _executionService = executionService;
    }

    public int Handle(DateOnly startDate, DateOnly endDate)
    {
        var results = _executionService.RunRange("cli", startDate, endDate);

        Console.WriteLine($"Generated signal runs for {results.Count} day(s).");
        return results.All(result => result.Status == SignalRunStatus.Completed) ? 0 : 1;
    }
}
