using BlowingCandles.Domain.Enums;
using BlowingCandles.Infrastructure.Persistence;
using BlowingCandles.Infrastructure.Persistence.Entities;
using BlowingCandles.Infrastructure.Persistence.Services;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Infrastructure.Tests;

[Collection(PostgresContainerCollection.Name)]
public sealed class SignalRunReadServiceTests
{
    private readonly PostgresContainerFixture _fixture;

    public SignalRunReadServiceTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetLatestLiveRun_ReturnsMostRecentCompletedLiveRunWithSignals()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        await SeedRunAsync(
            dbContext,
            SignalRunType.Realtime,
            false,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 8),
            new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 20, 1, 0, TimeSpan.Zero),
            "AAPL");
        await SeedRunAsync(
            dbContext,
            SignalRunType.AsOf,
            true,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 9),
            new DateTimeOffset(2026, 3, 9, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 9, 8, 1, 0, TimeSpan.Zero),
            "MSFT");
        await SeedRunAsync(
            dbContext,
            SignalRunType.Realtime,
            false,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 9),
            new DateTimeOffset(2026, 3, 9, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 9, 20, 2, 0, TimeSpan.Zero),
            "NVDA");
        var service = new SignalRunReadService(dbContext);

        var run = service.GetLatestLiveRun();

        Assert.NotNull(run);
        Assert.Equal(SignalRunType.Realtime, run.RunType);
        Assert.Equal(new DateOnly(2026, 3, 9), run.AsOfDate);
        Assert.Equal("NVDA", Assert.Single(run.Signals).Ticker);
    }

    [Fact]
    public async Task GetLatestLiveRun_WithoutCompletedLiveRun_ReturnsNull()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        await SeedRunAsync(
            dbContext,
            SignalRunType.Realtime,
            false,
            SignalRunStatus.Failed,
            new DateOnly(2026, 3, 8),
            new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 20, 1, 0, TimeSpan.Zero),
            "AAPL");
        await SeedRunAsync(
            dbContext,
            SignalRunType.AsOf,
            true,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 9),
            new DateTimeOffset(2026, 3, 9, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 9, 8, 1, 0, TimeSpan.Zero),
            "MSFT");
        var service = new SignalRunReadService(dbContext);

        Assert.Null(service.GetLatestLiveRun());
    }

    [Fact]
    public async Task GetRunById_ReturnsSignalsAndMissingRunReturnsNull()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        var runId = await SeedRunAsync(
            dbContext,
            SignalRunType.Realtime,
            false,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 8),
            new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 20, 1, 0, TimeSpan.Zero),
            "AAPL",
            "MSFT");
        var service = new SignalRunReadService(dbContext);

        var run = service.GetRunById(runId);

        Assert.NotNull(run);
        Assert.Equal(runId, run.RunId);
        Assert.Equal(["AAPL", "MSFT"], run.Signals.Select(x => x.Ticker).ToArray());
        Assert.Null(service.GetRunById(runId + 1));
    }

    [Fact]
    public async Task GetRecentRuns_ReturnsMostRecentRunsByStartedAtDescending()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        await SeedRunAsync(
            dbContext,
            SignalRunType.Realtime,
            false,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 7),
            new DateTimeOffset(2026, 3, 7, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 7, 20, 1, 0, TimeSpan.Zero),
            "AAPL");
        await SeedRunAsync(
            dbContext,
            SignalRunType.Realtime,
            false,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 8),
            new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 20, 1, 0, TimeSpan.Zero),
            "MSFT");
        await SeedRunAsync(
            dbContext,
            SignalRunType.AsOf,
            true,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 9),
            new DateTimeOffset(2026, 3, 9, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 9, 20, 1, 0, TimeSpan.Zero),
            "NVDA");
        var service = new SignalRunReadService(dbContext);

        var runs = service.GetRecentRuns(2);

        Assert.Equal(2, runs.Count);
        Assert.Equal(new DateOnly(2026, 3, 9), runs[0].AsOfDate);
        Assert.Equal(new DateOnly(2026, 3, 8), runs[1].AsOfDate);
    }

    [Fact]
    public async Task GetRecentRuns_ExcessiveLimit_CapsAtMaximum()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        await SeedRunAsync(
            dbContext,
            SignalRunType.Realtime,
            false,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 7),
            new DateTimeOffset(2026, 3, 7, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 7, 20, 1, 0, TimeSpan.Zero),
            "AAPL");
        await SeedRunAsync(
            dbContext,
            SignalRunType.Realtime,
            false,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 8),
            new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 20, 1, 0, TimeSpan.Zero),
            "MSFT");
        await SeedRunAsync(
            dbContext,
            SignalRunType.AsOf,
            true,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 9),
            new DateTimeOffset(2026, 3, 9, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 9, 20, 1, 0, TimeSpan.Zero),
            "NVDA");
        var service = new SignalRunReadService(dbContext);

        var runs = service.GetRecentRuns(500);

        Assert.Equal(3, runs.Count);
        Assert.Equal(new DateOnly(2026, 3, 9), runs[0].AsOfDate);
        Assert.Equal(new DateOnly(2026, 3, 8), runs[1].AsOfDate);
        Assert.Equal(new DateOnly(2026, 3, 7), runs[2].AsOfDate);
    }

    private static async Task<long> SeedRunAsync(
        AppDbContext dbContext,
        SignalRunType runType,
        bool isSimulation,
        SignalRunStatus status,
        DateOnly asOfDate,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        params string[] tickers)
    {
        var run = new SignalRunEntity
        {
            RunType = runType,
            Trigger = "cli",
            Status = status,
            AsOfDate = asOfDate,
            IsSimulation = isSimulation,
            TickerCount = tickers.Length,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            ErrorMessage = status == SignalRunStatus.Failed ? "failed" : null
        };

        foreach (var ticker in tickers)
        {
            run.Results.Add(
                new SignalRunResultEntity
                {
                    Run = run,
                    Ticker = ticker,
                    Action = TradingAction.BUY,
                    NewsState = NewsState.TRADE_OK,
                    MarketAction = TradingAction.BUY,
                    Reason = string.Empty,
                    Timestamp = completedAtUtc
                });
        }

        dbContext.SignalRuns.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }
}
