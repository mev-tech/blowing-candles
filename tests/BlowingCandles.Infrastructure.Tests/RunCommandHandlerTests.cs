using BlowingCandles.Application.Services;
using BlowingCandles.Cli.Handlers;
using BlowingCandles.Infrastructure.Persistence.Entities;
using BlowingCandles.Infrastructure.Persistence.Models;

namespace BlowingCandles.Infrastructure.Tests;

[Collection("Console")]
public sealed class RunCommandHandlerTests
{
    [Fact]
    public void RunRealtime_DelegatesToExecutionService_ReturnsZeroOnCompletedRun()
    {
        var service = new FakeSignalRunExecutionService
        {
            RealtimeResult = CreateResult(SignalRunType.Realtime, SignalRunStatus.Completed, new DateOnly(2026, 3, 7), 2)
        };
        var handler = new RunRealtimeHandler(service);

        var (exitCode, output) = Invoke(() => handler.Handle());

        Assert.Equal(0, exitCode);
        Assert.Equal("Generated 2 signals." + Environment.NewLine, output);
        Assert.Equal(["cli"], service.RealtimeTriggers);
    }

    [Fact]
    public void RunAsOf_DelegatesToExecutionService_ReturnsOneOnFailedRun()
    {
        var asOfDate = new DateOnly(2026, 1, 15);
        var service = new FakeSignalRunExecutionService
        {
            AsOfResult = CreateResult(SignalRunType.AsOf, SignalRunStatus.Failed, asOfDate, 1)
        };
        var handler = new RunAsOfHandler(service);

        var (exitCode, output) = Invoke(() => handler.Handle(asOfDate));

        Assert.Equal(1, exitCode);
        Assert.Equal("Generated 1 signals for 2026-01-15." + Environment.NewLine, output);
        Assert.Equal([("cli", asOfDate)], service.AsOfRequests);
    }

    [Fact]
    public void RunRange_DelegatesToExecutionService_ReturnsOneWhenAnyDayFails()
    {
        var startDate = new DateOnly(2026, 1, 15);
        var endDate = new DateOnly(2026, 1, 16);
        var service = new FakeSignalRunExecutionService
        {
            RangeResults =
            [
                CreateResult(SignalRunType.Range, SignalRunStatus.Completed, startDate, 1),
                CreateResult(SignalRunType.Range, SignalRunStatus.Failed, endDate, 1)
            ]
        };
        var handler = new RunRangeHandler(service);

        var (exitCode, output) = Invoke(() => handler.Handle(startDate, endDate));

        Assert.Equal(1, exitCode);
        Assert.Equal("Generated signal runs for 2 day(s)." + Environment.NewLine, output);
        Assert.Equal([("cli", startDate, endDate)], service.RangeRequests);
    }

    private static SignalRunExecutionResult CreateResult(
        SignalRunType runType,
        SignalRunStatus status,
        DateOnly asOfDate,
        int tickerCount)
    {
        return new SignalRunExecutionResult(
            1L,
            runType,
            "cli",
            status,
            asOfDate,
            runType != SignalRunType.Realtime,
            new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero),
            tickerCount,
            CreateSignals(tickerCount),
            status == SignalRunStatus.Completed ? null : "failed");
    }

    private static IReadOnlyList<SignalRunSignalResult> CreateSignals(int tickerCount)
    {
        var signals = new List<SignalRunSignalResult>(tickerCount);

        for (var index = 0; index < tickerCount; index++)
        {
            signals.Add(
                new SignalRunSignalResult(
                    $"TICKER{index + 1}",
                    Domain.Enums.Action.WAIT,
                    Domain.Enums.NewsState.WAIT,
                    Domain.Enums.Action.WAIT,
                    "TEST",
                    new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero)));
        }

        return signals;
    }

    private static (int ExitCode, string Output) Invoke(Func<int> action)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);
            var exitCode = action();
            writer.Flush();
            return (exitCode, writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private sealed class FakeSignalRunExecutionService : ISignalRunExecutionService
    {
        public SignalRunExecutionResult RealtimeResult { get; init; } = null!;

        public SignalRunExecutionResult AsOfResult { get; init; } = null!;

        public IReadOnlyList<SignalRunExecutionResult> RangeResults { get; init; } = [];

        public List<string> RealtimeTriggers { get; } = [];

        public List<(string Trigger, DateOnly AsOfDate)> AsOfRequests { get; } = [];

        public List<(string Trigger, DateOnly StartDate, DateOnly EndDate)> RangeRequests { get; } = [];

        public SignalRunExecutionResult RunRealtime(string trigger)
        {
            RealtimeTriggers.Add(trigger);
            return RealtimeResult;
        }

        public SignalRunExecutionResult RunAsOf(string trigger, DateOnly asOfDate)
        {
            AsOfRequests.Add((trigger, asOfDate));
            return AsOfResult;
        }

        public IReadOnlyList<SignalRunExecutionResult> RunRange(string trigger, DateOnly startDate, DateOnly endDate)
        {
            RangeRequests.Add((trigger, startDate, endDate));
            return RangeResults;
        }
    }
}
